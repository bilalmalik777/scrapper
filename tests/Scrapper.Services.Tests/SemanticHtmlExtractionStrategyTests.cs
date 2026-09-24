using FluentAssertions;
using HtmlAgilityPack;
using Scrapper.Models.DTOs;
using Scrapper.Models.Enums;
using Scrapper.Services.Implementations.Strategies;
using Scrapper.Services.Models;
using Xunit;

namespace Scrapper.Services.Tests;

public class SemanticHtmlExtractionStrategyTests
{
    private readonly SemanticHtmlExtractionStrategy _sut = new();

    private static ExtractionContext BuildContext(string recordHtml)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(recordHtml);
        var record = doc.DocumentNode.SelectSingleNode("//div") ?? doc.DocumentNode;
        return new ExtractionContext
        {
            RecordNode = record,
            DocumentNode = doc.DocumentNode,
            RecordIndex = 0,
            TotalRecords = 1,
            StructuredDataObjects = [],
        };
    }

    [Fact]
    public async Task TryExtractAsync_NameKind_PrefersHeading()
    {
        var context = BuildContext("<div><h2>Dr John Smith</h2><span class='location'>Manchester</span></div>");
        var field = new FieldDefinitionDto { Name = "Name" };

        var candidate = await _sut.TryExtractAsync(context, field, FieldKind.Name, CancellationToken.None);

        candidate.Should().NotBeNull();
        candidate!.Value.Should().Be("Dr John Smith");
    }

    [Fact]
    public async Task TryExtractAsync_LocationKind_MatchesClassSynonym()
    {
        var context = BuildContext("<div><h2>Dr John Smith</h2><div class='location'>Manchester</div></div>");
        var field = new FieldDefinitionDto { Name = "Location" };

        var candidate = await _sut.TryExtractAsync(context, field, FieldKind.Location, CancellationToken.None);

        candidate.Should().NotBeNull();
        candidate!.Value.Should().Be("Manchester");
    }

    [Fact]
    public async Task TryExtractAsync_DefinitionListPair_ExtractsValueFromDd()
    {
        var context = BuildContext("<div><dt>Fee</dt><dd>£150</dd></div>");
        var field = new FieldDefinitionDto { Name = "Fees" };

        var candidate = await _sut.TryExtractAsync(context, field, FieldKind.Currency, CancellationToken.None);

        candidate.Should().NotBeNull();
        candidate!.Value.Should().Be("£150");
    }

    [Fact]
    public async Task TryExtractAsync_NoMatchingContent_ReturnsNull()
    {
        var context = BuildContext("<div><p>Nothing relevant here</p></div>");
        var field = new FieldDefinitionDto { Name = "Fees" };

        var candidate = await _sut.TryExtractAsync(context, field, FieldKind.Currency, CancellationToken.None);

        candidate.Should().BeNull();
    }

    [Fact]
    public async Task TryExtractAsync_TextKind_ExtractsFullListSectionNotJustFirstItem()
    {
        // A "Qualifications"-style section: a labelled container wrapping a whole bullet
        // list, longer than the 200-char cap used for short single-value fields — the
        // engine should return the whole list, not truncate to the first item or nothing.
        var context = BuildContext("""
            <div>
              <span data-testid="about-qualifications">
                <ul>
                  <li>MBBS University College London 2008</li>
                  <li>Neuroscience BSc University College London 2005</li>
                  <li>Orthopaedic Engineering MSc Cardiff University 2013</li>
                </ul>
              </span>
            </div>
            """);
        var field = new FieldDefinitionDto { Name = "Qualifications" };

        var candidate = await _sut.TryExtractAsync(context, field, FieldKind.Text, CancellationToken.None);

        candidate.Should().NotBeNull();
        candidate!.Value.Should().Contain("MBBS");
        candidate.Value.Should().Contain("Neuroscience BSc");
        candidate.Value.Should().Contain("Orthopaedic Engineering MSc");
    }

    [Fact]
    public async Task TryExtractAsync_TextKind_LooseWordMatchFindsAgeRangesSection()
    {
        // Field name "Age Ranges" against a testid "about-age-ranges" — neither string
        // contains the other in full once "Treats" isn't part of the testid, but they share
        // the significant word "ranges".
        var context = BuildContext("""
            <div>
              <ul data-testid="about-age-ranges">
                <li>13-17 years</li>
                <li>18-64 years</li>
              </ul>
            </div>
            """);
        var field = new FieldDefinitionDto { Name = "Age Ranges" };

        var candidate = await _sut.TryExtractAsync(context, field, FieldKind.Text, CancellationToken.None);

        candidate.Should().NotBeNull();
        candidate!.Value.Should().Contain("13-17 years");
    }
}
