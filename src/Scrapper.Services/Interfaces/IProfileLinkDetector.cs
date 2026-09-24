using HtmlAgilityPack;

namespace Scrapper.Services.Interfaces;

public interface IProfileLinkDetector
{
    /// <summary>
    /// Finds the link within a listing record that points to that record's own detail/profile
    /// page (e.g. a specialist card's link to their individual profile), without any
    /// site-specific knowledge. Returns null when no such link can be identified. Only
    /// same-host links are considered — this is also where the "same domain by default"
    /// crawl restriction is enforced, not just at fetch time.
    /// </summary>
    string? DetectProfileUrl(HtmlNode recordNode, Uri pageUri);

    /// <summary>
    /// Finds a "next page" link on a listing page (rel="next", or common pagination link
    /// text/aria-labels). Returns null when no further page is found — "Load more" buttons
    /// that require JavaScript execution (rather than a real link) are not supported.
    /// </summary>
    string? DetectNextPageUrl(HtmlNode documentNode, Uri pageUri);
}
