using Scrapper.Models.DTOs;

namespace Scrapper.Services.Interfaces;

/// <summary>
/// Starts/resumes paged crawl jobs on background tasks (a crawl can run for many minutes —
/// far too long for one HTTP request) and tracks which ones are currently running so the app
/// can cancel them cleanly on shutdown. State itself lives in <see cref="ICrawlStateStore"/>,
/// not here — this only owns the in-memory "is job X currently executing" bookkeeping.
/// </summary>
public interface ICrawlJobRunner
{
    /// <summary>Creates (or resets, if <paramref name="fresh"/>) a job's checkpoint and starts it running in the background. Returns the job id.</summary>
    Task<string> StartAsync(string? jobId, PagedCrawlConfigDto config, bool fresh, CancellationToken cancellationToken);

    /// <summary>
    /// Resumes an existing, not-currently-running job from its last checkpoint — or, when
    /// <paramref name="fromPage"/> is given, from that page instead (re-crawling it and every
    /// later page; records already saved are updated in place, missing ones are added).
    /// </summary>
    Task ResumeAsync(string jobId, int? fromPage, int? maxProfiles, CancellationToken cancellationToken);

    /// <summary>Re-attempts only this job's currently-failed pages.</summary>
    Task RetryFailedAsync(string jobId, CancellationToken cancellationToken);

    Task<CrawlJobStateDto?> GetStateAsync(string jobId, CancellationToken cancellationToken);

    /// <summary>Signals every currently-running job to stop and save its checkpoint — called on app shutdown.</summary>
    void StopAll();
}
