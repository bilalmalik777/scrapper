using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Scrapper.Services.Interfaces;

namespace Scrapper.Services.Implementations;

/// <summary>
/// Identifies "this record links to its own detail page" and "this listing has another
/// page" without any site-specific selectors — purely from generic link shape/position
/// signals (does it wrap the record's heading? is its text pagination-like?).
/// </summary>
public partial class ProfileLinkDetector : IProfileLinkDetector
{
    public string? DetectProfileUrl(HtmlNode recordNode, Uri pageUri)
    {
        var candidates = recordNode.Descendants("a")
            .Select(a => new { Node = a, Href = a.GetAttributeValue("href", string.Empty) })
            .Where(x => IsCandidateHref(x.Href))
            .Select(x => new { x.Node, Resolved = ResolveUrl(x.Href, pageUri) })
            .Where(x => x.Resolved is not null && IsSameHost(x.Resolved, pageUri))
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        // Strongest signal: the anchor wraps (or is wrapped by) the record's own heading —
        // this is how most card-based listings link a name to its detail page.
        var headingLink = candidates.FirstOrDefault(c =>
            c.Node.Descendants().Any(d => d.Name is "h1" or "h2" or "h3" or "h4") || IsDescendantOfHeading(c.Node));
        if (headingLink is not null)
        {
            return headingLink.Resolved!.ToString();
        }

        // Otherwise, the first link with substantial, non-pagination-looking text.
        var substantial = candidates.FirstOrDefault(c =>
        {
            var text = HtmlExtractionUtils.GetVisibleText(c.Node).Trim();
            return text.Length >= 3 && !PaginationLikeText().IsMatch(text);
        });

        return substantial?.Resolved?.ToString();
    }

    public string? DetectNextPageUrl(HtmlNode documentNode, Uri pageUri)
    {
        var relNext = documentNode.SelectSingleNode("//a[@rel='next']")
                      ?? documentNode.SelectSingleNode("//link[@rel='next']");
        if (relNext is not null)
        {
            var href = relNext.GetAttributeValue("href", string.Empty);
            var resolved = ResolveUrl(href, pageUri);
            if (resolved is not null && IsSameHost(resolved, pageUri))
            {
                return resolved.ToString();
            }
        }

        var candidate = documentNode.Descendants("a")
            .Select(a => new
            {
                Node = a,
                Href = a.GetAttributeValue("href", string.Empty),
                Text = HtmlExtractionUtils.GetVisibleText(a).Trim(),
                AriaLabel = a.GetAttributeValue("aria-label", string.Empty),
            })
            .Where(x => IsCandidateHref(x.Href))
            .FirstOrDefault(x => NextPageText().IsMatch(x.Text) || NextPageText().IsMatch(x.AriaLabel));

        if (candidate is null)
        {
            return null;
        }

        var resolvedNext = ResolveUrl(candidate.Href, pageUri);
        if (resolvedNext is null || !IsSameHost(resolvedNext, pageUri))
        {
            return null;
        }

        // Guard against a "next" link that just points back at the current page.
        return string.Equals(resolvedNext.ToString(), pageUri.ToString(), StringComparison.OrdinalIgnoreCase)
            ? null
            : resolvedNext.ToString();
    }

    private static bool IsCandidateHref(string href) =>
        !string.IsNullOrWhiteSpace(href)
        && !href.StartsWith('#')
        && !href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)
        && !href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
        && !href.StartsWith("tel:", StringComparison.OrdinalIgnoreCase);

    private static Uri? ResolveUrl(string href, Uri baseUri)
    {
        try
        {
            return new Uri(baseUri, href);
        }
        catch (UriFormatException)
        {
            return null;
        }
    }

    private static bool IsSameHost(Uri a, Uri b) =>
        string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase);

    private static bool IsDescendantOfHeading(HtmlNode node)
    {
        var ancestor = node.ParentNode;
        while (ancestor is not null)
        {
            if (ancestor.Name is "h1" or "h2" or "h3" or "h4")
            {
                return true;
            }
            ancestor = ancestor.ParentNode;
        }
        return false;
    }

    [GeneratedRegex(@"^(next|prev(ious)?|first|last|»|«|›|‹|>>|<<|\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex PaginationLikeText();

    // Anchored at the end only (not the start) — a real "next" control's accessible text is
    // very often phrased as a full sentence with the meaningful part at the end (e.g. Doctify's
    // aria-label="Go to next page"), not just the bare word, so requiring the whole string to
    // equal "next page" misses those entirely.
    [GeneratedRegex(@"(next( page)?|»|›|>>|older)$", RegexOptions.IgnoreCase)]
    private static partial Regex NextPageText();
}
