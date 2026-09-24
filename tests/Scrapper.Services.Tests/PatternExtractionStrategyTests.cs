using FluentAssertions;
using HtmlAgilityPack;
using Scrapper.Models.DTOs;
using Scrapper.Models.Enums;
using Scrapper.Services.Implementations.Strategies;
using Scrapper.Services.Models;
using Xunit;

namespace Scrapper.Services.Tests;

public class PatternExtractionStrategyTests
{
    private readonly PatternExtractionStrategy _sut = new();

    private static ExtractionContext BuildContext(string recordHtml)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(recordHtml);
        return new ExtractionContext
        {
            RecordNode = doc.DocumentNode,
            DocumentNode = doc.DocumentNode,
            RecordIndex = 0,
            TotalRecords = 1,
            StructuredDataObjects = [],
        };
    }

    [Fact]
    public async Task TryExtractAsync_CurrencyPattern_FindsPoundValue()
    {
        var context = BuildContext("<div>Consultation from £150 per session</div>");
        var candidate = await _sut.TryExtractAsync(context, new FieldDefinitionDto { Name = "Fees" }, FieldKind.Currency, CancellationToken.None);

        candidate.Should().NotBeNull();
        candidate!.Value.Should().Contain("150");
    }

    [Fact]
    public async Task TryExtractAsync_EmailPattern_FindsEmail()
    {
        var context = BuildContext("<div>Contact: john.smith@example.com</div>");
        var candidate = await _sut.TryExtractAsync(context, new FieldDefinitionDto { Name = "Email" }, FieldKind.Email, CancellationToken.None);

        candidate.Should().NotBeNull();
        candidate!.Value.Should().Be("john.smith@example.com");
    }

    [Fact]
    public async Task TryExtractAsync_KindWithoutPattern_ReturnsNull()
    {
        var context = BuildContext("<div>Dr John Smith</div>");
        var candidate = await _sut.TryExtractAsync(context, new FieldDefinitionDto { Name = "Name" }, FieldKind.Name, CancellationToken.None);

        candidate.Should().BeNull();
    }

    [Fact]
    public async Task TryExtractAsync_LocationPattern_ExtractsAddressBoundedByPostcode()
    {
        var context = BuildContext("<div>298.70 miles | 32 St Thomas St, London, SE1 9BSProlotherapy(7)+132Contact</div>");
        var candidate = await _sut.TryExtractAsync(context, new FieldDefinitionDto { Name = "Location" }, FieldKind.Location, CancellationToken.None);

        candidate.Should().NotBeNull();
        candidate!.Value.Should().Be("32 St Thomas St, London, SE1 9BS");
    }

    [Fact]
    public async Task TryExtractAsync_NoPatternMatch_ReturnsNull()
    {
        var context = BuildContext("<div>No currency information here</div>");
        var candidate = await _sut.TryExtractAsync(context, new FieldDefinitionDto { Name = "Fees" }, FieldKind.Currency, CancellationToken.None);

        candidate.Should().BeNull();
    }

    [Fact]
    public async Task TryExtractAsync_MultipleLocationsOnOnePage_JoinsAllOfThem()
    {
        var context = BuildContext("""
            <div>
              London Bridge Sports Medicine, 32 St Thomas St, London, SE1 9BS
              HCA UK at The Shard, St Thomas Street, London, SE1 9BS
              The Institute of Sport, 170 Tottenham Court Road, London, W1T 7HA
            </div>
            """);

        var candidate = await _sut.TryExtractAsync(context, new FieldDefinitionDto { Name = "Location" }, FieldKind.Location, CancellationToken.None);

        candidate.Should().NotBeNull();
        candidate!.Value.Should().Contain("SE1 9BS");
        candidate.Value.Should().Contain("W1T 7HA");
        candidate.Value.Split("; ").Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task TryExtractAsync_LocationMatchContainingReviewNoise_IsFilteredOut()
    {
        // A repeated rating/review summary blurb that merely ends near a postcode-shaped
        // string is not a real address and must not be joined in alongside genuine ones.
        var context = BuildContext("""
            <div>
              4.95 /5 (877 reviews) 18 years of experience London, SE1 9BS
              London Bridge Sports Medicine, 32 St Thomas St, London, SE1 9BS
            </div>
            """);

        var candidate = await _sut.TryExtractAsync(context, new FieldDefinitionDto { Name = "Location" }, FieldKind.Location, CancellationToken.None);

        candidate.Should().NotBeNull();
        candidate!.Value.Should().NotContain("reviews");
        candidate.Value.Should().Contain("London Bridge Sports Medicine");
    }
}
