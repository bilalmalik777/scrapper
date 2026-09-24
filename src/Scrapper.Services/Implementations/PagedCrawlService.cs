using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using Scrapper.Models.DTOs;
using Scrapper.Models.Enums;
using Scrapper.Services.Interfaces;
using Scrapper.Utils.Constants;
using Scrapper.Utils.Helpers;

namespace Scrapper.Services.Implementations;

/// <summary>
/// The crawl lifecycle for a paged job: persistent session, human-paced delays, retry with
/// backoff, per-page checkpointing, and clean stop-on-block — see <see cref="IPagedCrawlService"/>.
/// Page fetching goes through <see cref="ICrawlHttpSession"/>; page-to-records extraction goes
/// through the untouched <see cref="IListingPageExtractor"/> pipeline. This class owns none of
/// the selector/parsing/extraction logic itself, only the fetch-retry-checkpoint loop around it.
/// </summary>
public class PagedCrawlService(
    ICrawlStateStore stateStore,
    ICrawlHttpSessionFactory sessionFactory,
    IListingPageExtractor pageExtractor,
    IProfileFieldMerger profileFieldMerger,
    ILogger<PagedCrawlService> logger) : IPagedCrawlService
{
    public async Task RunAsync(string jobId, CancellationToken cancellationToken)
    {
        var state = await RequireStateAsync(jobId, cancellationToken);
        state.Status = CrawlJobStatus.Running;
        state.LastError = null;
        await stateStore.SaveAsync(state, cancellationToken);

        using var session = sessionFactory.Create(state.Config.UserAgent, state.Config.TimeoutSeconds, state.Config.EnableJavaScriptRendering);
        var totalPages = Math.Clamp(state.TotalPages, 1, ScrapingLimits.MaxCrawlTotalPages);

        try
        {
            for (var page = state.LastCompletedPage + 1; page <= totalPages; page++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await DelayBeforePageAsync(state.Config, page, cancellationToken);

                var outcome = await ProcessPageAsync(session, state, page, cancellationToken);
                if (outcome == PageOutcome.StopCrawl)
                {
                    return;
                }

                state.LastCompletedPage = page;
                await stateStore.SaveAsync(state, cancellationToken);
            }

            state.Status = CrawlJobStatus.Completed;
            state.CurrentPage = null;
            await stateStore.SaveAsync(state, cancellationToken);
            logger.LogInformation("Crawl job {JobId} completed: {RecordsSaved} records across {TotalPages} pages", jobId, state.RecordsSaved, totalPages);
        }
        catch (OperationCanceledException)
        {
            state.Status = CrawlJobStatus.Paused;
            await stateStore.SaveAsync(state, CancellationToken.None);
            logger.LogInformation("Crawl job {JobId} paused at page {Page} (cancelled/shutting down) — resumable", jobId, state.CurrentPage);
        }
    }

    public async Task RetryFailedAsync(string jobId, CancellationToken cancellationToken)
    {
        var state = await RequireStateAsync(jobId, cancellationToken);
        var pagesToRetry = state.FailedPages.OrderBy(p => p).ToList();
        if (pagesToRetry.Count == 0)
        {
            return;
        }

        state.Status = CrawlJobStatus.Running;
        state.LastError = null;
        await stateStore.SaveAsync(state, cancellationToken);

        using var session = sessionFactory.Create(state.Config.UserAgent, state.Config.TimeoutSeconds, state.Config.EnableJavaScriptRendering);

        try
        {
            foreach (var page in pagesToRetry)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await DelayBeforePageAsync(state.Config, page, cancellationToken);
                await ProcessPageAsync(session, state, page, cancellationToken);
                await stateStore.SaveAsync(state, cancellationToken);
            }

            state.Status = state.FailedPages.Count == 0 && state.LastCompletedPage >= state.TotalPages
                ? CrawlJobStatus.Completed
                : CrawlJobStatus.Paused;
            state.CurrentPage = null;
            await stateStore.SaveAsync(state, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            state.Status = CrawlJobStatus.Paused;
            await stateStore.SaveAsync(state, CancellationToken.None);
        }
    }

    private enum PageOutcome { Continue, StopCrawl }

    /// <summary>
    /// Fetches, extracts, and checkpoints one page. A page is only ever marked complete (and
    /// its records only ever saved) after the fetch AND extraction AND persistence all
    /// succeeded — never before — so a crash can't leave a checkpoint pointing past data that
    /// was never actually written.
    /// </summary>
    private async Task<PageOutcome> ProcessPageAsync(ICrawlHttpSession session, CrawlJobStateDto state, int page, CancellationToken cancellationToken)
    {
        state.CurrentPage = page;
        var startedAt = DateTimeOffset.UtcNow;
        var url = PagedUrlBuilder.BuildPageUrl(state.Config.Url, page);
        var referer = page > 1 ? PagedUrlBuilder.BuildPageUrl(state.Config.Url, page - 1) : null;

        var (result, attempts) = await FetchWithRetryAsync(session, url, referer, state.Config, page, cancellationToken);
        var elapsed = DateTimeOffset.UtcNow - startedAt;

        if (!result.IsSuccess)
        {
            state.FailedPages.Remove(page);
            state.FailedPages.Add(page);
            state.LastError = $"Page {page}: {result.Outcome} — {result.Detail}";
            await stateStore.SaveAsync(state, cancellationToken);

            var stopCleanly = result.Outcome == Interfaces.PageFetchOutcome.Forbidden || state.Config.StopOnPersistentFailure;
            if (stopCleanly)
            {
                state.Status = CrawlJobStatus.Blocked;
                state.CurrentPage = null;
                await stateStore.SaveAsync(state, cancellationToken);
                logger.LogWarning(
                    "Page: {Page}/{TotalPages} Status: {Outcome} Reason: {Detail} Action: saving checkpoint and stopping",
                    page, state.TotalPages, result.Outcome, result.Detail);
                return PageOutcome.StopCrawl;
            }

            logger.LogWarning(
                "Page: {Page}/{TotalPages} Status: {Outcome} Reason: {Detail} Action: recording as failed and continuing",
                page, state.TotalPages, result.Outcome, result.Detail);
            return PageOutcome.Continue;
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(result.Html!);
        var pageUri = new Uri(url);

        List<ScrapedRecordDto> records;
        try
        {
            records = await pageExtractor.ExtractAsync(
                doc.DocumentNode, pageUri, state.Config.RecordSelector, state.Config.RecordSelectorType, state.Config.Fields, cancellationToken);
        }
        catch (Exception ex)
        {
            state.FailedPages.Remove(page);
            state.FailedPages.Add(page);
            state.LastError = $"Page {page}: extraction failed — {ex.Message}";
            logger.LogError(ex, "Page: {Page}/{TotalPages} extraction failed", page, state.TotalPages);
            return state.Config.StopOnPersistentFailure ? PageOutcome.StopCrawl : PageOutcome.Continue;
        }

        foreach (var record in records)
        {
            record.PageNumber = page;
        }

        await VisitProfilesAsync(session, state, records, url, page, cancellationToken);

        // Surface the discovered profile link as an ordinary field, same as the single-scrape
        // engine, so it shows up in the saved records/CSV export like any other requested field.
        const string profileUrlFieldName = "Profile URL";
        if (state.Config.EnableProfileCrawl)
        {
            foreach (var record in records.Where(r => r.ProfileUrl is not null))
            {
                record.Fields[profileUrlFieldName] = record.ProfileUrl;
            }
        }

        var saved = await stateStore.AppendRecordsAsync(state.JobId, records, cancellationToken);
        state.RecordsSaved += saved;
        state.FailedPages.Remove(page);
        if (state.FieldNames.Count == 0)
        {
            state.FieldNames = state.Config.Fields.Select(f => f.Name).ToList();
        }

        if (state.Config.EnableProfileCrawl && records.Any(r => r.ProfileUrl is not null) && !state.FieldNames.Contains(profileUrlFieldName))
        {
            state.FieldNames.Add(profileUrlFieldName);
        }

        logger.LogInformation(
            "Page: {Page}/{TotalPages} Status: {StatusCode} Records extracted: {Extracted} Records saved: {Saved} Attempts: {Attempts} Elapsed: {Elapsed}ms",
            page, state.TotalPages, result.StatusCode, records.Count, saved, attempts, (int)elapsed.TotalMilliseconds);

        return PageOutcome.Continue;
    }

    /// <summary>
    /// Visits each extracted record's own profile/detail page — through the same persistent
    /// session, and the same delay/retry policy as listing pages — to fill in fields the
    /// listing page alone doesn't show. A single record's profile failing never aborts the
    /// crawl (it just keeps whatever the listing page already found), matching the single-scrape
    /// engine's behaviour; the per-job <see cref="CrawlJobStateDto.MaxProfiles"/> cap (via
    /// <see cref="PagedCrawlConfigDto.MaxProfiles"/>) is enforced across the whole crawl, not
    /// per page.
    /// </summary>
    private async Task VisitProfilesAsync(
        ICrawlHttpSession session, CrawlJobStateDto state, List<ScrapedRecordDto> records, string listingPageUrl, int page, CancellationToken cancellationToken)
    {
        if (!state.Config.EnableProfileCrawl)
        {
            return;
        }

        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (state.ProfilesVisited >= state.Config.MaxProfiles)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(record.ProfileUrl))
            {
                continue;
            }

            await RandomDelayAsync(state.Config, cancellationToken);

            var (profileResult, _) = await FetchWithRetryAsync(session, record.ProfileUrl, listingPageUrl, state.Config, page, cancellationToken);
            if (!profileResult.IsSuccess)
            {
                logger.LogWarning(
                    "Page: {Page} Profile: {ProfileUrl} Status: {Outcome} Reason: {Detail} Action: keeping listing data, continuing",
                    page, record.ProfileUrl, profileResult.Outcome, profileResult.Detail);
                continue;
            }

            var profileDoc = new HtmlDocument();
            profileDoc.LoadHtml(profileResult.Html!);
            await profileFieldMerger.MergeProfileFieldsAsync(record, profileDoc.DocumentNode, state.Config.Fields, cancellationToken);
            state.ProfilesVisited++;

            logger.LogInformation(
                "Page: {Page} Profile: {ProfileUrl} Status: {StatusCode} Profiles visited: {Visited}/{Max}",
                page, record.ProfileUrl, profileResult.StatusCode, state.ProfilesVisited, state.Config.MaxProfiles);
        }
    }

    private async Task<(Interfaces.PageFetchResult Result, int Attempts)> FetchWithRetryAsync(
        ICrawlHttpSession session, string url, string? referer, PagedCrawlConfigDto config, int page, CancellationToken cancellationToken)
    {
        var maxAttempts = Math.Clamp(config.MaxRetriesPerPage, 1, ScrapingLimits.MaxCrawlRetriesPerPage);

        for (var attempt = 1; ; attempt++)
        {
            var result = await session.FetchAsync(url, referer, cancellationToken);
            if (result.IsSuccess)
            {
                return (result, attempt);
            }

            // Never retried: an explicit block or a URL our own safety checks rejected — more
            // attempts would just repeat the exact same outcome.
            if (result.Outcome is Interfaces.PageFetchOutcome.Forbidden or Interfaces.PageFetchOutcome.UrlNotAllowed)
            {
                return (result, attempt);
            }

            if (attempt >= maxAttempts)
            {
                return (result, attempt);
            }

            var delay = result.RetryAfter ?? ComputeBackoff(attempt, config);
            logger.LogWarning(
                "Page: {Page} Status: {Outcome} Retry attempt: {Attempt}/{MaxAttempts} Backoff: {Backoff}s",
                page, result.Outcome, attempt, maxAttempts, delay.TotalSeconds.ToString("F0"));

            await Task.Delay(delay, cancellationToken);
        }
    }

    private static TimeSpan ComputeBackoff(int attempt, PagedCrawlConfigDto config)
    {
        var baseSeconds = Math.Max(0, config.RetryBackoffBaseSeconds);
        var maxSeconds = Math.Max(baseSeconds, config.RetryBackoffMaxSeconds);
        var exponential = Math.Min(baseSeconds * Math.Pow(2, attempt - 1), maxSeconds);
        var jitter = Random.Shared.NextDouble() * exponential * 0.2;
        return TimeSpan.FromSeconds(exponential + jitter);
    }

    /// <summary>
    /// A random human-scale pause before every page, plus an occasional longer one — never a
    /// fixed interval. Skipped for the very first page of a run so starting a job doesn't wait
    /// needlessly.
    /// </summary>
    private async Task DelayBeforePageAsync(PagedCrawlConfigDto config, int page, CancellationToken cancellationToken)
    {
        if (page <= 1)
        {
            return;
        }

        var delaySeconds = RandomDelaySeconds(config);

        if (config.LongPauseEveryPages > 0 && page % config.LongPauseEveryPages == 0)
        {
            var longPause = Math.Max(0, config.LongPauseSeconds);
            delaySeconds += longPause;
            logger.LogInformation("Page: {Page} taking a longer pause ({LongPause}s) after every {Every} pages", page, longPause.ToString("F0"), config.LongPauseEveryPages);
        }

        logger.LogInformation("Page: {Page} Next request delay: {Delay}s", page, delaySeconds.ToString("F1"));
        await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
    }

    /// <summary>The same randomized human-scale pause as between listing pages, without the "every N pages" long-pause escalation (that's keyed to listing page position, not profile count).</summary>
    private static async Task RandomDelayAsync(PagedCrawlConfigDto config, CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(RandomDelaySeconds(config)), cancellationToken);
    }

    private static double RandomDelaySeconds(PagedCrawlConfigDto config)
    {
        var min = Math.Max(0, config.MinDelaySeconds);
        var max = Math.Max(min, Math.Min(config.MaxDelaySeconds, ScrapingLimits.MaxCrawlDelaySeconds));
        return min + Random.Shared.NextDouble() * (max - min);
    }

    private async Task<CrawlJobStateDto> RequireStateAsync(string jobId, CancellationToken cancellationToken)
    {
        var state = await stateStore.LoadAsync(jobId, cancellationToken);
        return state ?? throw new InvalidOperationException($"No crawl job found with id '{jobId}'.");
    }
}
