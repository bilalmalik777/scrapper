using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Scrapper.Services.Implementations;
using Xunit;

namespace Scrapper.Services.Tests;

public class UrlValidationServiceTests
{
    private readonly UrlValidationService _sut = new(NullLogger<UrlValidationService>.Instance);

    [Fact]
    public async Task ValidateAsync_PublicIpLiteral_ReturnsValid()
    {
        var result = await _sut.ValidateAsync("http://8.8.8.8/");

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_PrivateIp_ReturnsInvalid()
    {
        var result = await _sut.ValidateAsync("http://10.0.0.1/");

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_Localhost_ReturnsInvalid()
    {
        var result = await _sut.ValidateAsync("http://localhost:5000/");

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_MalformedUrl_ReturnsInvalid()
    {
        var result = await _sut.ValidateAsync("htp:/bad-url");

        result.IsValid.Should().BeFalse();
        result.Reason.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ValidateAsync_UnsupportedProtocol_ReturnsInvalid()
    {
        var result = await _sut.ValidateAsync("ftp://example.com/file");

        result.IsValid.Should().BeFalse();
    }
}
