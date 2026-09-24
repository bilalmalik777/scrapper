namespace Scrapper.Services.Interfaces;

public interface IHtmlFetcher
{
    Task<string> FetchAsync(string url, int timeoutSeconds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Like <see cref="FetchAsync"/>, but renders the page in a headless browser first (runs
    /// its JavaScript, waits for network activity to settle) before returning the resulting
    /// HTML. For sites whose content — a search-results widget, a "find a doctor" map, ...  —
    /// only exists after client-side JS runs, which a plain HTTP fetch can never see. Slower
    /// and heavier than <see cref="FetchAsync"/>, so it's opt-in per scrape
    /// (<c>ScraperConfigDto.EnableJavaScriptRendering</c>), not the default path.
    /// </summary>
    Task<string> FetchRenderedAsync(string url, int timeoutSeconds, CancellationToken cancellationToken = default);
}
