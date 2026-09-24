using FluentAssertions;
using Scrapper.Utils.Helpers;

namespace Scrapper.Services.Tests;

public class PagedUrlBuilderTests
{
    [Fact]
    public void BuildPageUrl_ReplacesExistingPageParam()
    {
        var url = PagedUrlBuilder.BuildPageUrl("https://example.com/search?area=doctor&page=1", 5);

        url.Should().Be("https://example.com/search?area=doctor&page=5");
    }

    [Fact]
    public void BuildPageUrl_AddsPageParamWhenMissing()
    {
        var url = PagedUrlBuilder.BuildPageUrl("https://example.com/search?area=doctor", 3);

        url.Should().Be("https://example.com/search?area=doctor&page=3");
    }

    [Fact]
    public void BuildPageUrl_AddsPageParamWhenNoQueryStringAtAll()
    {
        var url = PagedUrlBuilder.BuildPageUrl("https://example.com/search", 2);

        url.Should().Be("https://example.com/search?page=2");
    }
}
