using FluentAssertions;
using Scrapper.Models.Enums;
using Scrapper.Utils.Helpers;
using Xunit;

namespace Scrapper.Services.Tests;

public class FieldValueValidatorTests
{
    [Theory]
    [InlineData(FieldKind.Currency, "£150", true)]
    [InlineData(FieldKind.Currency, "From £120", true)]
    [InlineData(FieldKind.Currency, "150 GBP", true)]
    [InlineData(FieldKind.Currency, "Manchester", false)]
    public void IsPlausible_Currency(FieldKind kind, string value, bool expected)
    {
        FieldValueValidator.IsPlausible(kind, value).Should().Be(expected);
    }

    [Theory]
    [InlineData("john@example.com", true)]
    [InlineData("not-an-email", false)]
    public void IsPlausible_Email(string value, bool expected)
    {
        FieldValueValidator.IsPlausible(FieldKind.Email, value).Should().Be(expected);
    }

    [Fact]
    public void IsPlausible_Location_RejectsCurrencyLookingValue()
    {
        FieldValueValidator.IsPlausible(FieldKind.Location, "£150").Should().BeFalse();
    }

    [Fact]
    public void IsPlausible_Location_AcceptsPlaceName()
    {
        FieldValueValidator.IsPlausible(FieldKind.Location, "Manchester").Should().BeTrue();
    }

    [Fact]
    public void IsPlausible_EmptyValue_ReturnsFalse()
    {
        FieldValueValidator.IsPlausible(FieldKind.Text, "  ").Should().BeFalse();
    }
}
