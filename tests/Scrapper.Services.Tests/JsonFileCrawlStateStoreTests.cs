using FluentAssertions;
using Scrapper.Models.DTOs;
using Scrapper.Models.Enums;
using Scrapper.Services.Implementations;

namespace Scrapper.Services.Tests;

public class JsonFileCrawlStateStoreTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "scrapper-crawl-tests-" + Guid.NewGuid().ToString("n"));
    private readonly JsonFileCrawlStateStore _store;

    public JsonFileCrawlStateStoreTests()
    {
        _store = new JsonFileCrawlStateStore(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_ReturnsNull_WhenJobDoesNotExist()
    {
        var state = await _store.LoadAsync("missing-job");

        state.Should().BeNull();
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsState()
    {
        var state = new CrawlJobStateDto { JobId = "job-1", TotalPages = 68, LastCompletedPage = 19, Status = CrawlJobStatus.Paused };

        await _store.SaveAsync(state);
        var loaded = await _store.LoadAsync("job-1");

        loaded.Should().NotBeNull();
        loaded!.TotalPages.Should().Be(68);
        loaded.LastCompletedPage.Should().Be(19);
        loaded.Status.Should().Be(CrawlJobStatus.Paused);
    }

    [Fact]
    public async Task AppendRecordsAsync_PersistsRecordsAcrossPages()
    {
        var record1 = new ScrapedRecordDto { Fields = { ["Name"] = "Alan Buckley" }, ProfileUrl = "https://example.com/alan" };
        var record2 = new ScrapedRecordDto { Fields = { ["Name"] = "Alan Jones" }, ProfileUrl = "https://example.com/jones" };

        await _store.AppendRecordsAsync("job-2", [record1]);
        await _store.AppendRecordsAsync("job-2", [record2]);

        var all = await _store.LoadRecordsAsync("job-2");

        all.Should().HaveCount(2);
        all.Select(r => r.Fields["Name"]).Should().BeEquivalentTo(["Alan Buckley", "Alan Jones"]);
    }

    [Fact]
    public async Task AppendRecordsAsync_SkipsDuplicateProfileUrl()
    {
        var record = new ScrapedRecordDto { Fields = { ["Name"] = "Alan Buckley" }, ProfileUrl = "https://example.com/alan" };

        var firstSaved = await _store.AppendRecordsAsync("job-3", [record]);
        var secondSaved = await _store.AppendRecordsAsync("job-3", [record]); // Simulates re-processing a page after a resume.

        firstSaved.Should().Be(1);
        secondSaved.Should().Be(0);
        (await _store.LoadRecordsAsync("job-3")).Should().HaveCount(1);
    }

    [Fact]
    public async Task AppendRecordsAsync_DedupesByFieldValues_WhenNoProfileUrl()
    {
        var record = new ScrapedRecordDto { Fields = { ["Name"] = "Alan Buckley", ["Location"] = "Oxford" } };

        var firstSaved = await _store.AppendRecordsAsync("job-4", [record]);
        var secondSaved = await _store.AppendRecordsAsync("job-4", [record]);

        firstSaved.Should().Be(1);
        secondSaved.Should().Be(0);
    }

    [Fact]
    public async Task ResetAsync_RemovesStateAndRecords()
    {
        await _store.SaveAsync(new CrawlJobStateDto { JobId = "job-5" });
        await _store.AppendRecordsAsync("job-5", [new ScrapedRecordDto { Fields = { ["Name"] = "X" } }]);

        await _store.ResetAsync("job-5");

        (await _store.LoadAsync("job-5")).Should().BeNull();
        (await _store.LoadRecordsAsync("job-5")).Should().BeEmpty();
    }
}
