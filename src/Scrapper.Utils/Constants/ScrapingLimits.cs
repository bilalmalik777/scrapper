namespace Scrapper.Utils.Constants;

public static class ScrapingLimits
{
    public const int MaxResponseBytes = 10 * 1024 * 1024; // 10 MB
    public const int MaxTimeoutSeconds = 30;
    public const int MinTimeoutSeconds = 1;
    public const int MaxRecordsHardCap = 1000;
    public const int MaxFields = 50;
    public const int MaxRedirects = 5;
    public const int TestSelectorPreviewCount = 5;
    public const int TestSelectorMaxMatches = 500;

    // Automatic record (repeated-card) detection
    public const int MinRepeatedSiblingsForRecord = 3;
    public const int MaxRecordDetectionCandidateDepth = 6;

    // Confidence thresholds (0-100)
    public const int ConfidenceHighThreshold = 80;
    public const int ConfidenceMediumThreshold = 50;
    public const int ConfidenceAcceptThreshold = 40;

    // Deep profile crawling
    public const int MaxProfilesHardCap = 1000;
    public const int MaxPagesHardCap = 100;

    // Politeness / rate-limit handling — a deep crawl can easily fire hundreds of requests
    // at one site in a short window, which is exactly what triggers a site's own bot
    // protection (HTTP 429). Spacing requests out proactively, and backing off and retrying
    // when a 429 does happen, gets far more of a large crawl through than firing everything
    // as fast as possible (which only makes the block happen sooner, not the crawl finish
    // faster — a burst of parallel requests would trigger the same protection even harder).
    public const int PoliteCrawlDelayMs = 200;
    public const int MaxTooManyRequestsRetries = 2;
    public const int TooManyRequestsBaseDelayMs = 1000;
    public const int TooManyRequestsMaxDelayMs = 8000;

    // Losing the next-listing-page fetch ends the entire rest of a multi-page crawl, so a
    // rate-limit block there gets a longer, separate cooldown — well past what a single
    // fetch's own timeout budget could afford — rather than giving up after one failure.
    public const int MaxPageRateLimitCooldownRetries = 3;
    public const int PageRateLimitCooldownSeconds = 8;

    // Paged crawl jobs (checkpointed, resumable, many-minute crawls across dozens of pages)
    public const int MaxCrawlTotalPages = 500;
    public const int MaxCrawlRetriesPerPage = 10;
    public const double MaxCrawlDelaySeconds = 300;

    // Substrings (checked case-insensitively) that indicate the response is a bot-detection
    // challenge/interstitial page rather than real content — never bypassed, only detected so
    // the crawl can back off and, if it persists, stop cleanly.
    public static readonly string[] ChallengePageMarkers =
    [
        "just a moment", "verify you are human", "cf-challenge", "captcha", "access denied",
        "attention required", "checking your browser",
    ];
}
