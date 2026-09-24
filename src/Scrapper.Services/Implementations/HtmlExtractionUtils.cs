using System.Text.RegularExpressions;
using Fizzler.Systems.HtmlAgilityPack;
using HtmlAgilityPack;
using Scrapper.Models.Enums;

namespace Scrapper.Services.Implementations;

/// <summary>
/// Shared low-level DOM selection/extraction helpers used by both the explicit-selector
/// path (<see cref="FieldExtractor"/>) and the automatic multi-strategy extraction engine,
/// so CSS/XPath handling lives in exactly one place.
/// </summary>
internal static class HtmlExtractionUtils
{
    public static List<HtmlNode> SelectNodes(HtmlNode scope, string? selector, SelectorType selectorType)
    {
        if (string.IsNullOrWhiteSpace(selector))
        {
            return [];
        }

        try
        {
            if (selectorType == SelectorType.XPath)
            {
                var nodes = scope.SelectNodes(selector);
                return nodes?.ToList() ?? [];
            }

            return scope.QuerySelectorAll(selector).ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }

    public static string? ExtractValue(HtmlNode node, ExtractionType extractionType, string? attribute)
    {
        return extractionType switch
        {
            ExtractionType.Text => CleanText(node.InnerText),
            ExtractionType.Html => node.InnerHtml.Trim(),
            ExtractionType.Attribute => attribute is null ? null : node.GetAttributeValue(attribute, null),
            ExtractionType.Link => node.GetAttributeValue("href", null) ?? node.GetAttributeValue("src", null),
            _ => CleanText(node.InnerText)
        };
    }

    private static readonly Regex HorizontalWhitespaceRunPattern = new(@"[ \t]+", RegexOptions.Compiled);

    // Collapses repeated literal spaces/tabs within a line (a common CMS/source-formatting
    // artifact — e.g. a postcode ending up as "KT10  9NP" with a double space, which a regex
    // expecting exactly one whitespace character between the two halves then fails to match at
    // all) down to one space, the way a browser renders them. Deliberately leaves actual
    // newlines alone rather than collapsing all whitespace uniformly: several field-value
    // patterns (see FieldSynonymCatalog's postcode-anchored Location fallback) rely on a raw
    // '\n' as a rough proxy for "this is a different block of content" to stop a lazy match
    // from reaching across unrelated page content — flattening newlines into spaces here would
    // silently let those patterns span far more of the page than intended.
    public static string CleanText(string text) =>
        HorizontalWhitespaceRunPattern.Replace(System.Net.WebUtility.HtmlDecode(text), " ").Trim();

    private static readonly HashSet<string> NonVisibleTextTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "noscript", "template"
    };

    /// <summary>
    /// The node's text content a human would actually see on the page — unlike
    /// <see cref="HtmlNode.InnerText"/>, this excludes &lt;script&gt;/&lt;style&gt; content.
    /// A page's embedded JSON state blob (Next.js/Apollo/etc.) lives in a &lt;script&gt; tag
    /// and can be hundreds of KB of arbitrary numbers and strings; treating it as visible
    /// text would let the automatic strategies "find" a value that's really just
    /// coincidental noise from unrelated application state. Used by every automatic
    /// (no-selector) strategy; the explicit-selector path still uses raw InnerText, since
    /// there the user picked that exact node deliberately.
    /// </summary>
    public static string GetVisibleText(HtmlNode node)
    {
        if (NonVisibleTextTags.Contains(node.Name))
        {
            return string.Empty;
        }

        var textNodes = node.SelectNodes(".//text()");
        if (textNodes is null)
        {
            return node.NodeType == HtmlNodeType.Text ? CleanText(node.InnerText) : string.Empty;
        }

        var relevant = textNodes.Where(t => t.ParentNode is null || !NonVisibleTextTags.Contains(t.ParentNode.Name));
        return CleanText(string.Join(' ', relevant.Select(t => t.InnerText)));
    }

    private static readonly string[] BlockTags = ["p", "div", "li", "br", "h1", "h2", "h3", "h4", "h5", "h6"];

    /// <summary>
    /// Like <see cref="GetVisibleText"/>, but joins each block-level child (paragraphs, list
    /// items, ...) with a blank line instead of a single space — used for long-form content
    /// (a biography section) where the paragraph structure is meaningful and shouldn't be
    /// collapsed into one run-on line.
    /// </summary>
    public static string GetVisibleParagraphText(HtmlNode node)
    {
        var paragraphs = node.Descendants()
            .Where(d => BlockTags.Contains(d.Name))
            .Select(GetVisibleText)
            .Where(t => t.Length > 0)
            .Distinct()
            .ToList();

        if (paragraphs.Count == 0)
        {
            return GetVisibleText(node);
        }

        // Remove paragraphs that are fully contained within another (longer) one already
        // captured — otherwise a wrapping <div> and its inner <p> would both be included.
        var deduped = paragraphs
            .Where(p => !paragraphs.Any(other => other.Length > p.Length && other.Contains(p)))
            .ToList();

        return string.Join("\n\n", deduped);
    }

    /// <summary>
    /// Does this element's class/id/data-testid/aria-label attributes indicate it's the
    /// element for one of these tokens (a field's own name, or its kind's synonyms)?
    /// Shared by <see cref="Scrapper.Services.Implementations.RecordDetector"/> and
    /// <see cref="Strategies.SemanticHtmlExtractionStrategy"/> so the matching rules — and
    /// their false-positive guards — live in exactly one place.
    /// </summary>
    public static bool MatchesAnyToken(string haystack, IReadOnlyList<string> tokens)
    {
        if (string.IsNullOrWhiteSpace(haystack))
        {
            return false;
        }

        var haystackWords = haystack.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        foreach (var token in tokens)
        {
            if (token.Length <= 5)
            {
                // Short/generic tokens (e.g. "name") are too collision-prone for raw
                // substring matching — they turn up inside unrelated compound identifiers
                // constantly (a "keyword-name" label, a "class-name" attribute, ...).
                // Require the token to appear as its own whole word instead.
                if (haystackWords.Contains(token))
                {
                    return true;
                }
            }
            else if (haystack.Contains(token))
            {
                return true;
            }
        }

        // Word-level fallback for multi-word tokens — e.g. a field named "Treats Age
        // Ranges" against a testid "about-age-ranges" (normalized "about age ranges"):
        // neither string contains the other in full, but they share "ranges". Words under
        // 4 chars are excluded so generic terms ("the", "and") don't cause noise.
        foreach (var token in tokens)
        {
            var tokenWords = token.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 4);
            if (tokenWords.Any(haystackWords.Contains))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The class/id/data-testid/aria-label of a node, normalized for token matching.</summary>
    public static string BuildAttributeSignature(HtmlNode node)
    {
        var parts = new[]
        {
            node.GetAttributeValue("class", string.Empty),
            node.GetAttributeValue("id", string.Empty),
            node.GetAttributeValue("data-testid", string.Empty),
            node.GetAttributeValue("aria-label", string.Empty),
        };

        return string.Join(' ', parts).Replace('-', ' ').Replace('_', ' ').ToLowerInvariant();
    }
}
