using System.Net;
using FluentAssertions;
using Scrapper.Services.Implementations;
using Scrapper.Services.Interfaces;

namespace Scrapper.Services.Tests;

public class CrawlHttpSessionTests
{
    private const string TestUrl = "http://8.8.8.8/page"; // Raw IP: skips DNS, and 8.8.8.8 isn't a private/reserved address.

    private static CrawlHttpSession CreateSut(HttpMessageHandler handler, string? userAgent = null) =>
        new(userAgent, 10, handler);

    [Fact]
    public async Task FetchAsync_SuccessfulResponse_ReturnsHtml()
    {
        using var sut = CreateSut(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html><body>hello</body></html>")
        }));

        var result = await sut.FetchAsync(TestUrl, null);

        result.IsSuccess.Should().BeTrue();
        result.Html.Should().Contain("hello");
        result.Outcome.Should().Be(PageFetchOutcome.Success);
    }

    [Fact]
    public async Task FetchAsync_Http403_ReturnsForbiddenOutcome()
    {
        using var sut = CreateSut(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)));

        var result = await sut.FetchAsync(TestUrl, null);

        result.IsSuccess.Should().BeFalse();
        result.Outcome.Should().Be(PageFetchOutcome.Forbidden);
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task FetchAsync_Http429_ReturnsRateLimitedWithRetryAfter()
    {
        using var sut = CreateSut(new FakeHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
            return response;
        }));

        var result = await sut.FetchAsync(TestUrl, null);

        result.Outcome.Should().Be(PageFetchOutcome.RateLimited);
        result.RetryAfter.Should().Be(TimeSpan.FromSeconds(7));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task FetchAsync_ServerErrors_ReturnServerErrorOutcome(HttpStatusCode statusCode)
    {
        using var sut = CreateSut(new FakeHttpMessageHandler(_ => new HttpResponseMessage(statusCode)));

        var result = await sut.FetchAsync(TestUrl, null);

        result.Outcome.Should().Be(PageFetchOutcome.ServerError);
        result.StatusCode.Should().Be((int)statusCode);
    }

    [Fact]
    public async Task FetchAsync_EmptyBody_ReturnsEmptyResponseOutcome()
    {
        using var sut = CreateSut(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("   ")
        }));

        var result = await sut.FetchAsync(TestUrl, null);

        result.Outcome.Should().Be(PageFetchOutcome.EmptyResponse);
    }

    [Theory]
    [InlineData("<html><body>Just a moment...</body></html>")]
    [InlineData("<title>Verify you are human</title>")]
    [InlineData("<div class=\"cf-challenge\">blocked</div>")]
    public async Task FetchAsync_ChallengePageMarkers_ReturnsChallengePageOutcome(string body)
    {
        using var sut = CreateSut(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body)
        }));

        var result = await sut.FetchAsync(TestUrl, null);

        result.Outcome.Should().Be(PageFetchOutcome.ChallengePage);
    }

    [Fact]
    public async Task FetchAsync_SameSessionReusesOneUnderlyingHttpClientAcrossCalls()
    {
        // Cookie persistence itself is delegated to HttpClientHandler's own CookieContainer
        // (framework-provided, exercised by the real constructor in CrawlHttpSessionFactory) —
        // what this test covers is the part that IS this class's own responsibility: reusing
        // one client/session for every page rather than creating a new one per request.
        var requestCount = 0;
        using var sut = CreateSut(new FakeHttpMessageHandler(_ =>
        {
            requestCount++;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"<html>{requestCount}</html>") };
        }));

        var first = await sut.FetchAsync(TestUrl, null);
        var second = await sut.FetchAsync(TestUrl, null);

        first.Html.Should().Contain("1");
        second.Html.Should().Contain("2");
        requestCount.Should().Be(2);
    }
}
