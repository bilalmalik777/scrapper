using FluentAssertions;
using HtmlAgilityPack;
using Moq;
using Scrapper.Models.DTOs;
using Scrapper.Models.Enums;
using Scrapper.Services.Implementations;
using Scrapper.Services.Interfaces;
using Scrapper.Services.Models;
using Xunit;

namespace Scrapper.Services.Tests;

public class FieldExtractionOrchestratorTests
{
    private readonly Mock<IFieldInferenceEngine> _inferenceEngineMock = new();

    private static ExtractionContext BuildContext()
    {
        var doc = new HtmlDocument();
        doc.LoadHtml("<div></div>");
        return new ExtractionContext
        {
            RecordNode = doc.DocumentNode,
            DocumentNode = doc.DocumentNode,
            RecordIndex = 0,
            TotalRecords = 1,
            StructuredDataObjects = [],
        };
    }

    private static Mock<IFieldExtractionStrategy> StrategyReturning(ExtractionSource source, ExtractionCandidate? candidate)
    {
        var mock = new Mock<IFieldExtractionStrategy>();
        mock.SetupGet(x => x.Source).Returns(source);
        mock.Setup(x => x.TryExtractAsync(It.IsAny<ExtractionContext>(), It.IsAny<FieldDefinitionDto>(), It.IsAny<FieldKind>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidate);
        return mock;
    }

    [Fact]
    public async Task ExtractFieldAsync_ExplicitTypeProvided_SkipsInference()
    {
        var field = new FieldDefinitionDto { Name = "Whatever", Type = FieldKind.Currency };
        var strategy = StrategyReturning(ExtractionSource.Pattern, new ExtractionCandidate("£100", 65, ExtractionSource.Pattern));
        var sut = new FieldExtractionOrchestrator([strategy.Object], _inferenceEngineMock.Object);

        var result = await sut.ExtractFieldAsync(BuildContext(), field, CancellationToken.None);

        result.ResolvedType.Should().Be(FieldKind.Currency);
        _inferenceEngineMock.Verify(x => x.Infer(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ExtractFieldAsync_NoTypeProvided_UsesInferenceEngine()
    {
        _inferenceEngineMock.Setup(x => x.Infer("Fees")).Returns(FieldKind.Currency);
        var field = new FieldDefinitionDto { Name = "Fees" };
        var strategy = StrategyReturning(ExtractionSource.Pattern, new ExtractionCandidate("£100", 65, ExtractionSource.Pattern));
        var sut = new FieldExtractionOrchestrator([strategy.Object], _inferenceEngineMock.Object);

        var result = await sut.ExtractFieldAsync(BuildContext(), field, CancellationToken.None);

        result.ResolvedType.Should().Be(FieldKind.Currency);
        _inferenceEngineMock.Verify(x => x.Infer("Fees"), Times.Once);
    }

    [Fact]
    public async Task ExtractFieldAsync_FirstStrategyValidates_StopsAtFirstMatch()
    {
        var field = new FieldDefinitionDto { Name = "Fees", Type = FieldKind.Currency };
        var first = StrategyReturning(ExtractionSource.Selector, new ExtractionCandidate("£150", 99, ExtractionSource.Selector));
        var second = StrategyReturning(ExtractionSource.Pattern, new ExtractionCandidate("£999", 65, ExtractionSource.Pattern));
        var sut = new FieldExtractionOrchestrator([first.Object, second.Object], _inferenceEngineMock.Object);

        var result = await sut.ExtractFieldAsync(BuildContext(), field, CancellationToken.None);

        result.Value.Should().Be("£150");
        result.Source.Should().Be(ExtractionSource.Selector);
        second.Verify(x => x.TryExtractAsync(It.IsAny<ExtractionContext>(), It.IsAny<FieldDefinitionDto>(), It.IsAny<FieldKind>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExtractFieldAsync_HighConfidenceCandidate_MapsToHighLevel()
    {
        var field = new FieldDefinitionDto { Name = "Fees", Type = FieldKind.Currency };
        var strategy = StrategyReturning(ExtractionSource.StructuredData, new ExtractionCandidate("£150", 90, ExtractionSource.StructuredData));
        var sut = new FieldExtractionOrchestrator([strategy.Object], _inferenceEngineMock.Object);

        var result = await sut.ExtractFieldAsync(BuildContext(), field, CancellationToken.None);

        result.ConfidenceLevel.Should().Be(ConfidenceLevel.High);
        result.IsMissing.Should().BeFalse();
    }

    [Fact]
    public async Task ExtractFieldAsync_InvalidCandidateForKind_DowngradedToLowConfidence()
    {
        var field = new FieldDefinitionDto { Name = "Fees", Type = FieldKind.Currency };
        // "Manchester" doesn't look like a currency value — should be downgraded, not accepted outright.
        var strategy = StrategyReturning(ExtractionSource.SemanticHtml, new ExtractionCandidate("Manchester", 80, ExtractionSource.SemanticHtml));
        var sut = new FieldExtractionOrchestrator([strategy.Object], _inferenceEngineMock.Object);

        var result = await sut.ExtractFieldAsync(BuildContext(), field, CancellationToken.None);

        result.Value.Should().Be("Manchester");
        result.Confidence.Should().BeLessThan(80);
        result.ConfidenceLevel.Should().Be(ConfidenceLevel.Low);
        result.IsMissing.Should().BeFalse();
    }

    [Fact]
    public async Task ExtractFieldAsync_NoStrategyProducesCandidate_ReturnsMissing()
    {
        var field = new FieldDefinitionDto { Name = "Fees", Type = FieldKind.Currency };
        var strategy = StrategyReturning(ExtractionSource.Pattern, null);
        var sut = new FieldExtractionOrchestrator([strategy.Object], _inferenceEngineMock.Object);

        var result = await sut.ExtractFieldAsync(BuildContext(), field, CancellationToken.None);

        result.IsMissing.Should().BeTrue();
        result.Value.Should().BeNull();
        result.ConfidenceLevel.Should().Be(ConfidenceLevel.None);
    }
}
