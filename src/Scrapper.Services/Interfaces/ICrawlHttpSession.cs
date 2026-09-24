namespace Scrapper.Services.Interfaces;

public enum PageFetchOutcome
{
    Success,

    /// <summary>Server explicitly denied access (HTTP 403) — treated distinctly from other failures; never bypassed, only detected.</summary>
    Forbidden,

    /// <summary>HTTP 429 — the response's Retry-After header (if any) is on the result.</summary>
    RateLimited,

    /// <summary>HTTP 500/502/503 — usually transient server-side trouble, worth a retry.</summary>
    ServerError,

    /// <summary>The body matched a known bot-detection challenge/interstitial page (see ScrapingLimits.ChallengePageMarkers).</summary>
    ChallengePage,

    /// <summary>A 2xx response with an empty or whitespace-only body.</summary>
    EmptyResponse,

    /// <summary>Timed out, DNS/connection failure, or another network-level problem.</summary>
    NetworkError,

    /// <summary>URL failed the same SSRF/scheme checks the rest of the app applies before fetching anything.</summary>
    UrlNotAllowed,
}

public record PageFetchResult(PageFetchOutcome Outcome, int? StatusCode, string? Html, string? Detail, TimeSpan? RetryAfter)
{
    public bool IsSuccess => Outcome == PageFetchOutcome.Success;
}

/// <summary>
/// One HTTP session for an entire paged crawl job: a single cookie jar and a single,
/// configured User-Agent shared across every page request, kept alive for the whole crawl and
/// disposed when it ends — never a fresh client per page. Classifies each response (success,
/// explicit block, transient server error, challenge page, ...) but never attempts to defeat
/// any of them; that classification is what lets the caller apply its own retry/stop policy.
/// </summary>
public interface ICrawlHttpSession : IDisposable
{
    Task<PageFetchResult> FetchAsync(string url, string? referer, CancellationToken cancellationToken = default);
}

public interface ICrawlHttpSessionFactory
{
    ICrawlHttpSession Create(string? userAgent, int timeoutSeconds);
}
