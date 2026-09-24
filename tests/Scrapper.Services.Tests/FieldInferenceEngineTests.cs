using FluentAssertions;
using Scrapper.Models.Enums;
using Scrapper.Services.Implementations;
using Xunit;

namespace Scrapper.Services.Tests;

public class FieldInferenceEngineTests
{
    private readonly FieldInferenceEngine _sut = new();

    [Theory]
    [InlineData("Name", FieldKind.Name)]
    [InlineData("Full Name", FieldKind.Name)]
    [InlineData("Specialist", FieldKind.Name)]
    [InlineData("Location", FieldKind.Location)]
    [InlineData("Address", FieldKind.Location)]
    [InlineData("Fees", FieldKind.Currency)]
    [InlineData("Consultation Fee", FieldKind.Currency)]
    [InlineData("Treatment Price", FieldKind.Currency)]
    [InlineData("Cost", FieldKind.Currency)]
    [InlineData("Pricing", FieldKind.Currency)]
    [InlineData("Email", FieldKind.Email)]
    [InlineData("Phone", FieldKind.Phone)]
    [InlineData("Website", FieldKind.Url)]
    [InlineData("Rating", FieldKind.Rating)]
    public void Infer_KnownFieldNames_ReturnsExpectedKind(string fieldName, FieldKind expected)
    {
        _sut.Infer(fieldName).Should().Be(expected);
    }

    [Fact]
    public void Infer_UnrecognizedFieldName_ReturnsText()
    {
        _sut.Infer("Zorblax").Should().Be(FieldKind.Text);
    }

    [Fact]
    public void Infer_EmptyFieldName_ReturnsText()
    {
        _sut.Infer("").Should().Be(FieldKind.Text);
    }
}
