using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using Scrapper.Models.Common;
using Scrapper.Models.DTOs;
using Scrapper.Models.Enums;
using Scrapper.Services.Interfaces;
using Scrapper.Utils.Constants;

namespace Scrapper.Services.Implementations;

public class ScraperEngine(
    IHtmlFetcher htmlFetcher,
    IUrlValidationService urlValidationService,
    IRecordDetector recordDetector,
    IStructuredDataParser structuredDataParser,
    IProfileLinkDetector profileLinkDetector,
    IFieldExtractionOrchestrator extractionOrchestrator,
    IProfileFieldMerger profileFieldMerger,
    ILogger<ScraperEngine> logger) : IScraperEngine
{
    public async Task<ScrapeResultDto> ScrapeAsync(ScraperConfigDto config, CancellationToken cancellationToken = default)
    {
        var result = new ScrapeResultDto
        {
            FieldNames = config.Fields.Select(f => f.Name).ToList()
        };

        if (config.Fields.Count == 0)
        {
            result.Errors.Add("At least one field must be configured.");
            return result;
        }

        var validation = await urlValidationService.ValidateAsync(config.Url, cancellationToken);
        if (!validation.IsValid)
        {
            result.Errors.Add(validation.Reason ?? "Unable to access the URL.");
            return result;
        }

        var firstPageUrl = validation.NormalizedUrl ?? config.Url;
        string html;
        try
        {
            html = await FetchPageAsync(firstPageUrl, config, cancellationToken);
        }
        catch (ScrapeException ex)
        {
            result.Errors.Add(ex.Message);
            return result;
        }
        catch (SsrfViolationException ex)
        {
            result.Errors.Add(ex.Message);
            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error while fetching {Url}", config.Url);
            result.Errors.Add("Unable to access the URL.");
            return result;
        }

        try
        {
            await CrawlAsync(html, firstPageUrl, config, result, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error while extracting fields for {Url}", config.Url);
            result.Errors.Add("Some fields could not be extracted.");
        }

        return result;
    }

    /// <summary>
    /// Walks the listing across pages (if pagination is configured), extracting records per
    /// page and — when enabled — visiting each record's own detail/profile page to fill in
    /// fields the listing page doesn't show (a bio, qualifications, ...). Profile data always
    /// overrides listing data for the same field, since the profile page is the more
    /// detailed source.
    /// </summary>
    private async Task CrawlAsync(string firstPageHtml, string firstPageUrl, ScraperConfigDto config, ScrapeResultDto result, CancellationToken cancellationToken)
    {
        var maxRecords = Math.Min(config.MaxRecords, ScrapingLimits.MaxRecordsHardCap);
        var maxPages = Math.Clamp(config.MaxPages, 1, ScrapingLimits.MaxPagesHardCap);
        var maxProfiles = config.EnableProfileCrawl ? Math.Min(config.MaxProfiles, ScrapingLimits.MaxProfilesHardCap) : 0;

        var allRecords = new List<ScrapedRecordDto>();
        var visitedListingUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { firstPageUrl };
        var visitedProfileUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var currentHtml = firstPageHtml;
        var currentUrl = firstPageUrl;
        var pageCount = 0;
        var profilesVisited = 0;
        var anyAutoDetected = false;
        var lastTotalDetected = 0;

        while (true)
        {
            pageCount++;
            var pageUri = new Uri(currentUrl);
            var doc = new HtmlDocument();
            doc.LoadHtml(currentHtml);

            List<HtmlNode> recordNodes;
            bool autoDetected;

            if (!string.IsNullOrWhiteSpace(config.RecordSelector))
            {
                recordNodes = HtmlExtractionUtils.SelectNodes(doc.DocumentNode, config.RecordSelector, config.RecordSelectorType);
                autoDetected = false;

                if (recordNodes.Count == 0 && pageCount == 1)
                {
                    result.Warnings.Add("The record selector did not match any elements.");
                    return;
                }
            }
            else
            {
                var detected = recordDetector.DetectRecords(doc.DocumentNode, config.Fields);
                recordNodes = detected ?? (pageCount == 1 ? [doc.DocumentNode] : []);
                autoDetected = detected is not null;
            }

            anyAutoDetected |= autoDetected;
            lastTotalDetected = recordNodes.Count;

            var structuredDataObjects = structuredDataParser.Parse(doc.DocumentNode);
            var remainingCapacity = maxRecords - allRecords.Count;
            var pageRecordNodes = recordNodes.Take(Math.Max(0, remainingCapacity)).ToList();

            var pageRecords = await ExtractRecordsAsync(
                pageRecordNodes, doc.DocumentNode, structuredDataObjects, config.Fields, pageUri, cancellationToken);

            // Auto-detection can occasionally lock onto a repeated *sub*-widget within a single
            // entity's page (e.g. skill-rating bars on a specialist profile) rather than a
            // genuine list of distinct entities. A tell-tale sign: every "record" produces the
            // same (or no) value for every field — real, distinct records virtually never do
            // that. When it happens on the first page, collapse back to a single whole-page
            // record instead of returning misleading pseudo-records.
            if (pageCount == 1 && autoDetected && pageRecords.Count > 1 && !AnyFieldVariesAcrossRecords(pageRecords, config.Fields))
            {
                anyAutoDetected = false;
                lastTotalDetected = 1;
                pageRecords = await ExtractRecordsAsync(
                    [doc.DocumentNode], doc.DocumentNode, structuredDataObjects, config.Fields, pageUri, cancellationToken);
            }

            foreach (var record in pageRecords)
            {
                record.PageNumber = pageCount;
            }

            if (config.EnableProfileCrawl)
            {
                foreach (var record in pageRecords)
                {
                    if (profilesVisited >= maxProfiles || record.ProfileUrl is null)
                    {
                        continue;
                    }

                    if (!visitedProfileUrls.Add(record.ProfileUrl))
                    {
                        continue; // Already visited this exact profile for an earlier record.
                    }

                    // A deep crawl can easily fire hundreds of requests at one site in a short
                    // window — exactly what triggers a site's own bot protection (HTTP 429).
                    // Spacing profile fetches out proactively gets far more of a large crawl
                    // through than firing them back-to-back as fast as possible.
                    if (profilesVisited > 0)
                    {
                        await Task.Delay(ScrapingLimits.PoliteCrawlDelayMs, cancellationToken);
                    }

                    var visited = await TryCrawlProfileAsync(record, config, cancellationToken, result);
                    if (visited)
                    {
                        profilesVisited++;
                    }
                }
            }

            allRecords.AddRange(pageRecords);

            if (allRecords.Count >= maxRecords || pageCount >= maxPages)
            {
                break;
            }

            var nextPageUrl = profileLinkDetector.DetectNextPageUrl(doc.DocumentNode, pageUri);
            if (nextPageUrl is null || !visitedListingUrls.Add(nextPageUrl))
            {
                break;
            }

            var nextValidation = await urlValidationService.ValidateAsync(nextPageUrl, cancellationToken);
            if (!nextValidation.IsValid || !IsSameHost(nextValidation.NormalizedUrl ?? nextPageUrl, pageUri))
            {
                break;
            }

            currentUrl = nextValidation.NormalizedUrl ?? nextPageUrl;
            var nextPageHtml = await TryFetchNextPageWithCooldownAsync(currentUrl, config, cancellationToken);
            if (nextPageHtml is null)
            {
                break;
            }

            currentHtml = nextPageHtml;
        }

        result.RecordSelectorAutoDetected = anyAutoDetected;
        result.PagesProcessed = pageCount;
        result.ProfilesVisited = profilesVisited;

        foreach (var field in config.Fields.Where(f => f.Required))
        {
            if (allRecords.Any(r => r.MissingFields.Contains(field.Name)))
            {
                result.Warnings.Add($"Required field '{field.Name}' could not be extracted for one or more records.");
            }
        }

        // Surface the discovered profile link as an ordinary field so it shows up in the
        // results table and CSV export the same way as any other requested field.
        if (config.EnableProfileCrawl && allRecords.Any(r => r.ProfileUrl is not null))
        {
            const string profileUrlFieldName = "Profile URL";
            result.FieldNames.Add(profileUrlFieldName);
            foreach (var record in allRecords)
            {
                record.Fields[profileUrlFieldName] = record.ProfileUrl;
            }
        }

        result.Records = allRecords;
        result.TotalRecords = allRecords.Count;

        if (allRecords.Count == 0)
        {
            result.Warnings.Add("No records were found.");
        }
        else if (pageCount == 1 && lastTotalDetected > maxRecords)
        {
            result.Warnings.Add($"Only the first {maxRecords} records were processed out of {lastTotalDetected} found.");
        }
    }

    /// <summary>
    /// Fetches one record's profile page and merges its extracted fields into that record.
    /// Profile values always override listing values for the same field. Any failure
    /// (blocked URL, timeout, HTTP error) is recorded as a warning and the record simply
    /// keeps whatever it already had from the listing page — it never aborts the scrape.
    /// </summary>
    private async Task<bool> TryCrawlProfileAsync(
        ScrapedRecordDto record, ScraperConfigDto config, CancellationToken cancellationToken, ScrapeResultDto result)
    {
        var profileUrl = record.ProfileUrl!;
        var label = record.Fields.Values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? profileUrl;

        var validation = await urlValidationService.ValidateAsync(profileUrl, cancellationToken);
        if (!validation.IsValid)
        {
            result.Warnings.Add($"Skipped the profile page for '{label}': {validation.Reason ?? "URL not allowed"}.");
            return false;
        }

        string profileHtml;
        try
        {
            profileHtml = await FetchPageAsync(validation.NormalizedUrl ?? profileUrl, config, cancellationToken);
        }
        catch (Exception ex) when (ex is ScrapeException or SsrfViolationException)
        {
            result.Warnings.Add($"Could not load the profile page for '{label}': {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unexpected error fetching profile page {Url}", profileUrl);
            result.Warnings.Add($"Could not load the profile page for '{label}'.");
            return false;
        }

        var profileDoc = new HtmlDocument();
        profileDoc.LoadHtml(profileHtml);
        await profileFieldMerger.MergeProfileFieldsAsync(record, profileDoc.DocumentNode, config.Fields, cancellationToken);

        return true;
    }

    private async Task<List<ScrapedRecordDto>> ExtractRecordsAsync(
        IReadOnlyList<HtmlNode> recordNodes,
        HtmlNode documentNode,
        IReadOnlyList<Dictionary<string, object?>> structuredDataObjects,
        List<FieldDefinitionDto> fields,
        Uri pageUri,
        CancellationToken cancellationToken)
    {
        recordNodes = FilterToMajorityEntityType(recordNodes, structuredDataObjects, pageUri);

        var records = new List<ScrapedRecordDto>();
        var index = 0;

        foreach (var recordNode in recordNodes)
        {
            var context = new Models.ExtractionContext
            {
                RecordNode = recordNode,
                DocumentNode = documentNode,
                RecordIndex = index,
                TotalRecords = recordNodes.Count,
                StructuredDataObjects = structuredDataObjects,
            };

            var record = new ScrapedRecordDto();

            foreach (var field in fields)
            {
                var fieldResult = await extractionOrchestrator.ExtractFieldAsync(context, field, cancellationToken);
                record.Fields[field.Name] = fieldResult.Value;
                record.FieldResults[field.Name] = fieldResult;

                if (fieldResult.IsMissing)
                {
                    record.MissingFields.Add(field.Name);
                }
            }

            // Only look for a profile link when this record is scoped to something narrower
            // than the whole document — a whole-page "record" has no meaningful sub-link to
            // "its own" detail page.
            if (!ReferenceEquals(recordNode, documentNode))
            {
                record.ProfileUrl = profileLinkDetector.DetectProfileUrl(recordNode, pageUri);
            }

            records.Add(record);
            index++;
        }

        return records;
    }

    /// <summary>
    /// Some directory sites render a different kind of entity (e.g. an affiliated hospital or
    /// clinic promo card) through the exact same reusable card component as the doctor/person
    /// cards the user actually wants — structurally indistinguishable, so RecordDetector
    /// legitimately groups them together. Each candidate's own detected profile link is
    /// matched against the page's JSON-LD entities by URL (far more reliable than assuming
    /// list position lines up with JSON-LD block order, which frequently doesn't hold — a
    /// page can publish extra unrelated blocks, or omit one, without affecting card order). A
    /// clear majority type (e.g. mostly "Physician") among the confidently-matched candidates
    /// is a strong signal that a minority (e.g. one "Hospital") doesn't belong in the results.
    /// Candidates with no confident match are kept (benefit of doubt); a tie or too few
    /// confident matches leaves everything untouched rather than guessing.
    /// </summary>
    private IReadOnlyList<HtmlNode> FilterToMajorityEntityType(
        IReadOnlyList<HtmlNode> nodes, IReadOnlyList<Dictionary<string, object?>> structuredData, Uri pageUri)
    {
        if (nodes.Count <= 1 || structuredData.Count == 0)
        {
            return nodes;
        }

        var entityByUrl = new Dictionary<string, Dictionary<string, object?>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in structuredData)
        {
            var url = (entity.GetValueOrDefault("url") ?? entity.GetValueOrDefault("@id"))?.ToString();
            var normalized = NormalizeUrlForComparison(url);
            if (normalized is not null)
            {
                entityByUrl.TryAdd(normalized, entity);
            }
        }

        if (entityByUrl.Count == 0)
        {
            return nodes;
        }

        var matchedTypes = nodes
            .Select(node =>
            {
                var link = NormalizeUrlForComparison(profileLinkDetector.DetectProfileUrl(node, pageUri));
                return link is not null && entityByUrl.TryGetValue(link, out var entity)
                    ? entity.GetValueOrDefault("@type")?.ToString()
                    : null;
            })
            .ToList();

        var confidentMatches = matchedTypes.Count(t => !string.IsNullOrEmpty(t));
        if (confidentMatches < 2)
        {
            return nodes;
        }

        var majority = matchedTypes
            .Where(t => !string.IsNullOrEmpty(t))
            .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .First();

        if (majority.Count() <= confidentMatches / 2)
        {
            return nodes;
        }

        var keepIndexes = Enumerable.Range(0, nodes.Count)
            .Where(i => matchedTypes[i] is null || string.Equals(matchedTypes[i], majority.Key, StringComparison.OrdinalIgnoreCase))
            .ToHashSet();

        return keepIndexes.Count == nodes.Count ? nodes : nodes.Where((_, i) => keepIndexes.Contains(i)).ToList();
    }

    private static string? NormalizeUrlForComparison(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var path = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.AbsolutePath : url;
        return path.TrimEnd('/').ToLowerInvariant();
    }

    private static bool AnyFieldVariesAcrossRecords(List<ScrapedRecordDto> records, List<FieldDefinitionDto> fields)
    {
        foreach (var field in fields)
        {
            var distinctValues = records
                .Select(r => r.Fields.GetValueOrDefault(field.Name))
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct()
                .Count();

            if (distinctValues > 1)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Dispatches to the plain HTTP fetch or the headless-browser rendering fetch depending on
    /// <see cref="ScraperConfigDto.EnableJavaScriptRendering"/> — the only place that decision
    /// is made, so every page fetch in a scrape (listing, pagination, profile) is consistent.
    /// </summary>
    private Task<string> FetchPageAsync(string url, ScraperConfigDto config, CancellationToken cancellationToken) =>
        config.EnableJavaScriptRendering
            ? htmlFetcher.FetchRenderedAsync(url, config.TimeoutSeconds, cancellationToken)
            : htmlFetcher.FetchAsync(url, config.TimeoutSeconds, cancellationToken);

    /// <summary>
    /// Fetches the next listing page, giving a sustained rate-limit block (HTTP 429) real
    /// time to lift rather than giving up after one failed attempt. Losing this one fetch ends
    /// the entire rest of a multi-page crawl, so it's worth pausing for longer here than the
    /// fetcher's own short in-request retry — each attempt below gets a fresh per-request
    /// timeout budget, which a single fetch's own retry loop can't do without exceeding its
    /// configured timeout. A non-rate-limit failure (a real 404/500/timeout) still gives up
    /// immediately, matching the previous behaviour.
    /// </summary>
    private async Task<string?> TryFetchNextPageWithCooldownAsync(string url, ScraperConfigDto config, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await Task.Delay(ScrapingLimits.PoliteCrawlDelayMs, cancellationToken);
                return await FetchPageAsync(url, config, cancellationToken);
            }
            catch (RateLimitedException) when (attempt < ScrapingLimits.MaxPageRateLimitCooldownRetries)
            {
                logger.LogWarning(
                    "Rate-limited fetching next listing page {Url}, cooling down for {Seconds}s before retrying (attempt {Attempt})",
                    url, ScrapingLimits.PageRateLimitCooldownSeconds, attempt + 1);
                await Task.Delay(TimeSpan.FromSeconds(ScrapingLimits.PageRateLimitCooldownSeconds), cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to fetch next listing page {Url}", url);
                return null;
            }
        }
    }

    private static bool IsSameHost(string url, Uri reference)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
               && string.Equals(uri.Host, reference.Host, StringComparison.OrdinalIgnoreCase);
    }
}
