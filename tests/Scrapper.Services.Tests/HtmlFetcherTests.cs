using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Scrapper.Models.Common;
using Scrapper.Services.Implementations;
using Scrapper.Services.Interfaces;
using Xunit;

namespace Scrapper.Services.Tests;

public class HtmlFetcherTests
{
    private const string PublicTestUrl = "http://8.8.8.8/page";

    private static HtmlFetcher CreateSut(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler);
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);

        return new HtmlFetcher(factoryMock.Object, Mock.Of<IBrowserProvider>(), NullLogger<HtmlFetcher>.Instance);
    }

    [Fact]
    public async Task FetchAsync_SuccessfulResponse_ReturnsHtml()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html><body>hello</body></html>")
        });
        var sut = CreateSut(handler);

        var html = await sut.FetchAsync(PublicTestUrl, 10);

        html.Should().Contain("hello");
    }

    [Fact]
    public async Task FetchAsync_NotFound_ThrowsScrapeException()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var sut = CreateSut(handler);

        var act = async () => await sut.FetchAsync(PublicTestUrl, 10);

        await act.Should().ThrowAsync<ScrapeException>();
    }

    [Fact]
    public async Task FetchAsync_ServerError_ThrowsScrapeException()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var sut = CreateSut(handler);

        var act = async () => await sut.FetchAsync(PublicTestUrl, 10);

        await act.Should().ThrowAsync<ScrapeException>();
    }

    [Fact]
    public async Task FetchAsync_Timeout_ThrowsScrapeException()
    {
        var handler = new DelayedHttpMessageHandler(TimeSpan.FromSeconds(5));
        var sut = CreateSut(handler);

        var act = async () => await sut.FetchAsync(PublicTestUrl, 1);

        await act.Should().ThrowAsync<ScrapeException>();
    }

    [Fact]
    public async Task FetchAsync_OversizedResponse_ThrowsScrapeException()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(new string('a', 200)),
            Headers = { }
        });
        var sut = CreateSut(new FakeHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(new string('a', 200))
            };
            response.Content.Headers.ContentLength = long.MaxValue / 2;
            return response;
        }));

        var act = async () => await sut.FetchAsync(PublicTestUrl, 10);

        await act.Should().ThrowAsync<ScrapeException>();
    }

    [Fact]
    public async Task FetchAsync_MalformedUrl_ThrowsScrapeException()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var sut = CreateSut(handler);

        var act = async () => await sut.FetchAsync("not-a-url", 10);

        await act.Should().ThrowAsync<ScrapeException>();
    }

    [Fact]
    public async Task FetchAsync_PrivateIpLiteral_ThrowsScrapeException()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var sut = CreateSut(handler);

        var act = async () => await sut.FetchAsync("http://192.168.1.10/", 10);

        await act.Should().ThrowAsync<ScrapeException>();
    }

    [Fact]
    public async Task FetchAsync_Redirect_FollowsToFinalLocation()
    {
        var callCount = 0;
        var handler = new FakeHttpMessageHandler(request =>
        {
            callCount++;
            if (callCount == 1)
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                redirect.Headers.Location = new Uri("http://8.8.8.8/final");
                return redirect;
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("final content") };
        });
        var sut = CreateSut(handler);

        var html = await sut.FetchAsync(PublicTestUrl, 10);

        html.Should().Be("final content");
        callCount.Should().Be(2);
    }
}
