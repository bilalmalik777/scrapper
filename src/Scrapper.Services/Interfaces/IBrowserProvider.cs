using Microsoft.Playwright;

namespace Scrapper.Services.Interfaces;

/// <summary>
/// Owns a single shared headless-browser instance for the lifetime of the app (launching one
/// per request would be far too slow/heavy) — used only by the opt-in JavaScript-rendering
/// fetch path, never by the default plain-HTTP fetch.
/// </summary>
public interface IBrowserProvider : IAsyncDisposable
{
    Task<IBrowser> GetBrowserAsync(CancellationToken cancellationToken);
}
