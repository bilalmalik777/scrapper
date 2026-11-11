using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Scrapper.Services.Interfaces;

namespace Scrapper.Services.Implementations;

/// <summary>
/// Lazily launches one shared headless Chromium instance on first use and reuses it for every
/// subsequent JavaScript-rendering fetch, relaunching only if it disconnects. Registered as a
/// singleton — the DI container disposes it (closing the browser) on application shutdown.
/// </summary>
public sealed partial class PlaywrightBrowserProvider(ILogger<PlaywrightBrowserProvider>? logger = null) : IBrowserProvider
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
            try
            {
                _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = true,
                });
            }
            catch (PlaywrightException ex) when (ex.Message.Contains("Executable doesn't exist", StringComparison.OrdinalIgnoreCase))
            {
                // The browser check happens inside Playwright's own driver process, so "doesn't
                // exist" can mean the file is genuinely missing OR that this particular process
                // (e.g. one launched from Visual Studio rather than `dotnet run`) runs under a
                // different user/environment than the one the browser was installed for. Report
                // what this process actually sees so the two cases can be told apart.
                var diagnostics = Describe(ex.Message);
                logger?.LogError(ex, "Playwright browser launch failed. {Diagnostics}", diagnostics);
                var firstLine = ex.Message.Split('\n')[0].Trim();
                throw new InvalidOperationException($"{firstLine} [{diagnostics}]", ex);
            }

            return _browser;
        }
        finally
        {
            _lock.Release();
        }
    }

    private static string Describe(string message)
    {
        var path = ExecutablePathRegex().Match(message) is { Success: true } m ? m.Groups[1].Value : null;
        var bytes = path is not null && File.Exists(path) ? new FileInfo(path).Length : -1;
        return $"process user={Environment.UserDomainName}\\{Environment.UserName}, pid={Environment.ProcessId}, "
            + $"LOCALAPPDATA={Environment.GetEnvironmentVariable("LOCALAPPDATA")}, "
            + $"PLAYWRIGHT_BROWSERS_PATH={Environment.GetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH") ?? "(unset)"}, "
            + $"file visible to .NET={(bytes >= 0 ? $"yes ({bytes} bytes)" : "no")}";
    }

    [GeneratedRegex(@"Executable doesn't exist at (.+?headless_shell\.exe|.+?chrome\.exe)")]
    private static partial Regex ExecutablePathRegex();

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
