using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Scrapper.Models.DTOs;
using Scrapper.Models.Enums;
using Scrapper.Services.Interfaces;
using Scrapper.Services.Models;
using Scrapper.Utils.Constants;

namespace Scrapper.Services.Implementations.Strategies;

/// <summary>
/// Priority 3: reads the record's HTML structure — headings, labelled elements
/// (class/id/data-testid/aria-label containing a field synonym), definition lists,
/// and "Label: Value" text patterns — without needing a selector from the user.
/// </summary>
public partial class SemanticHtmlExtractionStrategy : IFieldExtractionStrategy
{
    private static readonly string[] HeadingTags = ["h1", "h2", "h3", "h4"];

    public ExtractionSource Source => ExtractionSource.SemanticHtml;

    public Task<ExtractionCandidate?> TryExtractAsync(
        ExtractionContext context, FieldDefinitionDto field, FieldKind resolvedKind, CancellationToken cancellationToken)
    {
        var tokens = GetTokens(field.Name, resolvedKind);
        var record = context.RecordNode;

        if (resolvedKind == FieldKind.Name)
        {
            // A heading that's a question ("What is X's specialty?") is never a person/entity
            // name — skip past it rather than returning it, e.g. if an FAQ item's own heading
            // happened to be the first heading on a whole-page record. Likewise a heading that
            // sits inside a cookie-consent/GDPR banner: some consent widgets (rendered via a
            // headless-browser fetch, since they're pure client-side JS) inject a *genuine*
            // heading of their own ("This site uses cookies...") as real, visible DOM content
            // positioned before the page's actual content — on a single-record whole-page
            // record, that banner heading is otherwise indistinguishable from a real one.
            var heading = record.Descendants()
                .FirstOrDefault(n => HeadingTags.Contains(n.Name)
                    && !HtmlExtractionUtils.GetVisibleText(n).TrimEnd().EndsWith('?')
                    && !IsCookieConsentHeading(n));
            if (heading is not null)
            {
                var headingText = HtmlExtractionUtils.GetVisibleText(heading);
                // A short, all-caps, space-free heading (e.g. "AB") is an avatar-initials
                // placeholder some directory cards show in place of a photo — not a real
                // name — so it's skipped in favour of the name-shaped-text fallback below,
                // rather than returned as-is. A genuine heading always has more to it than
                // this (lowercase letters, a space, a title, ...), so this never affects a
                // real name.
                if (!string.IsNullOrWhiteSpace(headingText) && !InitialsPlaceholderRegex().IsMatch(headingText))
                {
                    return Result(headingText, 85);
                }
            }

            // Some directory cards show the person's name as plain text next to (or instead
            // of) a heading — e.g. "Alan Buckley — Psychotherapist" in a <span> — rather than
            // wrapping it in a heading tag itself. Only reached when the heading check above
            // didn't already return, so this never changes behaviour for the (far more common)
            // sites where the name genuinely is the heading.
            var nameLike = record.Descendants()
                .Where(n => n.NodeType == HtmlNodeType.Element && !HeadingTags.Contains(n.Name))
                .Select(n => HtmlExtractionUtils.GetVisibleText(n))
                .Where(t => t.Length is > 0 and <= 200)
                .Select(t => NameLikePrefixRegex().Match(t))
                .FirstOrDefault(m => m.Success);
            if (nameLike is not null)
            {
                return Result(nameLike.Groups["name"].Value.Trim(), 75);
            }
        }

        if (resolvedKind is FieldKind.Biography or FieldKind.Text)
        {
            // Long-form / list-style content (a bio, but equally "Qualifications",
            // "Insurers", "Languages spoken" — sections that are a heading plus a list or
            // paragraph, not a single short value). Two different real shapes both need
            // handling here: (a) one labelled container wrapping the *entire* list (e.g.
            // "Qualifications" as one <ul>) — the richest single match is already the whole
            // thing; (b) several separate labelled cards, one per item (e.g. "Expertise" as
            // N sibling "area-of-expertise-item" divs, each with its own keyword) — every
            // leaf match has to be collected and joined, or all but one item is silently
            // dropped. A leaf-level match (no matching descendant of its own) covers both:
            // a single wrapping <ul> has no matching descendant so it's the one leaf; N
            // sibling cards are N leaves.
            var maxLength = resolvedKind == FieldKind.Biography ? 8000 : 3000;
            var leafMatches = record.Descendants()
                .Where(n => n.NodeType == HtmlNodeType.Element
                    && MatchesAnyToken(BuildAttributeSignature(n), tokens)
                    && !n.Descendants().Any(d => d.NodeType == HtmlNodeType.Element && MatchesAnyToken(BuildAttributeSignature(d), tokens)))
                .Select(n => CleanListItemText(HtmlExtractionUtils.GetVisibleParagraphText(n)))
                .Where(t => t.Length >= 2 && t.Length <= maxLength)
                .Distinct()
                .ToList();

            if (leafMatches.Count == 1)
            {
                return Result(leafMatches[0], 80);
            }

            if (leafMatches.Count > 1)
            {
                var joined = string.Join("; ", leafMatches.Take(20));
                if (joined.Length <= maxLength)
                {
                    return Result(joined, 80);
                }
            }
        }
        else if (resolvedKind == FieldKind.Currency)
        {
            // A specialist can charge several different fees (e.g. "New appointment £425",
            // "Follow-up £275") shown as separate labelled elements — collect every distinct
            // leaf-level match rather than only the shortest, so none of them are dropped.
            var candidates = record.Descendants()
                .Where(n => n.NodeType == HtmlNodeType.Element
                    && !n.Descendants().Any(d => d.NodeType == HtmlNodeType.Element && MatchesAnyToken(BuildAttributeSignature(d), tokens))
                    && MatchesAnyToken(BuildAttributeSignature(n), tokens))
                .Select(n => HtmlExtractionUtils.GetVisibleText(n))
                .Where(t => t.Length is > 0 and <= 200)
                .Distinct()
                .ToList();

            if (candidates.Count > 0)
            {
                return Result(string.Join("; ", candidates), 80);
            }
        }
        else
        {
            // Prefer the most specific (shortest-text) matching element rather than the first
            // one encountered in document order — a large ancestor wrapper can coincidentally
            // match a token too (e.g. a "rate-paper-container" rating widget matching a "rate"
            // synonym), and picking that would swallow unrelated content as the "value".
            var labelled = record.Descendants()
                .Where(n => n.NodeType == HtmlNodeType.Element && MatchesAnyToken(BuildAttributeSignature(n), tokens))
                .Select(n => (Node: n, Text: HtmlExtractionUtils.GetVisibleText(n)))
                .Where(x => x.Text.Length is > 0 and <= 200)
                // Some sites reuse one generic class (e.g. "directory-section__address") for a
                // whole block of sibling contact-detail rows regardless of what each one
                // actually is — an address row, a phone row, a website row can all share it.
                // For Location specifically, a candidate that's clearly phone-shaped is never
                // the right pick even though its class matched, so it's excluded up front
                // rather than being able to win purely for having the shortest text.
                .Where(x => resolvedKind != FieldKind.Location
                    || !FieldSynonymCatalog.Patterns[FieldKind.Phone].Any(r => r.IsMatch(x.Text)))
                .OrderBy(x => x.Text.Length)
                .FirstOrDefault();
            if (labelled.Node is not null)
            {
                var value = resolvedKind == FieldKind.Url
                    ? labelled.Node.GetAttributeValue("href", null) ?? labelled.Text
                    : labelled.Text;

                if (!string.IsNullOrWhiteSpace(value))
                {
                    return Result(value, 80);
                }
            }
        }

        var dt = record.Descendants("dt")
            .FirstOrDefault(n => MatchesAnyToken(HtmlExtractionUtils.GetVisibleText(n).ToLowerInvariant(), tokens));
        var dd = dt?.NextSiblingElement("dd") ?? dt?.SelectSingleNode("following-sibling::dd[1]");
        if (dd is not null)
        {
            var value = HtmlExtractionUtils.GetVisibleText(dd);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return Result(value, 82);
            }
        }

