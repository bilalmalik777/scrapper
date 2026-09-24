using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Scrapper.Models.DTOs;
using Scrapper.Services.Interfaces;

namespace Scrapper.Services.Implementations;

/// <summary>
/// File-based <see cref="ICrawlStateStore"/> — one directory per job, holding a
/// <c>state.json</c> checkpoint and a <c>records.jsonl</c> (one JSON object per line, appended
/// to as pages complete). No database dependency, and trivially inspectable/portable — exactly
/// what a small, resumable crawl needs; nothing here precludes swapping in a database-backed
/// implementation later behind the same <see cref="ICrawlStateStore"/> interface.
/// </summary>
public class JsonFileCrawlStateStore : ICrawlStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _rootDirectory;

    public JsonFileCrawlStateStore(string? rootDirectory = null)
    {
        _rootDirectory = rootDirectory ?? Path.Combine(AppContext.BaseDirectory, "crawl-data");
    }

    public async Task<CrawlJobStateDto?> LoadAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var path = StatePath(jobId);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<CrawlJobStateDto>(stream, JsonOptions, cancellationToken);
    }

    public async Task SaveAsync(CrawlJobStateDto state, CancellationToken cancellationToken = default)
    {
        state.UpdatedAt = DateTimeOffset.UtcNow;
        var dir = JobDirectory(state.JobId);
        Directory.CreateDirectory(dir);

        var finalPath = StatePath(state.JobId);
        var tempPath = finalPath + ".tmp";

        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken);
        }

        File.Move(tempPath, finalPath, overwrite: true);
    }

    public Task ResetAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var dir = JobDirectory(jobId);
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }

        return Task.CompletedTask;
    }

    public async Task<int> AppendRecordsAsync(string jobId, IReadOnlyList<ScrapedRecordDto> records, CancellationToken cancellationToken = default)
    {
        if (records.Count == 0)
        {
            return 0;
        }

        var dir = JobDirectory(jobId);
        Directory.CreateDirectory(dir);

        var existingKeys = await LoadDedupeKeysAsync(jobId, cancellationToken);
        var newLines = new List<string>();

        foreach (var record in records)
        {
            var key = DedupeKey(record);
            if (!existingKeys.Add(key))
            {
                continue; // Already saved (e.g. this page was re-processed after a resume).
            }

            newLines.Add(JsonSerializer.Serialize(record));
        }

        if (newLines.Count > 0)
        {
            await File.AppendAllLinesAsync(RecordsPath(jobId), newLines, cancellationToken);
        }

        return newLines.Count;
    }

    public async Task<List<ScrapedRecordDto>> LoadRecordsAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var path = RecordsPath(jobId);
        if (!File.Exists(path))
        {
            return [];
        }

        var records = new List<ScrapedRecordDto>();
        foreach (var line in await File.ReadAllLinesAsync(path, cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var record = JsonSerializer.Deserialize<ScrapedRecordDto>(line);
            if (record is not null)
            {
                records.Add(record);
            }
        }

        return records;
    }

    private async Task<HashSet<string>> LoadDedupeKeysAsync(string jobId, CancellationToken cancellationToken)
    {
        var records = await LoadRecordsAsync(jobId, cancellationToken);
        return records.Select(DedupeKey).ToHashSet();
    }

    /// <summary>
    /// A record's own detail-page URL is the ideal unique identifier — stable across re-runs
    /// and unambiguous. When there isn't one (no profile crawl, or the site has no per-record
    /// page), fall back to a hash of every field's value: two records are "the same" only if
    /// every extracted field matches exactly.
    /// </summary>
    private static string DedupeKey(ScrapedRecordDto record)
    {
        if (!string.IsNullOrWhiteSpace(record.ProfileUrl))
        {
            return "url:" + record.ProfileUrl.Trim().ToLowerInvariant();
        }

        var joined = string.Join('|', record.Fields.OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key}={kv.Value}"));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(joined));
        return "fields:" + Convert.ToHexString(hash);
    }

    private string JobDirectory(string jobId) => Path.Combine(_rootDirectory, SanitizeJobId(jobId));
    private string StatePath(string jobId) => Path.Combine(JobDirectory(jobId), "state.json");
    private string RecordsPath(string jobId) => Path.Combine(JobDirectory(jobId), "records.jsonl");

    private static string SanitizeJobId(string jobId)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(jobId.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "job" : sanitized;
    }
}
