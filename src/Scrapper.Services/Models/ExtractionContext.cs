using HtmlAgilityPack;

namespace Scrapper.Services.Models;

/// <summary>
/// Everything a field extraction strategy might need: the specific record node to search
/// within, the full document (for strategies that need page-level context), and the
/// page's parsed structured data (JSON-LD/microdata), matched positionally to records.
/// </summary>
public class ExtractionContext
{
    public required HtmlNode RecordNode { get; init; }
    public required HtmlNode DocumentNode { get; init; }
    public required int RecordIndex { get; init; }
    public required int TotalRecords { get; init; }
    public required IReadOnlyList<Dictionary<string, object?>> StructuredDataObjects { get; init; }

    // Purely navigational/SEO JSON-LD blocks that pages commonly emit alongside their main
    // entity (breadcrumbs, site search box, ...) — never the thing a user is asking to scrape.
    // Internal (not private): also used by StructuredDataExtractionStrategy's cross-entity
    // fallback, which must apply the exact same exclusion when scanning every published block.
    internal static readonly HashSet<string> NonContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "BreadcrumbList", "WebSite", "SiteNavigationElement", "SearchAction",
        // A generic page-wrapper entity — its "name" is the page's own <title> (e.g. "Find a
        // Therapist | UKAHPP"), never a specific listing record's real name.
        "WebPage", "CollectionPage", "SearchResultsPage",
    };

    /// <summary>The structured-data object positionally aligned with this record, if the counts line up.</summary>
    public Dictionary<string, object?>? MatchingStructuredDataObject
    {
        get
        {
            if (StructuredDataObjects.Count == 0)
            {
                return null;
            }

            // A single-record page (a profile) — including the degenerate case of exactly one
            // JSON-LD block, which would otherwise trivially satisfy the positional match below
            // (1 block == 1 record) and return it unchecked. A page can genuinely have nothing
            // but a site-wide "WebSite"/breadcrumb block and no real Person/Product entity at
            // all; using that anyway would substitute the site's own name/URL for the record's
            // real data, so it's excluded here rather than only when there's more than one block
            // to choose from.
            if (TotalRecords == 1)
            {
                return StructuredDataObjects
                    .Where(o => !NonContentTypes.Contains(o.GetValueOrDefault("@type")?.ToString() ?? string.Empty))
                    .OrderByDescending(o => o.Count)
                    .FirstOrDefault();
            }

            // A multi-record listing page can coincidentally publish the same number of
            // site-wide JSON-LD blocks (an Organization, a WebSite, a BreadcrumbList, ...) as
            // there happen to be records on the page — pure coincidence, not real per-record
            // alignment. Trusting position blindly there would hand e.g. record 0 the site's
            // own "WebSite" name instead of its real one. The non-content-type filter guards
            // against that; a positional slot that IS a non-content type is treated as no
            // match at all; a slot with a data-only shape (no "@type") is still trusted.
            if (StructuredDataObjects.Count == TotalRecords && RecordIndex < StructuredDataObjects.Count)
            {
                var positional = StructuredDataObjects[RecordIndex];
                var positionalType = positional.GetValueOrDefault("@type")?.ToString() ?? string.Empty;
                if (!NonContentTypes.Contains(positionalType))
                {
                    return positional;
                }
            }

            if (StructuredDataObjects.Count == 1)
            {
                var only = StructuredDataObjects[0];
                return NonContentTypes.Contains(only.GetValueOrDefault("@type")?.ToString() ?? string.Empty) ? null : only;
            }

            return null;
        }
    }
}
