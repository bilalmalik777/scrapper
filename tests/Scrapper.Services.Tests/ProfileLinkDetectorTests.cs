using FluentAssertions;
using HtmlAgilityPack;
using Scrapper.Services.Implementations;
using Xunit;

namespace Scrapper.Services.Tests;

public class ProfileLinkDetectorTests
{
    private readonly ProfileLinkDetector _sut = new();
    private static readonly Uri PageUri = new("https://example.com/uk/find/specialists");

    private static HtmlNode LoadRecord(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        return doc.DocumentNode.SelectSingleNode("//div");
    }

    [Fact]
    public void DetectProfileUrl_LinkWrappingHeading_ReturnsResolvedAbsoluteUrl()
    {
        var record = LoadRecord("""
            <div class="card"><a href="/uk/specialist/dr-john-smith"><h2>Dr John Smith</h2></a></div>
            """);

        var url = _sut.DetectProfileUrl(record, PageUri);

        url.Should().Be("https://example.com/uk/specialist/dr-john-smith");
    }

    [Fact]
    public void DetectProfileUrl_HeadingInsideLink_ReturnsResolvedUrl()
    {
        var record = LoadRecord("""
            <div class="card"><h2><a href="/uk/specialist/dr-jane-doe">Dr Jane Doe</a></h2></div>
            """);

        var url = _sut.DetectProfileUrl(record, PageUri);

        url.Should().Be("https://example.com/uk/specialist/dr-jane-doe");
    }

    [Fact]
    public void DetectProfileUrl_NoHeading_FallsBackToSubstantialLink()
    {
        var record = LoadRecord("""
            <div class="card"><span>Dr John Smith</span><a href="/uk/specialist/dr-john-smith">View profile</a></div>
            """);

        var url = _sut.DetectProfileUrl(record, PageUri);

        url.Should().Be("https://example.com/uk/specialist/dr-john-smith");
    }

    [Fact]
    public void DetectProfileUrl_OnlyPaginationLikeLinks_ReturnsNull()
    {
        var record = LoadRecord("""
            <div class="card"><a href="/page/2">2</a><a href="/page/next">Next</a></div>
            """);

        var url = _sut.DetectProfileUrl(record, PageUri);

        url.Should().BeNull();
    }

    [Fact]
    public void DetectProfileUrl_CrossDomainLink_IsIgnored()
    {
        var record = LoadRecord("""
            <div class="card"><h2><a href="https://other-domain.com/profile/1">Dr John Smith</a></h2></div>
            """);

        var url = _sut.DetectProfileUrl(record, PageUri);

        url.Should().BeNull();
    }

    [Fact]
    public void DetectProfileUrl_NoLinksAtAll_ReturnsNull()
    {
        var record = LoadRecord("<div class='card'><span>Dr John Smith</span></div>");

        var url = _sut.DetectProfileUrl(record, PageUri);

        url.Should().BeNull();
    }

    [Fact]
    public void DetectProfileUrl_HashOrJavascriptLinks_AreIgnored()
    {
        var record = LoadRecord("""
            <div class="card"><h2><a href="#">Dr John Smith</a></h2><a href="javascript:void(0)">Book</a></div>
            """);

        var url = _sut.DetectProfileUrl(record, PageUri);

        url.Should().BeNull();
    }

    [Fact]
    public void DetectNextPageUrl_RelNextAttribute_ReturnsResolvedUrl()
    {
        var doc = new HtmlDocument();
        doc.LoadHtml("<html><body><a rel=\"next\" href=\"/uk/find/specialists?page=2\">Next</a></body></html>");

        var url = _sut.DetectNextPageUrl(doc.DocumentNode, PageUri);

        url.Should().Be("https://example.com/uk/find/specialists?page=2");
    }

    [Fact]
    public void DetectNextPageUrl_NextLinkText_ReturnsResolvedUrl()
    {
        var doc = new HtmlDocument();
        doc.LoadHtml("<html><body><a href=\"/uk/find/specialists?page=2\">Next</a></body></html>");

        var url = _sut.DetectNextPageUrl(doc.DocumentNode, PageUri);

        url.Should().Be("https://example.com/uk/find/specialists?page=2");
    }

    [Fact]
    public void DetectNextPageUrl_NoNextLink_ReturnsNull()
    {
        var doc = new HtmlDocument();
        doc.LoadHtml("<html><body><a href=\"/uk/find/specialists?page=1\">1</a></body></html>");

        var url = _sut.DetectNextPageUrl(doc.DocumentNode, PageUri);

        url.Should().BeNull();
    }

    [Fact]
    public void DetectNextPageUrl_CrossDomainNextLink_ReturnsNull()
    {
        var doc = new HtmlDocument();
        doc.LoadHtml("<html><body><a rel=\"next\" href=\"https://other.com/page/2\">Next</a></body></html>");

        var url = _sut.DetectNextPageUrl(doc.DocumentNode, PageUri);

        url.Should().BeNull();
    }
}
