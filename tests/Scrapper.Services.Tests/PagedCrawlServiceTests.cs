using FluentAssertions;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Scrapper.Models.DTOs;
using Scrapper.Models.Enums;
using Scrapper.Services.Implementations;
using Scrapper.Services.Interfaces;

namespace Scrapper.Services.Tests;

public class PagedCrawlServiceTests
{
    private readonly Mock<ICrawlStateStore> _stateStore = new();
    private readonly Mock<ICrawlHttpSession> _session = new();
    private readonly Mock<ICrawlHttpSessionFactory> _sessionFactory = new();
    private readonly Mock<IListingPageExtractor> _pageExtractor = new();
    private readonly Mock<IProfileFieldMerger> _profileFieldMerger = new();
    private readonly List<CrawlJobStateDto> _savedStates = [];

    public PagedCrawlServiceTests()
    {
        _sessionFactory.Setup(f => f.Create(It.IsAny<string?>(), It.IsAny<int>())).Returns(_session.Object);
        _stateStore.Setup(s => s.SaveAsync(It.IsAny<CrawlJobStateDto>(), It.IsAny<CancellationToken>()))
            .Callback<CrawlJobStateDto, CancellationToken>((s, _) => _savedStates.Add(Clone(s)))
            .Returns(Task.CompletedTask);
        _pageExtractor
            .Setup(e => e.ExtractAsync(It.IsAny<HtmlNode>(), It.IsAny<Uri>(), It.IsAny<string?>(), It.IsAny<SelectorType>(), It.IsAny<List<FieldDefinitionDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ScrapedRecordDto { Fields = { ["Name"] = "Someone" } }]);
    }

    private PagedCrawlService CreateSut() =>
        new(_stateStore.Object, _sessionFactory.Object, _pageExtractor.Object, _profileFieldMerger.Object, NullLogger<PagedCrawlService>.Instance);

    private static CrawlJobStateDto Clone(CrawlJobStateDto s) => new()
    {
        JobId = s.JobId,
        Config = s.Config,
        Status = s.Status,
        TotalPages = s.TotalPages,
        LastCompletedPage = s.LastCompletedPage,
        CurrentPage = s.CurrentPage,
        FailedPages = [.. s.FailedPages],
        RecordsSaved = s.RecordsSaved,
        ProfilesVisited = s.ProfilesVisited,
        LastError = s.LastError,
    };

    private static CrawlJobStateDto NewState(int totalPages = 3, int lastCompletedPage = 0) => new()
    {
        JobId = "job-1",
        TotalPages = totalPages,
        LastCompletedPage = lastCompletedPage,
        Config = new PagedCrawlConfigDto
        {
            Url = "http://8.8.8.8/search?page=1",
            TotalPages = totalPages,
            Fields = [new FieldDefinitionDto { Name = "Name", Type = FieldKind.Name }],
            MinDelaySeconds = 0,
            MaxDelaySeconds = 0,
            MaxRetriesPerPage = 2,
            RetryBackoffBaseSeconds = 0,
            RetryBackoffMaxSeconds = 0,
        },
    };

