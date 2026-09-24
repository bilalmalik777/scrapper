using Microsoft.Playwright;
using Scrapper.Services.Interfaces;

namespace Scrapper.Services.Implementations;

/// <summary>
/// Lazily launches one shared headless Chromium instance on first use and reuses it for every
/// subsequent JavaScript-rendering fetch, relaunching only if it disconnects. Registered as a
/// singleton — the DI container disposes it (closing the browser) on application shutdown.
/// </summary>
public sealed class PlaywrightBrowserProvider : IBrowserProvider
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public async Task<IBrowser> GetBrowserAsync(CancellationToken cancellationToken)
    {
        if (_browser is { IsConnected: true })
        {
            return _browser;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_browser is { IsConnected: true })
            {
                return _browser;
            }

            _playwright ??= await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
            });

            return _browser;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.CloseAsync();
        }

        _playwright?.Dispose();
        _lock.Dispose();
    }
}
