using FluentAssertions;
using Scrapper.Utils.Helpers;
using Xunit;

namespace Scrapper.Services.Tests;

public class UrlSecurityHelperTests
{
    [Theory]
    [InlineData("https://example.com/page")]
    [InlineData("http://example.com")]
    public void TryParseAllowedUrl_ValidPublicUrl_ReturnsTrue(string url)
    {
        var result = UrlSecurityHelper.TryParseAllowedUrl(url, out var uri, out var reason);

        result.Should().BeTrue();
        uri.Should().NotBeNull();
        reason.Should().BeNull();
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParseAllowedUrl_MalformedUrl_ReturnsFalse(string url)
    {
        var result = UrlSecurityHelper.TryParseAllowedUrl(url, out _, out var reason);

        result.Should().BeFalse();
        reason.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("ftp://example.com")]
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    public void TryParseAllowedUrl_UnsupportedProtocol_ReturnsFalse(string url)
    {
        var result = UrlSecurityHelper.TryParseAllowedUrl(url, out _, out var reason);

        result.Should().BeFalse();
        reason.Should().Contain("http");
    }

    [Theory]
    [InlineData("http://localhost")]
    [InlineData("http://localhost:8080")]
    [InlineData("http://127.0.0.1")]
    [InlineData("http://0.0.0.0")]
    public void TryParseAllowedUrl_Localhost_ReturnsFalse(string url)
    {
        var result = UrlSecurityHelper.TryParseAllowedUrl(url, out _, out var reason);

        result.Should().BeFalse();
        reason.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("http://10.0.0.5")]
    [InlineData("http://172.16.0.1")]
    [InlineData("http://172.31.255.255")]
    [InlineData("http://192.168.1.1")]
    [InlineData("http://169.254.169.254")]
    public void TryParseAllowedUrl_PrivateIp_ReturnsFalse(string url)
    {
        var result = UrlSecurityHelper.TryParseAllowedUrl(url, out _, out var reason);

        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("http://172.15.255.255")]
    [InlineData("http://172.32.0.1")]
    [InlineData("http://8.8.8.8")]
    public void TryParseAllowedUrl_PublicIpJustOutsidePrivateRanges_ReturnsTrue(string url)
    {
        var result = UrlSecurityHelper.TryParseAllowedUrl(url, out _, out _);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ResolvesToBlockedAddressAsync_LiteralPrivateIp_ReturnsTrue()
    {
        var result = await UrlSecurityHelper.ResolvesToBlockedAddressAsync("192.168.0.1");

        result.Should().BeTrue();
    }
}
