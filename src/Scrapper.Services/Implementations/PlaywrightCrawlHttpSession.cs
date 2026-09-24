using Microsoft.Playwright;
using Scrapper.Services.Interfaces;
using Scrapper.Utils.Constants;
using Scrapper.Utils.Helpers;

namespace Scrapper.Services.Implementations;

/// <summary>
/// A JS-rendering counterpart to <see cref="CrawlHttpSession"/> — for sites whose bot
/// protection blocks a plain HTTP client regardless of headers (a TLS/connection-level
/// fingerprint check, not just User-Agent), where only a real browser engine gets through. One
/// browser context (and one page, navigated repeatedly — exactly how a real browser tab moves
/// between pages of the same site) is kept for the whole crawl job, giving the same "one
/// persistent session" guarantee <see cref="ICrawlHttpSession"/> promises for the plain-HTTP
/// path. Classifies responses the same way (block/challenge/empty detection); never attempts to
/// defeat any of them.
/// </summary>
public class PlaywrightCrawlHttpSession(IBrowserProvider browserProvider, string? userAgent, int timeoutSeconds) : ICrawlHttpSession
{
    private IBrowserContext? _context;
    private IPage? _page;

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

        var timeoutMs = Math.Clamp(timeoutSeconds, ScrapingLimits.MinTimeoutSeconds, ScrapingLimits.MaxTimeoutSeconds) * 1000f;

        IPage page;
        try
        {
            page = await GetOrCreatePageAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            return new PageFetchResult(PageFetchOutcome.NetworkError, null, null, $"Unable to start the headless browser: {ex.Message}", null);
        }

        IResponse? response;
        try
        {
            response = await page.GotoAsync(uri.ToString(), new PageGotoOptions
            {
                Timeout = timeoutMs,
                WaitUntil = WaitUntilState.NetworkIdle,
                Referer = referer,
            });
        }
        catch (TimeoutException)
        {
            return new PageFetchResult(PageFetchOutcome.NetworkError, null, null, "The request timed out.", null);
        }
        catch (PlaywrightException ex)
        {
            return new PageFetchResult(PageFetchOutcome.NetworkError, null, null, ex.Message, null);
        }

        var statusCode = response?.Status;

        if (statusCode == 403)
        {
            return new PageFetchResult(PageFetchOutcome.Forbidden, statusCode, null, "The server responded 403 Forbidden.", null);
        }

        if (statusCode == 429)
        {
            return new PageFetchResult(PageFetchOutcome.RateLimited, statusCode, null, "The server responded 429 Too Many Requests.", null);
        }

        if (statusCode is 500 or 502 or 503)
        {
            return new PageFetchResult(PageFetchOutcome.ServerError, statusCode, null, $"The server responded {statusCode}.", null);
        }

        if (response is not null && !response.Ok)
        {
            return new PageFetchResult(PageFetchOutcome.ServerError, statusCode, null, $"The server responded {statusCode}.", null);
        }

        string html;
        try
        {
            html = await page.ContentAsync();
        }
        catch (PlaywrightException ex)
        {
            return new PageFetchResult(PageFetchOutcome.NetworkError, statusCode, null, ex.Message, null);
        }

        if (System.Text.Encoding.UTF8.GetByteCount(html) > ScrapingLimits.MaxResponseBytes)
        {
            return new PageFetchResult(PageFetchOutcome.NetworkError, statusCode, null, "The page response was too large to process.", null);
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

    private async Task<IPage> GetOrCreatePageAsync(CancellationToken cancellationToken)
    {
        if (_page is not null)
        {
            return _page;
        }

        var browser = await browserProvider.GetBrowserAsync(cancellationToken);
        _context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            UserAgent = string.IsNullOrWhiteSpace(userAgent)
                ? "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
                : userAgent,
            IgnoreHTTPSErrors = false,
        });

        await _context.RouteAsync("**/*", async route =>
        {
            var request = route.Request;
            if (!request.IsNavigationRequest)
            {
                await route.ContinueAsync();
                return;
            }

            if (!UrlSecurityHelper.TryParseAllowedUrl(request.Url, out var navUri, out _)
                || await UrlSecurityHelper.ResolvesToBlockedAddressAsync(navUri!.Host, cancellationToken))
            {
                await route.AbortAsync();
                return;
            }

            await route.ContinueAsync();
        });

        _page = await _context.NewPageAsync();
        return _page;
    }

    private static string? DetectChallengeMarker(string html)
    {
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

    public void Dispose()
    {
        // IBrowserContext/IPage are only IAsyncDisposable — block briefly on the async
        // teardown rather than leaking the context for the process's remaining lifetime.
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private async ValueTask DisposeAsync()
    {
        if (_context is not null)
        {
            await _context.CloseAsync();
        }
    }
}
