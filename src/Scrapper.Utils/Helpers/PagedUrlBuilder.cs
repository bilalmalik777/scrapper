using System.Text.RegularExpressions;

namespace Scrapper.Utils.Helpers;

/// <summary>
/// Builds the URL for a specific page number of a paginated listing. Building page URLs
/// directly (rather than following "next page" links one hop at a time) is what makes jumping
/// straight to an arbitrary resume page possible. Two common schemes are supported: a page
/// number in the URL path (".../specialists/page-6", ".../list/page/6") — detected from the
/// base URL itself — and otherwise a "page" query-string parameter, added or replaced.
/// </summary>
public static partial class PagedUrlBuilder
{
    public static string BuildPageUrl(string baseUrl, int pageNumber)
    {
        var uri = new Uri(baseUrl, UriKind.Absolute);

        // A path-style page segment ("page-1", "page/1") must be rewritten in the path: a
        // "?page=N" query parameter is silently ignored by such sites, so every "page" would
        // come back as the same first page.
        var path = uri.AbsolutePath;
        var rewritten = PathPageDashRegex().Replace(path, $"${{prefix}}{pageNumber}");
        if (rewritten == path)
        {
            rewritten = PathPageSlashRegex().Replace(path, $"${{prefix}}{pageNumber}");
        }

        if (rewritten != path)
        {
            return new UriBuilder(uri) { Path = rewritten }.Uri.ToString();
        }

        var query = uri.Query.TrimStart('?');
        var parts = string.IsNullOrEmpty(query)
            ? []
            : query.Split('&', StringSplitOptions.RemoveEmptyEntries).ToList();

        var kept = parts.Where(p => !p.StartsWith("page=", StringComparison.OrdinalIgnoreCase)).ToList();
        kept.Add($"page={pageNumber}");

        var builder = new UriBuilder(uri) { Query = string.Join('&', kept) };
        return builder.Uri.ToString();
    }

    [GeneratedRegex(@"(?<prefix>/page-)\d+(?=/|$)", RegexOptions.IgnoreCase)]
    private static partial Regex PathPageDashRegex();

    [GeneratedRegex(@"(?<prefix>/page/)\d+(?=/|$)", RegexOptions.IgnoreCase)]
    private static partial Regex PathPageSlashRegex();
}
