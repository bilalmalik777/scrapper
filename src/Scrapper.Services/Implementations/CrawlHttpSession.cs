using System.Net;
using Scrapper.Models.Common;
using Scrapper.Services.Interfaces;
using Scrapper.Utils.Constants;
using Scrapper.Utils.Helpers;

namespace Scrapper.Services.Implementations;

/// <summary>
/// One dedicated <see cref="HttpClient"/>/<see cref="CookieContainer"/> pair per crawl job,
/// created once and reused for every page — see <see cref="ICrawlHttpSession"/>. Deliberately
/// separate from <see cref="HtmlFetcher"/>'s pooled-per-request client: a paged crawl needs its
/// cookies to persist reliably across dozens of requests over many minutes, which
/// <c>IHttpClientFactory</c>'s handler-recycling (every ~2 minutes) doesn't guarantee.
/// </summary>
public class CrawlHttpSession : ICrawlHttpSession
{
    private readonly string? _userAgent;
    private readonly HttpMessageHandler? _ownedHandler;
    private readonly HttpClient _client;

    public CrawlHttpSession(string? userAgent, int timeoutSeconds)
        : this(userAgent, timeoutSeconds, ownedHandler: new HttpClientHandler
        {
            UseCookies = true,
            CookieContainer = new CookieContainer(),
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true,
        })
    {
    }

    /// <summary>Test seam — lets tests supply a fake handler instead of making real HTTP calls.</summary>
    internal CrawlHttpSession(string? userAgent, int timeoutSeconds, HttpMessageHandler ownedHandler)
    {
        _userAgent = userAgent;
        _ownedHandler = ownedHandler;
        _client = new HttpClient(ownedHandler)
        {
            Timeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, ScrapingLimits.MinTimeoutSeconds, ScrapingLimits.MaxTimeoutSeconds)),
        };
    }

    private HttpClient Client => _client;

    public async Task<PageFetchResult> FetchAsync(string url, string? referer, CancellationToken cancellationToken = default)
    {
        if (!UrlSecurityHelper.TryParseAllowedUrl(url, out var uri, out var reason))
        {
            return new PageFetchResult(PageFetchOutcome.UrlNotAllowed, null, null, reason, null);
        }

        if (await UrlSecurityHelper.ResolvesToBlockedAddressAsync(uri!.Host, cancellationToken))
        {
            return new PageFetchResult(PageFetchOutcome.UrlNotAllowed, null, null, "Access to internal or private network addresses is not allowed.", null);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd(string.IsNullOrWhiteSpace(_userAgent)
            ? "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
            : _userAgent);
        request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        request.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
        if (!string.IsNullOrWhiteSpace(referer))
        {
            request.Headers.Referrer = new Uri(referer);
        }

        HttpResponseMessage response;
        try
        {
            response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new PageFetchResult(PageFetchOutcome.NetworkError, null, null, "The request timed out.", null);
        }
        catch (HttpRequestException ex)
        {
            return new PageFetchResult(PageFetchOutcome.NetworkError, null, null, ex.Message, null);
        }

        using (response)
        {
            var statusCode = (int)response.StatusCode;

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                return new PageFetchResult(PageFetchOutcome.Forbidden, statusCode, null, "The server responded 403 Forbidden.", null);
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return new PageFetchResult(PageFetchOutcome.RateLimited, statusCode, null, "The server responded 429 Too Many Requests.", GetRetryAfter(response));
            }

            if (response.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable)
            {
                return new PageFetchResult(PageFetchOutcome.ServerError, statusCode, null, $"The server responded {statusCode}.", GetRetryAfter(response));
            }

            if (!response.IsSuccessStatusCode)
            {
                return new PageFetchResult(PageFetchOutcome.ServerError, statusCode, null, $"The server responded {statusCode}.", null);
            }

            string html;
            try
            {
                html = await HtmlFetcher.ReadBoundedContentAsync(response, cancellationToken);
            }
            catch (ScrapeException ex)
            {
                return new PageFetchResult(PageFetchOutcome.NetworkError, statusCode, null, ex.Message, null);
            }

            if (string.IsNullOrWhiteSpace(html))
            {
                return new PageFetchResult(PageFetchOutcome.EmptyResponse, statusCode, null, "The response body was empty.", null);
            }

            var challenge = DetectChallengeMarker(html);
            if (challenge is not null)
            {
                return new PageFetchResult(PageFetchOutcome.ChallengePage, statusCode, null, $"Response matched challenge marker \"{challenge}\".", null);
            }

            return new PageFetchResult(PageFetchOutcome.Success, statusCode, html, null, null);
        }
    }

    private static string? DetectChallengeMarker(string html)
    {
        // Only worth checking a bounded prefix — a challenge page's own text is always near
        // the top, and scanning the full (possibly multi-MB) body on every page adds up.
        var window = html.Length > 20_000 ? html[..20_000] : html;
        foreach (var marker in ScrapingLimits.ChallengePageMarkers)
        {
            if (window.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return marker;
            }
        }

        return null;
    }

    private static TimeSpan? GetRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
        {
            return delta;
        }

        if (retryAfter?.Date is { } date)
        {
            var untilRetry = date - DateTimeOffset.UtcNow;
            return untilRetry > TimeSpan.Zero ? untilRetry : null;
        }

        return null;
    }

    public void Dispose()
    {
        _client.Dispose();
        _ownedHandler?.Dispose();
        GC.SuppressFinalize(this);
    }
}

public class CrawlHttpSessionFactory : ICrawlHttpSessionFactory
{
    public ICrawlHttpSession Create(string? userAgent, int timeoutSeconds) => new CrawlHttpSession(userAgent, timeoutSeconds);
}