        // Many "card" sections render as a bare heading (no distinguishing class/testid —
        // e.g. "<div><svg/><h3>Subspecialties</h3></div><ul>...values...</ul>") whose exact
        // text IS the field's label, with the actual value living in a sibling of either the
        // heading itself or its immediate wrapper (the icon+heading are often grouped in
        // their own wrapper div, so the value sits one level up from a plain dt/dd sibling).
        var headingLabel = record.Descendants()
            .FirstOrDefault(n => (HeadingTags.Contains(n.Name) || n.Name is "dt" or "strong" or "b")
                && MatchesAnyToken(HtmlExtractionUtils.GetVisibleText(n).Trim().ToLowerInvariant(), tokens));
        if (headingLabel is not null)
        {
            var maxLength = resolvedKind switch
            {
                FieldKind.Biography => 8000,
                FieldKind.Text => 3000,
                _ => 200,
            };

            foreach (var container in new[] { headingLabel.NextSiblingElementAny(), headingLabel.ParentNode?.NextSiblingElementAny() })
            {
                if (container is null)
                {
                    continue;
                }

                var value = HtmlExtractionUtils.GetVisibleParagraphText(container);
                if (value.Length is >= 1 && value.Length <= maxLength)
                {
                    return Result(value, 78);
                }
            }
        }

