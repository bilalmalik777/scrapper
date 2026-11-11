using FluentAssertions;
using HtmlAgilityPack;
using Moq;
using Scrapper.Models.DTOs;
using Scrapper.Models.Enums;
using Scrapper.Services.Implementations;
using Scrapper.Services.Interfaces;
using Scrapper.Services.Models;

namespace Scrapper.Services.Tests;

public class ProfileFieldMergerTests
{
    private static FieldResultDto Missing(FieldKind kind) => new() { ResolvedType = kind, IsMissing = true };

    private static (ProfileFieldMerger Sut, ScrapedRecordDto Record, List<FieldDefinitionDto> Fields) Build()
    {
        var parser = new Mock<IStructuredDataParser>();
        parser.Setup(p => p.Parse(It.IsAny<HtmlNode>())).Returns([]);
        var orchestrator = new Mock<IFieldExtractionOrchestrator>();
        orchestrator.Setup(o => o.ExtractFieldAsync(It.IsAny<ExtractionContext>(), It.IsAny<FieldDefinitionDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExtractionContext _, FieldDefinitionDto f, CancellationToken _) => Missing(f.Type ?? FieldKind.Text));

        var record = new ScrapedRecordDto { Fields = { ["Location"] = "108 Harley St", ["Role"] = "Dermatologist" } };
        var fields = new List<FieldDefinitionDto>
        {
            new() { Name = "Location", Type = FieldKind.Location },
            new() { Name = "Role", Type = FieldKind.Text },
        };
        return (new ProfileFieldMerger(parser.Object, orchestrator.Object), record, fields);
    }

    private static HtmlNode Doc(string body)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml($"<html><body>{body}</body></html>");
        return doc.DocumentNode;
    }

    [Fact]
    public async Task ContactUnavailableNotice_ClearsStaleListingContactValue_ButKeepsOtherFields()
    {
        var (sut, record, fields) = Build();

        await sut.MergeProfileFieldsAsync(record, Doc("<span>It is not possible to contact or book an appointment with this doctor.</span>"), fields, CancellationToken.None);

        record.Fields["Location"].Should().BeNull();
        record.MissingFields.Should().Contain("Location");
        record.Fields["Role"].Should().Be("Dermatologist");
    }

    [Fact]
    public async Task NormalProfile_WithNoLocation_KeepsListingValue()
    {
        var (sut, record, fields) = Build();

        await sut.MergeProfileFieldsAsync(record, Doc("<p>Nothing relevant</p>"), fields, CancellationToken.None);

        record.Fields["Location"].Should().Be("108 Harley St");
    }
}
