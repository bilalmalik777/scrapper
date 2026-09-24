using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Scrapper.Models.Common;
using Scrapper.Models.DTOs;
using Scrapper.Models.Enums;
using Scrapper.Services.Implementations;
using Scrapper.Services.Interfaces;
using Scrapper.Services.Models;
using Xunit;

namespace Scrapper.Services.Tests;

public class ScraperEngineTests
{
    private readonly Mock<IHtmlFetcher> _htmlFetcherMock = new();
    private readonly Mock<IUrlValidationService> _urlValidationServiceMock = new();
    private readonly Mock<IRecordDetector> _recordDetectorMock = new();
    private readonly Mock<IStructuredDataParser> _structuredDataParserMock = new();
    private readonly Mock<IProfileLinkDetector> _profileLinkDetectorMock = new();
    private readonly Mock<IFieldExtractionOrchestrator> _orchestratorMock = new();

    private ScraperEngine CreateSut()
    {
        _structuredDataParserMock
            .Setup(x => x.Parse(It.IsAny<HtmlAgilityPack.HtmlNode>()))
            .Returns([]);

        // Loose mock: DetectProfileUrl/DetectNextPageUrl default to null (no profile crawl,
        // no pagination) unless a test overrides them.
        return new ScraperEngine(
            _htmlFetcherMock.Object,
            _urlValidationServiceMock.Object,
            _recordDetectorMock.Object,
            _structuredDataParserMock.Object,
            _profileLinkDetectorMock.Object,
            _orchestratorMock.Object,
            new ProfileFieldMerger(_structuredDataParserMock.Object, _orchestratorMock.Object),
            NullLogger<ScraperEngine>.Instance);
    }

    private static ScraperConfigDto BuildConfig() => new()
    {
        Url = "https://example.com",
        Fields = [new FieldDefinitionDto { Name = "Name" }]
    };

    private static FieldResultDto Found(string value) => new()
    {
        Value = value,
        ResolvedType = FieldKind.Text,
        Source = ExtractionSource.SemanticHtml,
        Confidence = 85,
        ConfidenceLevel = ConfidenceLevel.High,
        IsMissing = false,
    };

