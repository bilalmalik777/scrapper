namespace Scrapper.Utils.Helpers;

/// <summary>
/// Builds the URL for a specific page number of a paginated listing by setting (adding or
/// replacing) a "page" query-string parameter — how this site's, and most listing sites',
/// pagination works. Building page URLs directly (rather than following "next page" links one
/// hop at a time) is what makes jumping straight to an arbitrary resume page possible.
/// </summary>
public static class PagedUrlBuilder
{
    public static string BuildPageUrl(string baseUrl, int pageNumber)
    {
        var uri = new Uri(baseUrl, UriKind.Absolute);
        var query = uri.Query.TrimStart('?');
        var parts = string.IsNullOrEmpty(query)
            ? []
            : query.Split('&', StringSplitOptions.RemoveEmptyEntries).ToList();

        var kept = parts.Where(p => !p.StartsWith("page=", StringComparison.OrdinalIgnoreCase)).ToList();
        kept.Add($"page={pageNumber}");

        var builder = new UriBuilder(uri) { Query = string.Join('&', kept) };
        return builder.Uri.ToString();
    }
}
