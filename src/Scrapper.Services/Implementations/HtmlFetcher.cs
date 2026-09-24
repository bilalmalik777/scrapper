using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Scrapper.Models.Common;
using Scrapper.Services.Interfaces;
using Scrapper.Utils.Constants;
using Scrapper.Utils.Helpers;

namespace Scrapper.Services.Implementations;

public class HtmlFetcher(
    IHttpClientFactory httpClientFactory, IBrowserProvider browserProvider, ILogger<HtmlFetcher> logger) : IHtmlFetcher
{
    public async Task<string> FetchAsync(string url, int timeoutSeconds, CancellationToken cancellationToken = default)
    {
        var client = httpClientFactory.CreateClient(nameof(HtmlFetcher));
        var effectiveTimeout = Math.Clamp(timeoutSeconds, ScrapingLimits.MinTimeoutSeconds, ScrapingLimits.MaxTimeoutSeconds);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(effectiveTimeout));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        var currentUrl = url;

        try
        {
            for (var redirectCount = 0; redirectCount <= ScrapingLimits.MaxRedirects; redirectCount++)
            {
                if (!UrlSecurityHelper.TryParseAllowedUrl(currentUrl, out var uri, out var reason))
                {
                    throw new ScrapeException($"Unable to access the URL. {reason}");
                }

                if (await UrlSecurityHelper.ResolvesToBlockedAddressAsync(uri!.Host, linkedCts.Token))
                {
                    throw new SsrfViolationException("Access to internal or private network addresses is not allowed.");
                }

                var response = await SendWithRetryAsync(client, uri, linkedCts.Token);
                using (response)
                {
                    if (IsRedirect(response.StatusCode) && response.Headers.Location is not null)
                    {
                        currentUrl = new Uri(uri, response.Headers.Location).ToString();
                        continue;
                    }

                    if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        throw new RateLimitedException("The server responded with an error (HTTP 429).");
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        throw new ScrapeException($"The server responded with an error (HTTP {(int)response.StatusCode}).");
                    }

                    var contentLength = response.Content.Headers.ContentLength;
                    if (contentLength is > ScrapingLimits.MaxResponseBytes)
                    {
                        throw new ScrapeException("The page response was too large to process.");
                    }

                    return await ReadBoundedContentAsync(response, linkedCts.Token);
                }
            }

            throw new ScrapeException("Too many redirects were encountered while accessing the URL.");
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            throw new ScrapeException("The request to the URL timed out.");
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "HTTP error fetching {Url}", url);
            throw new ScrapeException("Unable to access the URL.");
        }
    }

    /// <summary>
    /// Renders the page in a shared headless Chromium instance before returning its HTML —
    /// for sites whose content only exists after client-side JavaScript runs (a search-results
    /// widget, a map-based "find a doctor" list, ...), which <see cref="FetchAsync"/> can never
    /// see. Applies the same URL/SSRF checks as the plain fetch: the initial URL up front, and
    /// every subsequent navigation (redirects, client-side route changes) via request
    /// interception, since a headless browser — unlike the manual redirect loop above — doesn't
    /// give the caller a natural per-hop checkpoint.
    /// </summary>
    public async Task<string> FetchRenderedAsync(string url, int timeoutSeconds, CancellationToken cancellationToken = default)
    {
        if (!UrlSecurityHelper.TryParseAllowedUrl(url, out var uri, out var reason))
        {
            throw new ScrapeException($"Unable to access the URL. {reason}");
        }

        if (await UrlSecurityHelper.ResolvesToBlockedAddressAsync(uri!.Host, cancellationToken))
        {
            throw new SsrfViolationException("Access to internal or private network addresses is not allowed.");
        }

        var effectiveTimeout = Math.Clamp(timeoutSeconds, ScrapingLimits.MinTimeoutSeconds, ScrapingLimits.MaxTimeoutSeconds);
        var timeoutMs = effectiveTimeout * 1000f;

        IBrowser browser;
        try
        {
            browser = await browserProvider.GetBrowserAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unable to start the headless browser for {Url}", url);
            throw new ScrapeException("Unable to render the page (the headless browser could not start).");
        }

        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
            IgnoreHTTPSErrors = false,
        });

        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(timeoutMs);

        await page.RouteAsync("**/*", async route =>
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

        try
        {
            var response = await page.GotoAsync(uri.ToString(), new PageGotoOptions
            {
                Timeout = timeoutMs,
                WaitUntil = WaitUntilState.NetworkIdle,
            });

            if (response is not null && !response.Ok)
            {
                throw new ScrapeException($"The server responded with an error (HTTP {response.Status}).");
            }

            var html = await page.ContentAsync();
            if (System.Text.Encoding.UTF8.GetByteCount(html) > ScrapingLimits.MaxResponseBytes)
            {
                throw new ScrapeException("The page response was too large to process.");
            }

            return html;
        }
        catch (TimeoutException)
        {
            throw new ScrapeException("The request to the URL timed out.");
        }
        catch (PlaywrightException ex)
        {
            logger.LogWarning(ex, "Browser rendering error fetching {Url}", url);
            throw new ScrapeException("Unable to render the page.");
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    /// <summary>
    /// Sends the request, backing off and retrying when the server responds 429 (Too Many
    /// Requests) — common once a crawl has made more than a handful of requests to the same
    /// site in a short window. Any other status (including a redirect or a different error)
    /// is returned immediately for the caller to handle as before.
    /// </summary>
    private static async Task<HttpResponseMessage> SendWithRetryAsync(HttpClient client, Uri uri, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (response.StatusCode != System.Net.HttpStatusCode.TooManyRequests || attempt >= ScrapingLimits.MaxTooManyRequestsRetries)
            {
                return response;
            }

            var delay = GetRetryDelay(response, attempt);
            response.Dispose();
            await Task.Delay(delay, cancellationToken);
        }
    }

    private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
        {
            return delta < TimeSpan.FromMilliseconds(ScrapingLimits.TooManyRequestsMaxDelayMs)
                ? delta
                : TimeSpan.FromMilliseconds(ScrapingLimits.TooManyRequestsMaxDelayMs);
        }

        if (retryAfter?.Date is { } retryDate)
        {
            var untilRetry = retryDate - DateTimeOffset.UtcNow;
            if (untilRetry > TimeSpan.Zero)
            {
                return untilRetry < TimeSpan.FromMilliseconds(ScrapingLimits.TooManyRequestsMaxDelayMs)
                    ? untilRetry
                    : TimeSpan.FromMilliseconds(ScrapingLimits.TooManyRequestsMaxDelayMs);
            }
        }

        var exponentialMs = ScrapingLimits.TooManyRequestsBaseDelayMs * Math.Pow(2, attempt);
        return TimeSpan.FromMilliseconds(Math.Min(exponentialMs, ScrapingLimits.TooManyRequestsMaxDelayMs));
    }

    private static bool IsRedirect(System.Net.HttpStatusCode statusCode) =>
        statusCode is System.Net.HttpStatusCode.Moved
            or System.Net.HttpStatusCode.Found
            or System.Net.HttpStatusCode.SeeOther
            or System.Net.HttpStatusCode.TemporaryRedirect
            or System.Net.HttpStatusCode.PermanentRedirect;

    internal static async Task<string> ReadBoundedContentAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var memoryStream = new MemoryStream();
        var buffer = new byte[81920];
        int bytesRead;
        var total = 0;

        while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += bytesRead;
            if (total > ScrapingLimits.MaxResponseBytes)
            {
                throw new ScrapeException("The page response was too large to process.");
            }

            await memoryStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
        }

        var encoding = response.Content.Headers.ContentType?.CharSet is { } charset && !string.IsNullOrWhiteSpace(charset)
            ? TryGetEncoding(charset)
            : System.Text.Encoding.UTF8;

        return encoding.GetString(memoryStream.ToArray());
    }

    private static System.Text.Encoding TryGetEncoding(string charset)
    {
        try
        {
            return System.Text.Encoding.GetEncoding(charset);
        }
        catch (ArgumentException)
        {
            return System.Text.Encoding.UTF8;
        }
    }
}