    [Fact]
    public async Task RunAsync_ProcessesAllPagesAndCheckpointsIncrementally()
    {
        var state = NewState(totalPages: 3);
        _stateStore.Setup(s => s.LoadAsync("job-1", It.IsAny<CancellationToken>())).ReturnsAsync(state);
        _session.Setup(s => s.FetchAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PageFetchResult(PageFetchOutcome.Success, 200, "<html></html>", null, null));
        _stateStore.Setup(s => s.AppendRecordsAsync("job-1", It.IsAny<IReadOnlyList<ScrapedRecordDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        await CreateSut().RunAsync("job-1", CancellationToken.None);

        // A checkpoint was saved after page 1, then after page 2, then after page 3 (in order).
        _savedStates.Select(s => s.LastCompletedPage).Should().ContainInOrder(1, 2, 3);
        _savedStates.Last().Status.Should().Be(CrawlJobStatus.Completed);
        _savedStates.Last().RecordsSaved.Should().Be(3);
    }

    [Fact]
    public async Task RunAsync_ResumesFromLastCompletedPage_NotFromPageOne()
    {
        var state = NewState(totalPages: 3, lastCompletedPage: 1);
        _stateStore.Setup(s => s.LoadAsync("job-1", It.IsAny<CancellationToken>())).ReturnsAsync(state);
        var fetchedUrls = new List<string>();
        _session.Setup(s => s.FetchAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string?, CancellationToken>((url, _, _) => fetchedUrls.Add(url))
            .ReturnsAsync(new PageFetchResult(PageFetchOutcome.Success, 200, "<html></html>", null, null));
        _stateStore.Setup(s => s.AppendRecordsAsync("job-1", It.IsAny<IReadOnlyList<ScrapedRecordDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        await CreateSut().RunAsync("job-1", CancellationToken.None);

        fetchedUrls.Should().HaveCount(2);
        fetchedUrls[0].Should().Contain("page=2");
        fetchedUrls[1].Should().Contain("page=3");
    }

    [Fact]
    public async Task RunAsync_Http403_StopsCleanlyAndMarksBlocked()
    {
        var state = NewState(totalPages: 5);
        _stateStore.Setup(s => s.LoadAsync("job-1", It.IsAny<CancellationToken>())).ReturnsAsync(state);
        _session.Setup(s => s.FetchAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PageFetchResult(PageFetchOutcome.Forbidden, 403, null, "access denied", null));

        await CreateSut().RunAsync("job-1", CancellationToken.None);

        _session.Verify(s => s.FetchAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        _savedStates.Last().Status.Should().Be(CrawlJobStatus.Blocked);
        _savedStates.Last().FailedPages.Should().Contain(1);
        _savedStates.Last().LastCompletedPage.Should().Be(0); // Page 1 never completed.
    }

    [Fact]
    public async Task RunAsync_PersistentFailure_StopsWhenConfiguredTo()
    {
        var state = NewState(totalPages: 5);
        state.Config.StopOnPersistentFailure = true;
        _stateStore.Setup(s => s.LoadAsync("job-1", It.IsAny<CancellationToken>())).ReturnsAsync(state);
        _session.Setup(s => s.FetchAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PageFetchResult(PageFetchOutcome.ServerError, 500, null, "server error", null));

        await CreateSut().RunAsync("job-1", CancellationToken.None);

        // maxRetriesPerPage = 2, so exactly 2 attempts before giving up.
        _session.Verify(s => s.FetchAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _savedStates.Last().Status.Should().Be(CrawlJobStatus.Blocked);
    }

    [Fact]
    public async Task RunAsync_PersistentFailure_SkipsAndContinuesWhenConfiguredTo()
    {
        var state = NewState(totalPages: 3);
        state.Config.StopOnPersistentFailure = false;
        _stateStore.Setup(s => s.LoadAsync("job-1", It.IsAny<CancellationToken>())).ReturnsAsync(state);
        _session.SetupSequence(s => s.FetchAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PageFetchResult(PageFetchOutcome.ServerError, 500, null, "err", null)) // page 1, attempt 1
            .ReturnsAsync(new PageFetchResult(PageFetchOutcome.ServerError, 500, null, "err", null)) // page 1, attempt 2 (exhausted)
            .ReturnsAsync(new PageFetchResult(PageFetchOutcome.Success, 200, "<html></html>", null, null)) // page 2
            .ReturnsAsync(new PageFetchResult(PageFetchOutcome.Success, 200, "<html></html>", null, null)); // page 3
        _stateStore.Setup(s => s.AppendRecordsAsync("job-1", It.IsAny<IReadOnlyList<ScrapedRecordDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        await CreateSut().RunAsync("job-1", CancellationToken.None);

        var finalState = _savedStates.Last();
        finalState.Status.Should().Be(CrawlJobStatus.Completed);
        finalState.FailedPages.Should().Contain(1);
        finalState.LastCompletedPage.Should().Be(3);
    }

    [Fact]
    public async Task RetryFailedAsync_OnlyReprocessesFailedPages()
    {
        var state = NewState(totalPages: 5, lastCompletedPage: 5);
        state.FailedPages = [2, 4];
        _stateStore.Setup(s => s.LoadAsync("job-1", It.IsAny<CancellationToken>())).ReturnsAsync(state);
        var fetchedUrls = new List<string>();
        _session.Setup(s => s.FetchAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string?, CancellationToken>((url, _, _) => fetchedUrls.Add(url))
            .ReturnsAsync(new PageFetchResult(PageFetchOutcome.Success, 200, "<html></html>", null, null));
        _stateStore.Setup(s => s.AppendRecordsAsync("job-1", It.IsAny<IReadOnlyList<ScrapedRecordDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        await CreateSut().RetryFailedAsync("job-1", CancellationToken.None);

        fetchedUrls.Should().HaveCount(2);
        fetchedUrls[0].Should().Contain("page=2");
        fetchedUrls[1].Should().Contain("page=4");
        _savedStates.Last().FailedPages.Should().BeEmpty();
        _savedStates.Last().Status.Should().Be(CrawlJobStatus.Completed);
    }

    [Fact]
    public async Task RunAsync_RateLimited_RetriesThenSucceeds()
    {
        var state = NewState(totalPages: 1);
        _stateStore.Setup(s => s.LoadAsync("job-1", It.IsAny<CancellationToken>())).ReturnsAsync(state);
        _session.SetupSequence(s => s.FetchAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PageFetchResult(PageFetchOutcome.RateLimited, 429, null, "slow down", TimeSpan.FromMilliseconds(1)))
            .ReturnsAsync(new PageFetchResult(PageFetchOutcome.Success, 200, "<html></html>", null, null));
        _stateStore.Setup(s => s.AppendRecordsAsync("job-1", It.IsAny<IReadOnlyList<ScrapedRecordDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        await CreateSut().RunAsync("job-1", CancellationToken.None);

        _savedStates.Last().Status.Should().Be(CrawlJobStatus.Completed);
        _savedStates.Last().LastCompletedPage.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_Cancelled_SavesPausedStatus()
    {
        var state = NewState(totalPages: 5);
        _stateStore.Setup(s => s.LoadAsync("job-1", It.IsAny<CancellationToken>())).ReturnsAsync(state);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await CreateSut().RunAsync("job-1", cts.Token);

        _savedStates.Last().Status.Should().Be(CrawlJobStatus.Paused);
    }

    [Fact]
    public async Task RunAsync_VisitsEachRecordsProfilePage_WhenEnabled()
    {
        var state = NewState(totalPages: 1);
        state.Config.EnableProfileCrawl = true;
        state.Config.MaxProfiles = 10;
        _stateStore.Setup(s => s.LoadAsync("job-1", It.IsAny<CancellationToken>())).ReturnsAsync(state);

        var record = new ScrapedRecordDto { Fields = { ["Name"] = "Someone" }, ProfileUrl = "http://8.8.8.8/profile/1" };
        _pageExtractor
            .Setup(e => e.ExtractAsync(It.IsAny<HtmlNode>(), It.IsAny<Uri>(), It.IsAny<string?>(), It.IsAny<SelectorType>(), It.IsAny<List<FieldDefinitionDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([record]);

        _session.Setup(s => s.FetchAsync(It.Is<string>(u => u.Contains("page=1")), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PageFetchResult(PageFetchOutcome.Success, 200, "<html></html>", null, null));
        _session.Setup(s => s.FetchAsync("http://8.8.8.8/profile/1", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PageFetchResult(PageFetchOutcome.Success, 200, "<html>profile</html>", null, null));
        _stateStore.Setup(s => s.AppendRecordsAsync("job-1", It.IsAny<IReadOnlyList<ScrapedRecordDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        await CreateSut().RunAsync("job-1", CancellationToken.None);

        _profileFieldMerger.Verify(
            m => m.MergeProfileFieldsAsync(record, It.IsAny<HtmlNode>(), state.Config.Fields, It.IsAny<CancellationToken>()),
            Times.Once);
        _savedStates.Last().ProfilesVisited.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_StopsVisitingProfiles_OnceMaxProfilesReached()
    {
        var state = NewState(totalPages: 1);
        state.Config.EnableProfileCrawl = true;
        state.Config.MaxProfiles = 1;
        _stateStore.Setup(s => s.LoadAsync("job-1", It.IsAny<CancellationToken>())).ReturnsAsync(state);

        var records = new List<ScrapedRecordDto>
        {
            new() { Fields = { ["Name"] = "One" }, ProfileUrl = "http://8.8.8.8/profile/1" },
            new() { Fields = { ["Name"] = "Two" }, ProfileUrl = "http://8.8.8.8/profile/2" },
        };
        _pageExtractor
            .Setup(e => e.ExtractAsync(It.IsAny<HtmlNode>(), It.IsAny<Uri>(), It.IsAny<string?>(), It.IsAny<SelectorType>(), It.IsAny<List<FieldDefinitionDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(records);

        _session.Setup(s => s.FetchAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PageFetchResult(PageFetchOutcome.Success, 200, "<html></html>", null, null));
        _stateStore.Setup(s => s.AppendRecordsAsync("job-1", It.IsAny<IReadOnlyList<ScrapedRecordDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        await CreateSut().RunAsync("job-1", CancellationToken.None);

        _profileFieldMerger.Verify(
            m => m.MergeProfileFieldsAsync(It.IsAny<ScrapedRecordDto>(), It.IsAny<HtmlNode>(), It.IsAny<List<FieldDefinitionDto>>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _savedStates.Last().ProfilesVisited.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_ProfileFetchFailure_DoesNotAbortCrawl()
    {
        var state = NewState(totalPages: 1);
        state.Config.EnableProfileCrawl = true;
        _stateStore.Setup(s => s.LoadAsync("job-1", It.IsAny<CancellationToken>())).ReturnsAsync(state);

        var record = new ScrapedRecordDto { Fields = { ["Name"] = "Someone" }, ProfileUrl = "http://8.8.8.8/profile/1" };
        _pageExtractor
            .Setup(e => e.ExtractAsync(It.IsAny<HtmlNode>(), It.IsAny<Uri>(), It.IsAny<string?>(), It.IsAny<SelectorType>(), It.IsAny<List<FieldDefinitionDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([record]);

        _session.Setup(s => s.FetchAsync(It.Is<string>(u => u.Contains("page=1")), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PageFetchResult(PageFetchOutcome.Success, 200, "<html></html>", null, null));
        _session.Setup(s => s.FetchAsync("http://8.8.8.8/profile/1", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PageFetchResult(PageFetchOutcome.ServerError, 500, null, "err", null));
        _stateStore.Setup(s => s.AppendRecordsAsync("job-1", It.IsAny<IReadOnlyList<ScrapedRecordDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        await CreateSut().RunAsync("job-1", CancellationToken.None);

        _profileFieldMerger.Verify(
            m => m.MergeProfileFieldsAsync(It.IsAny<ScrapedRecordDto>(), It.IsAny<HtmlNode>(), It.IsAny<List<FieldDefinitionDto>>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _savedStates.Last().Status.Should().Be(CrawlJobStatus.Completed); // A profile failure never blocks/stops the crawl.
        _savedStates.Last().ProfilesVisited.Should().Be(0);
    }
}
