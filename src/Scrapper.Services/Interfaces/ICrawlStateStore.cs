using Scrapper.Models.DTOs;

namespace Scrapper.Services.Interfaces;

/// <summary>
/// Persists a paged crawl job's checkpoint (which page it's up to, failed pages, status) and
/// its accumulated records to disk, so a crawl can resume after a crash, a restart, or a
/// deliberate stop — nothing lives only in memory. Every write is atomic (temp file + rename)
/// so a process crash mid-write never leaves a corrupt/partial checkpoint behind.
/// </summary>
public interface ICrawlStateStore
{
    Task<CrawlJobStateDto?> LoadAsync(string jobId, CancellationToken cancellationToken = default);

    Task SaveAsync(CrawlJobStateDto state, CancellationToken cancellationToken = default);

    /// <summary>Deletes any existing state/records for this job id, for a fresh restart.</summary>
    Task ResetAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends newly-extracted records for one page, skipping any whose dedupe key (profile
    /// URL when available, else a hash of its field values) was already saved for this job —
    /// makes re-processing a page after a resume safe even if its checkpoint update didn't
    /// complete last time. Returns how many records were actually new.
    /// </summary>
    Task<int> AppendRecordsAsync(string jobId, IReadOnlyList<ScrapedRecordDto> records, CancellationToken cancellationToken = default);

    Task<List<ScrapedRecordDto>> LoadRecordsAsync(string jobId, CancellationToken cancellationToken = default);
}
