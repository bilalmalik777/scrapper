using Scrapper.Models.Enums;

namespace Scrapper.Models.DTOs;

/// <summary>
/// Configuration for a long-running, checkpointed multi-page crawl (dozens of pages, run over
/// several minutes) — distinct from <see cref="ScraperConfigDto"/>'s single synchronous scrape,
/// which returns everything in one HTTP response and has no notion of resuming after a crash or
/// a mid-crawl block. Page URLs are built by substituting a "page" query-string parameter on
/// <see cref="Url"/> (e.g. "...&amp;page=1" becomes "...&amp;page=2"), which is how this
/// site's — and most listing sites' — pagination works, rather than following "next page" links
/// one at a time (which would make jumping straight to an arbitrary resume page impossible).
/// </summary>
public class PagedCrawlConfigDto
{
    public string Url { get; set; } = string.Empty;

    public string? RecordSelector { get; set; }
    public SelectorType RecordSelectorType { get; set; } = SelectorType.Css;

    public List<FieldDefinitionDto> Fields { get; set; } = [];

    /// <summary>Total number of listing pages to crawl (the caller is expected to know this, e.g. from the site's own pagination).</summary>
    public int TotalPages { get; set; } = 1;

    /// <summary>
    /// Visits each record's own detail/profile page (through the same persistent session, with
    /// the same delay/retry policy as listing pages) to fill in fields the listing page doesn't
    /// show. Defaults to on, matching the single-scrape engine's default — most directory
    /// listings need this for anything beyond name/location.
    /// </summary>
    public bool EnableProfileCrawl { get; set; } = true;

    /// <summary>Maximum number of individual profile pages to visit across the whole job (not per page).</summary>
    public int MaxProfiles { get; set; } = 200;

    public int TimeoutSeconds { get; set; } = 20;

    /// <summary>
    /// Sent as-is for every request in this crawl session — never rotated mid-crawl. Falls
    /// back to a normal, current desktop-browser string when left blank.
    /// </summary>
    public string? UserAgent { get; set; }

    /// <summary>
    /// Fetches every page (listing and profile) through a real headless browser instead of a
    /// plain HTTP client — for sites whose bot protection blocks a plain client outright
    /// (a TLS/connection-level fingerprint check, not just missing headers). Off by default,
    /// matching the single-scrape engine's own opt-in JS rendering: slower/heavier, and
    /// unnecessary for the large majority of sites.
    /// </summary>
    public bool EnableJavaScriptRendering { get; set; }

    /// <summary>Randomized delay range between page requests, in seconds.</summary>
    public double MinDelaySeconds { get; set; } = 5;
    public double MaxDelaySeconds { get; set; } = 15;

    /// <summary>Every N pages, take one extra, longer pause on top of the normal per-page delay (0 disables this).</summary>
    public int LongPauseEveryPages { get; set; } = 10;
    public double LongPauseSeconds { get; set; } = 45;

    /// <summary>Retries for a single page before it's recorded as failed (exponential backoff with jitter).</summary>
    public int MaxRetriesPerPage { get; set; } = 3;

    /// <summary>Exponential backoff bounds (seconds) between retry attempts for one page — e.g. 30s, then 60s, then capped at 120s.</summary>
    public double RetryBackoffBaseSeconds { get; set; } = 30;
    public double RetryBackoffMaxSeconds { get; set; } = 120;

    /// <summary>
    /// When a page still fails after its retries are exhausted: true stops the whole crawl
    /// cleanly (saving everything gathered so far); false records the page as failed and moves
    /// on to the next one. An explicit HTTP 403 always stops the crawl regardless of this
    /// setting — it's the site explicitly denying access, not a transient hiccup.
    /// </summary>
    public bool StopOnPersistentFailure { get; set; } = true;
}

public class CrawlJobStateDto
{
    public string JobId { get; set; } = string.Empty;
    public PagedCrawlConfigDto Config { get; set; } = new();
    public CrawlJobStatus Status { get; set; } = CrawlJobStatus.NotStarted;
    public int TotalPages { get; set; }
    public int LastCompletedPage { get; set; }
    public int? CurrentPage { get; set; }
    public List<int> FailedPages { get; set; } = [];
    public int RecordsSaved { get; set; }
    public int ProfilesVisited { get; set; }
    public string? LastError { get; set; }
    public List<string> FieldNames { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class StartCrawlJobRequestDto
{
    public string? JobId { get; set; }
    public PagedCrawlConfigDto Config { get; set; } = new();

    /// <summary>Discards any existing state/records for this job id and starts over from page 1.</summary>
    public bool Fresh { get; set; }
}

public class CrawlJobRecordsResponseDto
{
    public List<string> FieldNames { get; set; } = [];
    public List<ScrapedRecordDto> Records { get; set; } = [];
}
