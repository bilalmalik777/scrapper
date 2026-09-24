namespace Scrapper.Services.Interfaces;

/// <summary>
/// Runs (or resumes) a checkpointed, multi-page crawl job to completion or until it's stopped
/// (cancellation, an explicit block, or persistent failure) — see <c>PagedCrawlConfigDto</c>
/// and <c>CrawlJobStateDto</c>. Every method loads its state from <see cref="ICrawlStateStore"/>
/// at the start and persists it after every page, so it's safe to call again after a crash,
/// a restart, or a cancelled run; it always picks up from the saved checkpoint.
/// </summary>
public interface IPagedCrawlService
{
    /// <summary>Runs forward from the job's last completed page through its configured total, one page at a time.</summary>
    Task RunAsync(string jobId, CancellationToken cancellationToken);

    /// <summary>Re-attempts only the pages currently recorded as failed, leaving the forward checkpoint untouched.</summary>
    Task RetryFailedAsync(string jobId, CancellationToken cancellationToken);
}
