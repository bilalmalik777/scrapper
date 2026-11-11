using Scrapper.Utils.Helpers;
using Xunit;

namespace Scrapper.Services.Tests;

public class PagedUrlBuilderTests
{
    [Theory]
    [InlineData("https://www.doctify.com/uk/find/chiropractic/london/specialists/page-1", 6, "https://www.doctify.com/uk/find/chiropractic/london/specialists/page-6")]
    [InlineData("https://example.com/list/page/2/", 10, "https://example.com/list/page/10/")]
    [InlineData("https://example.com/list", 3, "https://example.com/list?page=3")]
    [InlineData("https://example.com/list?a=1&page=2", 4, "https://example.com/list?a=1&page=4")]
    public void BuildPageUrl_UsesTheSitesPaginationScheme(string baseUrl, int page, string expected) =>
        Assert.Equal(expected, PagedUrlBuilder.BuildPageUrl(baseUrl, page));
}
