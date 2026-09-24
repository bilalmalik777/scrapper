namespace Scrapper.Models.Common;

public class ValidationException(string message) : Exception(message);

public class ScrapeException(string message) : Exception(message);

/// <summary>
/// The server rejected the request with HTTP 429 (Too Many Requests) even after the fetcher's
/// own short in-request retry was exhausted — a distinct case from a generic
/// <see cref="ScrapeException"/> because a caller doing a multi-page/multi-profile crawl may
/// want to pause for longer and retry the same fetch again, rather than treating it as a
/// permanent failure.
/// </summary>
public class RateLimitedException(string message) : ScrapeException(message);

public class SsrfViolationException(string message) : Exception(message);