    [Fact]
    public async Task ScrapeAsync_NoFieldsConfigured_ReturnsError()
    {
        var config = new ScraperConfigDto { Url = "https://example.com", Fields = [] };
        var sut = CreateSut();

        var result = await sut.ScrapeAsync(config);

        result.Errors.Should().NotBeEmpty();
        _urlValidationServiceMock.Verify(x => x.ValidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ScrapeAsync_InvalidUrl_ReturnsErrorWithoutFetching()
    {
        _urlValidationServiceMock.Setup(x => x.ValidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidateUrlResponseDto { IsValid = false, Reason = "blocked" });
        var sut = CreateSut();

        var result = await sut.ScrapeAsync(BuildConfig());

        result.Errors.Should().Contain("blocked");
        _htmlFetcherMock.Verify(x => x.FetchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ScrapeAsync_FetchThrowsScrapeException_ReturnsErrorMessage()
    {
        _urlValidationServiceMock.Setup(x => x.ValidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidateUrlResponseDto { IsValid = true, NormalizedUrl = "https://example.com" });
        _htmlFetcherMock.Setup(x => x.FetchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ScrapeException("Unable to access the URL."));
        var sut = CreateSut();

        var result = await sut.ScrapeAsync(BuildConfig());

        result.Errors.Should().Contain("Unable to access the URL.");
    }

    [Fact]
    public async Task ScrapeAsync_SuccessfulExtraction_ReturnsRecords()
    {
        _urlValidationServiceMock.Setup(x => x.ValidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidateUrlResponseDto { IsValid = true, NormalizedUrl = "https://example.com" });
        _htmlFetcherMock.Setup(x => x.FetchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("<html><body><div class='card'>Test</div></body></html>");

        var doc = new HtmlAgilityPack.HtmlDocument();
        doc.LoadHtml("<div class='card'>Test</div>");
        _recordDetectorMock.Setup(x => x.DetectRecords(It.IsAny<HtmlAgilityPack.HtmlNode>(), It.IsAny<System.Collections.Generic.IReadOnlyList<FieldDefinitionDto>>()))
            .Returns([doc.DocumentNode]);

        _orchestratorMock
            .Setup(x => x.ExtractFieldAsync(It.IsAny<ExtractionContext>(), It.IsAny<FieldDefinitionDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Found("Test"));

        var sut = CreateSut();

        var result = await sut.ScrapeAsync(BuildConfig());

        result.TotalRecords.Should().Be(1);
        result.Records[0].Fields["Name"].Should().Be("Test");
    }

    [Fact]
    public async Task ScrapeAsync_NoRecordsFound_AddsWarning()
    {
        _urlValidationServiceMock.Setup(x => x.ValidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidateUrlResponseDto { IsValid = true, NormalizedUrl = "https://example.com" });
        _htmlFetcherMock.Setup(x => x.FetchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("<html></html>");

        _recordDetectorMock.Setup(x => x.DetectRecords(It.IsAny<HtmlAgilityPack.HtmlNode>(), It.IsAny<System.Collections.Generic.IReadOnlyList<FieldDefinitionDto>>()))
            .Returns((List<HtmlAgilityPack.HtmlNode>?)null);

        _orchestratorMock
            .Setup(x => x.ExtractFieldAsync(It.IsAny<ExtractionContext>(), It.IsAny<FieldDefinitionDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FieldResultDto { IsMissing = true });

        var sut = CreateSut();

        var result = await sut.ScrapeAsync(BuildConfig());

        // No detected structure falls back to whole-document-as-one-record.
        result.TotalRecords.Should().Be(1);
        result.Records[0].MissingFields.Should().Contain("Name");
    }

    [Fact]
    public async Task ScrapeAsync_ExplicitRecordSelectorWithNoMatches_AddsWarning()
    {
        _urlValidationServiceMock.Setup(x => x.ValidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidateUrlResponseDto { IsValid = true, NormalizedUrl = "https://example.com" });
        _htmlFetcherMock.Setup(x => x.FetchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("<html><body></body></html>");

        var config = BuildConfig();
        config.RecordSelector = ".does-not-exist";

        var sut = CreateSut();

        var result = await sut.ScrapeAsync(config);

        result.Warnings.Should().Contain("The record selector did not match any elements.");
        result.TotalRecords.Should().Be(0);
    }

    [Fact]
    public async Task ScrapeAsync_ProfileCrawlEnabled_ProfileDataOverridesMissingListingField()
    {
        _urlValidationServiceMock.Setup(x => x.ValidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string url, CancellationToken _) => new ValidateUrlResponseDto { IsValid = true, NormalizedUrl = url });

        var listingDoc = new HtmlAgilityPack.HtmlDocument();
        listingDoc.LoadHtml("<div class='card'><h2>Dr John Smith</h2></div>");
        var listingRecordNode = listingDoc.DocumentNode.SelectSingleNode("//div");

        var profileDoc = new HtmlAgilityPack.HtmlDocument();
        profileDoc.LoadHtml("<div><h1>Dr John Smith</h1><p class='bio'>A long biography paragraph about the doctor.</p></div>");

        _htmlFetcherMock.Setup(x => x.FetchAsync("https://example.com", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("<html><body><div class='card'><h2>Dr John Smith</h2></div></body></html>");
        _htmlFetcherMock.Setup(x => x.FetchAsync("https://example.com/profile/1", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("<html><body><div><h1>Dr John Smith</h1><p class='bio'>A long biography paragraph about the doctor.</p></div></body></html>");

        _recordDetectorMock
            .Setup(x => x.DetectRecords(It.IsAny<HtmlAgilityPack.HtmlNode>(), It.IsAny<System.Collections.Generic.IReadOnlyList<FieldDefinitionDto>>()))
            .Returns((HtmlAgilityPack.HtmlNode doc, System.Collections.Generic.IReadOnlyList<FieldDefinitionDto>? _) =>
                doc.OuterHtml.Contains("bio") ? null : [doc.SelectSingleNode("//div")]);

        _profileLinkDetectorMock
            .Setup(x => x.DetectProfileUrl(It.IsAny<HtmlAgilityPack.HtmlNode>(), It.IsAny<Uri>()))
            .Returns("https://example.com/profile/1");

        _orchestratorMock
            .Setup(x => x.ExtractFieldAsync(It.IsAny<ExtractionContext>(), It.IsAny<FieldDefinitionDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExtractionContext ctx, FieldDefinitionDto field, CancellationToken _) =>
                ctx.RecordNode.OuterHtml.Contains("bio")
                    ? new FieldResultDto { Value = "A long biography paragraph about the doctor.", IsMissing = false, ConfidenceLevel = ConfidenceLevel.High, Source = ExtractionSource.SemanticHtml }
                    : new FieldResultDto { IsMissing = true });

        var config = new ScraperConfigDto { Url = "https://example.com", Fields = [new FieldDefinitionDto { Name = "Bio" }] };
        var sut = CreateSut();

        var result = await sut.ScrapeAsync(config);

        result.TotalRecords.Should().Be(1);
        result.ProfilesVisited.Should().Be(1);
        result.Records[0].ProfileVisited.Should().BeTrue();
        result.Records[0].Fields["Bio"].Should().Be("A long biography paragraph about the doctor.");
        result.Records[0].MissingFields.Should().NotContain("Bio");
        result.Records[0].Fields["Profile URL"].Should().Be("https://example.com/profile/1");
    }

    [Fact]
    public async Task ScrapeAsync_ProfileCrawlDisabled_NeverFetchesProfileUrl()
    {
        _urlValidationServiceMock.Setup(x => x.ValidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string url, CancellationToken _) => new ValidateUrlResponseDto { IsValid = true, NormalizedUrl = url });
        _htmlFetcherMock.Setup(x => x.FetchAsync("https://example.com", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("<html><body><div class='card'><h2>Dr John Smith</h2></div></body></html>");

        var listingDoc = new HtmlAgilityPack.HtmlDocument();
        listingDoc.LoadHtml("<div class='card'><h2>Dr John Smith</h2></div>");
        _recordDetectorMock
            .Setup(x => x.DetectRecords(It.IsAny<HtmlAgilityPack.HtmlNode>(), It.IsAny<System.Collections.Generic.IReadOnlyList<FieldDefinitionDto>>()))
            .Returns([listingDoc.DocumentNode.SelectSingleNode("//div")]);

        _profileLinkDetectorMock
            .Setup(x => x.DetectProfileUrl(It.IsAny<HtmlAgilityPack.HtmlNode>(), It.IsAny<Uri>()))
            .Returns("https://example.com/profile/1");

        _orchestratorMock
            .Setup(x => x.ExtractFieldAsync(It.IsAny<ExtractionContext>(), It.IsAny<FieldDefinitionDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FieldResultDto { IsMissing = true });

        var config = new ScraperConfigDto
        {
            Url = "https://example.com",
            EnableProfileCrawl = false,
            Fields = [new FieldDefinitionDto { Name = "Bio" }],
        };
        var sut = CreateSut();

        var result = await sut.ScrapeAsync(config);

        result.ProfilesVisited.Should().Be(0);
        result.Records[0].ProfileVisited.Should().BeFalse();
        result.Records[0].Fields.Should().NotContainKey("Profile URL");
        _htmlFetcherMock.Verify(x => x.FetchAsync("https://example.com/profile/1", It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ScrapeAsync_DuplicateProfileUrlsAcrossRecords_OnlyVisitedOnce()
    {
        _urlValidationServiceMock.Setup(x => x.ValidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string url, CancellationToken _) => new ValidateUrlResponseDto { IsValid = true, NormalizedUrl = url });
        _htmlFetcherMock.Setup(x => x.FetchAsync("https://example.com", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("<html><body><div class='card'>A</div><div class='card'>B</div></body></html>");
        _htmlFetcherMock.Setup(x => x.FetchAsync("https://example.com/profile/shared", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("<html><body><p>Shared profile</p></body></html>");

        var doc = new HtmlAgilityPack.HtmlDocument();
        doc.LoadHtml("<div class='card'>A</div><div class='card'>B</div>");
        var nodes = doc.DocumentNode.SelectNodes("//div").ToList();
        _recordDetectorMock
            .Setup(x => x.DetectRecords(It.IsAny<HtmlAgilityPack.HtmlNode>(), It.IsAny<System.Collections.Generic.IReadOnlyList<FieldDefinitionDto>>()))
            .Returns(nodes);

        _profileLinkDetectorMock
            .Setup(x => x.DetectProfileUrl(It.IsAny<HtmlAgilityPack.HtmlNode>(), It.IsAny<Uri>()))
            .Returns("https://example.com/profile/shared");

        // Distinct values per record index — otherwise the "records that all produce
        // identical values aren't real records" safeguard would (correctly) collapse these
        // two into one whole-page record, and this test wants to verify dedup across two
        // genuinely distinct records instead.
        _orchestratorMock
            .Setup(x => x.ExtractFieldAsync(It.IsAny<ExtractionContext>(), It.IsAny<FieldDefinitionDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExtractionContext ctx, FieldDefinitionDto _, CancellationToken _) =>
                new FieldResultDto { Value = $"Doctor {ctx.RecordIndex}", IsMissing = false });

        var config = new ScraperConfigDto { Url = "https://example.com", Fields = [new FieldDefinitionDto { Name = "Name" }] };
        var sut = CreateSut();

        var result = await sut.ScrapeAsync(config);

        result.ProfilesVisited.Should().Be(1);
        _htmlFetcherMock.Verify(x => x.FetchAsync("https://example.com/profile/shared", It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ScrapeAsync_ProfileUrlBlockedByValidation_AddsWarningAndKeepsListingData()
    {
        _urlValidationServiceMock.Setup(x => x.ValidateAsync("https://example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidateUrlResponseDto { IsValid = true, NormalizedUrl = "https://example.com" });
        _urlValidationServiceMock.Setup(x => x.ValidateAsync("http://169.254.169.254/evil", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidateUrlResponseDto { IsValid = false, Reason = "Access to internal or private network addresses is not allowed." });

        _htmlFetcherMock.Setup(x => x.FetchAsync("https://example.com", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("<html><body><div class='card'>A</div></body></html>");

        var doc = new HtmlAgilityPack.HtmlDocument();
        doc.LoadHtml("<div class='card'>A</div>");
        _recordDetectorMock
            .Setup(x => x.DetectRecords(It.IsAny<HtmlAgilityPack.HtmlNode>(), It.IsAny<System.Collections.Generic.IReadOnlyList<FieldDefinitionDto>>()))
            .Returns([doc.DocumentNode.SelectSingleNode("//div")]);

        _profileLinkDetectorMock
            .Setup(x => x.DetectProfileUrl(It.IsAny<HtmlAgilityPack.HtmlNode>(), It.IsAny<Uri>()))
            .Returns("http://169.254.169.254/evil");

        _orchestratorMock
            .Setup(x => x.ExtractFieldAsync(It.IsAny<ExtractionContext>(), It.IsAny<FieldDefinitionDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FieldResultDto { Value = "Listing Name", IsMissing = false });

        var config = new ScraperConfigDto { Url = "https://example.com", Fields = [new FieldDefinitionDto { Name = "Name" }] };
        var sut = CreateSut();

        var result = await sut.ScrapeAsync(config);

        result.ProfilesVisited.Should().Be(0);
        result.Records[0].ProfileVisited.Should().BeFalse();
        result.Records[0].Fields["Name"].Should().Be("Listing Name");
        result.Warnings.Should().Contain(w => w.Contains("Skipped the profile page"));
        _htmlFetcherMock.Verify(x => x.FetchAsync("http://169.254.169.254/evil", It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ScrapeAsync_MaxProfilesLimit_StopsVisitingFurtherProfiles()
    {
        _urlValidationServiceMock.Setup(x => x.ValidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string url, CancellationToken _) => new ValidateUrlResponseDto { IsValid = true, NormalizedUrl = url });
        _htmlFetcherMock.Setup(x => x.FetchAsync("https://example.com", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("<html><body><div class='card'>A</div><div class='card'>B</div><div class='card'>C</div></body></html>");
        _htmlFetcherMock.Setup(x => x.FetchAsync(It.Is<string>(u => u.StartsWith("https://example.com/profile/")), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("<html><body><p>Profile</p></body></html>");

        var doc = new HtmlAgilityPack.HtmlDocument();
        doc.LoadHtml("<div class='card'>A</div><div class='card'>B</div><div class='card'>C</div>");
        var nodes = doc.DocumentNode.SelectNodes("//div").ToList();
        _recordDetectorMock
            .Setup(x => x.DetectRecords(It.IsAny<HtmlAgilityPack.HtmlNode>(), It.IsAny<System.Collections.Generic.IReadOnlyList<FieldDefinitionDto>>()))
            .Returns(nodes);

        var callIndex = 0;
        _profileLinkDetectorMock
            .Setup(x => x.DetectProfileUrl(It.IsAny<HtmlAgilityPack.HtmlNode>(), It.IsAny<Uri>()))
            .Returns(() => $"https://example.com/profile/{callIndex++}");

        _orchestratorMock
            .Setup(x => x.ExtractFieldAsync(It.IsAny<ExtractionContext>(), It.IsAny<FieldDefinitionDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExtractionContext ctx, FieldDefinitionDto _, CancellationToken _) =>
                new FieldResultDto { Value = $"Doctor {ctx.RecordIndex}", IsMissing = false });

        var config = new ScraperConfigDto
        {
            Url = "https://example.com",
            MaxProfiles = 1,
            Fields = [new FieldDefinitionDto { Name = "Name" }],
        };
        var sut = CreateSut();

        var result = await sut.ScrapeAsync(config);

        result.ProfilesVisited.Should().Be(1);
        result.Records.Count(r => r.ProfileVisited).Should().Be(1);
    }
}
