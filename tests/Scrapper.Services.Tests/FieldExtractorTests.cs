using FluentAssertions;
using Scrapper.Models.DTOs;
using Scrapper.Models.Enums;
using Scrapper.Services.Implementations;
using Xunit;

namespace Scrapper.Services.Tests;

public class FieldExtractorTests
{
    private const string SampleHtml = """
        <html><body>
        <div class="card">
            <h2 class="name">John Smith</h2>
            <span class="location">London</span>
            <a class="profile" href="/profile/1">View</a>
            <img class="avatar" src="/img/1.png" />
        </div>
        <div class="card">
            <h2 class="name">Jane Doe</h2>
            <span class="location">Manchester</span>
            <a class="profile" href="/profile/2">View</a>
            <img class="avatar" src="/img/2.png" />
        </div>
        <div class="card">
            <h2 class="name">No Location</h2>
        </div>
        </body></html>
        """;

    private readonly FieldExtractor _sut = new();

    private static ScraperConfigDto BuildConfig() => new()
    {
        Url = "https://example.com",
        RecordSelector = ".card",
        RecordSelectorType = SelectorType.Css,
        MaxRecords = 100,
        Fields =
        [
            new FieldDefinitionDto { Name = "Name", Selector = ".name", ExtractionType = ExtractionType.Text, Required = true },
            new FieldDefinitionDto { Name = "Location", Selector = ".location", ExtractionType = ExtractionType.Text },
            new FieldDefinitionDto { Name = "ProfileUrl", Selector = ".profile", ExtractionType = ExtractionType.Link },
            new FieldDefinitionDto { Name = "Avatar", Selector = ".avatar", ExtractionType = ExtractionType.Attribute, Attribute = "src" }
        ]
    };

    [Fact]
    public void ExtractRecords_TextExtraction_ReturnsInnerText()
    {
        var records = _sut.ExtractRecords(SampleHtml, BuildConfig(), out _);

        records[0].Fields["Name"].Should().Be("John Smith");
    }

    [Fact]
    public void ExtractRecords_AttributeExtraction_ReturnsAttributeValue()
    {
        var records = _sut.ExtractRecords(SampleHtml, BuildConfig(), out _);

        records[0].Fields["Avatar"].Should().Be("/img/1.png");
    }

    [Fact]
    public void ExtractRecords_LinkExtraction_ReturnsHref()
    {
        var records = _sut.ExtractRecords(SampleHtml, BuildConfig(), out _);

        records[0].Fields["ProfileUrl"].Should().Be("/profile/1");
    }

    [Fact]
    public void ExtractRecords_RepeatedRecords_ReturnsAllCards()
    {
        var records = _sut.ExtractRecords(SampleHtml, BuildConfig(), out _);

        records.Should().HaveCount(3);
    }

    [Fact]
    public void ExtractRecords_MissingElement_ReturnsNullAndTracksMissingField()
    {
        var records = _sut.ExtractRecords(SampleHtml, BuildConfig(), out _);

        var thirdRecord = records[2];
        thirdRecord.Fields["Location"].Should().BeNullOrEmpty();
        thirdRecord.MissingFields.Should().Contain("Location");
    }

    [Fact]
    public void ExtractRecords_EmptyValue_IsReportedAsMissing()
    {
        const string html = "<div class='card'><h2 class='name'></h2></div>";
        var config = new ScraperConfigDto
        {
            Url = "https://example.com",
            RecordSelector = ".card",
            Fields = [new FieldDefinitionDto { Name = "Name", Selector = ".name", Required = true }]
        };

        var records = _sut.ExtractRecords(html, config, out var warnings);

        records[0].MissingFields.Should().Contain("Name");
        warnings.Should().Contain(w => w.Contains("Name"));
    }

    [Fact]
    public void ExtractRecords_InvalidSelector_DoesNotThrow_ReturnsNoMatches()
    {
        var config = BuildConfig();
        config.Fields[0].Selector = ":::invalid:::";

        var act = () => _sut.ExtractRecords(SampleHtml, config, out _);

        act.Should().NotThrow();
    }

    [Fact]
    public void ExtractRecords_NoRecordSelectorMatch_ReturnsEmptyWithWarning()
    {
        var config = BuildConfig();
        config.RecordSelector = ".does-not-exist";

        var records = _sut.ExtractRecords(SampleHtml, config, out var warnings);

        records.Should().BeEmpty();
        warnings.Should().Contain(w => w.Contains("did not match"));
    }

    [Fact]
    public void ExtractRecords_XPathSelector_ExtractsValue()
    {
        var config = BuildConfig();
        config.RecordSelectorType = SelectorType.XPath;
        config.RecordSelector = "//div[@class='card']";
        config.Fields[0].SelectorType = SelectorType.XPath;
        config.Fields[0].Selector = ".//h2";

        var records = _sut.ExtractRecords(SampleHtml, config, out _);

        records[0].Fields["Name"].Should().Be("John Smith");
    }

    [Fact]
    public void TestSelector_MatchingSelector_ReturnsMatchCountAndPreviews()
    {
        var (matchCount, previews) = _sut.TestSelector(SampleHtml, ".card", SelectorType.Css, ".name", SelectorType.Css, ExtractionType.Text, null);

        matchCount.Should().Be(3);
        previews.Should().Contain("John Smith");
    }

    [Fact]
    public void TestSelector_NoMatches_ReturnsZeroCount()
    {
        var (matchCount, previews) = _sut.TestSelector(SampleHtml, null, SelectorType.Css, ".nonexistent", SelectorType.Css, ExtractionType.Text, null);

        matchCount.Should().Be(0);
        previews.Should().BeEmpty();
    }
}