        var recordText = HtmlExtractionUtils.GetVisibleText(record);
        foreach (var line in recordText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = LabelValueRegex().Match(line);
            if (match.Success && MatchesAnyToken(match.Groups["label"].Value.ToLowerInvariant(), tokens))
            {
                var value = match.Groups["value"].Value.Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return Result(value, 65);
                }
            }
        }

        if (resolvedKind == FieldKind.Name)
        {
            var link = record.Descendants("a")
                .Select(a => HtmlExtractionUtils.GetVisibleText(a))
                .FirstOrDefault(t => t.Length is >= 3 and <= 150);
            if (link is not null)
            {
                return Result(link, 55);
            }
        }

        // "Role"/"Job Title"/"Specialty"/... has no matching class, testid, or label text
        // anywhere on many profile pages — it's shown purely by visual position, as a short
        // line immediately under the person's name (e.g. "Dr Ajai Seth" / "Sport & Exercise
        // Medicine Doctor"). When nothing else has matched and the field is one of these
        // name-driven aliases, fall back to the text right after the first heading.
        if (FieldSynonymCatalog.RoleStructuredDataPaths.ContainsKey(field.Name.Trim()))
        {
            var nameHeading = record.Descendants().FirstOrDefault(n => HeadingTags.Contains(n.Name));
            var subtitle = nameHeading?.NextSiblingElementAny() ?? nameHeading?.ParentNode?.NextSiblingElementAny();
            var subtitleText = subtitle is null ? null : HtmlExtractionUtils.GetVisibleText(subtitle);
            if (!string.IsNullOrWhiteSpace(subtitleText) && subtitleText.Length <= 200)
            {
                return Result(subtitleText, 55);
            }
        }

        return Task.FromResult<ExtractionCandidate?>(null);
    }

    private Task<ExtractionCandidate?> Result(string value, int confidence) =>
        Task.FromResult<ExtractionCandidate?>(new ExtractionCandidate(value, confidence, Source));

    /// <summary>
    /// A labelled "item" card often carries UI chrome alongside its real text — a review
    /// count, an endorsement badge — rendered as their own block-level element, so
    /// GetVisibleParagraphText picks them up as extra lines (e.g. "Chronic Fatigue Syndrome
    /// (Myalgic Encephalomyelitis)" + "1" + "1"). A line that's nothing but digits is never
    /// itself meaningful list content, so it's dropped rather than glued onto the real value.
    /// </summary>
    private static string CleanListItemText(string text)
    {
        var lines = text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !BareNumberRegex().IsMatch(line))
            .ToArray();

        return string.Join('\n', lines);
    }

    [GeneratedRegex(@"^[\d,.\s]+$")]
    private static partial Regex BareNumberRegex();

    private static string[] GetTokens(string fieldName, FieldKind kind)
    {
        var tokens = new List<string> { fieldName.Trim().ToLowerInvariant() };

        // FieldKind.Number is a grab-bag of otherwise-unrelated concepts (review counts,
        // endorsement counts, years of experience, ...) — unlike Name/Location/Currency's
        // synonym buckets, its entries aren't interchangeable descriptions of the same
        // thing, so treating any of them as evidence for *this* field would match e.g. an
        // "endorsements" counter for a field named "Experience". Only the field's own name
        // is trustworthy here.
        if (kind != FieldKind.Number && FieldSynonymCatalog.Synonyms.TryGetValue(kind, out var synonyms))
        {
            tokens.AddRange(synonyms.Select(s => s.ToLowerInvariant()));
        }

        return tokens.Where(t => t.Length > 0).Distinct().ToArray();
    }

    private static bool MatchesAnyToken(string haystack, string[] tokens) =>
        HtmlExtractionUtils.MatchesAnyToken(haystack, tokens);

    private static string BuildAttributeSignature(HtmlNode node) =>
        HtmlExtractionUtils.BuildAttributeSignature(node);

    // Consent-widget vendors abbreviate their own class/id names inconsistently (CivicComputing's
    // "CookieControl" widget uses ids like "ccc-title", "ccc-panel" — no literal "cookie"
    // substring at all), so an ancestor-class check can't reliably catch this. The banner's own
    // *wording* is far more consistent across vendors: virtually every cookie-consent heading
    // opens with some variant of "This site/website uses cookies" or "We use cookies" — and
    // essentially no genuine person/entity name heading would ever contain the word "cookies".
    [GeneratedRegex(@"^(this (site|website)|we)\s+use[sd]?\s+cookies", RegexOptions.IgnoreCase)]
    private static partial Regex CookieConsentHeadingRegex();

    // The banner's opening heading is only the first of several — consent widgets commonly
    // list each cookie category under its own sub-heading ("Necessary Cookies", "Analytics",
    // ...) inside the same panel. These are the common ones across vendors; none of them is
    // ever a plausible person/entity name.
    private static readonly HashSet<string> CookieCategoryHeadings = new(StringComparer.OrdinalIgnoreCase)
    {
        "necessary cookies", "strictly necessary cookies", "essential cookies", "analytics",
        "analytics cookies", "performance cookies", "functional cookies", "marketing cookies",
        "targeting cookies", "advertising cookies", "preference cookies", "social media cookies",
        "optional cookies", "cookie preferences", "manage cookies", "manage cookie preferences",
        "your privacy choices",
    };

    private static bool IsCookieConsentHeading(HtmlNode node)
    {
        var text = HtmlExtractionUtils.GetVisibleText(node).Trim();
        return CookieConsentHeadingRegex().IsMatch(text) || CookieCategoryHeadings.Contains(text);
    }

    [GeneratedRegex(@"^(?<label>[A-Za-z][A-Za-z \-]{1,40}?)\s*[:\-]\s*(?<value>.+)$")]
    private static partial Regex LabelValueRegex();

    // "AB", "A", "MB" — bare initials with no lowercase, digits, or spaces. A real name or
    // title heading always has at least one of those (e.g. "Dr Smith", "MBBS").
    [GeneratedRegex(@"^[A-Z]{1,4}$")]
    private static partial Regex InitialsPlaceholderRegex();

    // A leading run of 2-4 capitalised words (allowing single-letter middle initials, e.g.
    // "Anne L Ryan"), followed by a natural boundary (end of string, whitespace, or a
    // separator like an em/en-dash, hyphen, colon, or comma) rather than running on into
    // unrelated text.
    [GeneratedRegex(@"^(?<name>[A-Z][a-zA-Z'.-]*(?:\s+[A-Z][a-zA-Z'.-]*){1,3})(?=\s|$|[—–\-:,])")]
    private static partial Regex NameLikePrefixRegex();
}

internal static class HtmlNodeExtensions
{
    public static HtmlNode? NextSiblingElement(this HtmlNode node, string tagName)
    {
        var sibling = node.NextSibling;
        while (sibling is not null)
        {
            if (sibling.NodeType == HtmlNodeType.Element)
            {
                return string.Equals(sibling.Name, tagName, StringComparison.OrdinalIgnoreCase) ? sibling : null;
            }
            sibling = sibling.NextSibling;
        }
        return null;
    }

    public static HtmlNode? NextSiblingElementAny(this HtmlNode node)
    {
        var sibling = node.NextSibling;
        while (sibling is not null)
        {
            if (sibling.NodeType == HtmlNodeType.Element)
            {
                return sibling;
            }
            sibling = sibling.NextSibling;
        }
        return null;
    }
}
