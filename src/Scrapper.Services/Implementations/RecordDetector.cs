using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Scrapper.Models.DTOs;
using Scrapper.Models.Enums;
using Scrapper.Services.Interfaces;
using Scrapper.Utils.Constants;

namespace Scrapper.Services.Implementations;

/// <summary>
/// Detects repeated "record" nodes (specialist cards, product tiles, table rows, ...) on a
/// listing page without any site-specific knowledge, by grouping sibling elements that share
/// the same tag/class/data-testid "signature". Purely structural repetition is not enough on
/// its own — a detail page can repeat an incidental sub-widget (skill bars, opening-hours
/// rows, tag chips) far more often than its one genuine content section repeats (once). So
/// when the caller tells us which fields it actually wants, candidates are re-ranked by how
/// well they actually contain those fields, not just by how structurally rich/repeated they
/// are — this is what lets a detail page correctly fall back to "one record, the whole page"
/// instead of mistaking 145 skill-rating bars for 145 distinct records.
/// </summary>
public class RecordDetector(IFieldInferenceEngine inferenceEngine) : IRecordDetector
{
    private const int FieldSampleSize = 5;

    private static readonly HashSet<string> IgnoredTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "head", "meta", "link", "noscript", "svg", "path", "br", "hr"
    };

    public List<HtmlNode>? DetectRecords(HtmlNode documentNode, IReadOnlyList<FieldDefinitionDto>? fields = null)
    {
        var candidates = BuildCandidates(documentNode);
        if (candidates.Count == 0)
        {
            return null;
        }

        if (fields is not { Count: > 0 })
        {
            return candidates.OrderByDescending(c => c.StructuralScore).First().Nodes;
        }

        var scored = candidates
            .Select(c => (c.Nodes, c.StructuralScore, c.IsBareTag, c.Signature, FieldHitRate: FieldHitRate(c.Nodes, fields)))
            .ToList();

        // A repeated card grid is frequently wrapped in its own repeated ROW container (e.g. a
        // page-builder splitting N cards into several 3-per-row groups) — that row element is
        // *itself* a valid sibling group, sitting directly above the real card group in the
        // same DOM chain, and it inherits the same field hits its children have (a row's first
        // heading/title-block is just its first card's). Its raw structural score is always
        // bigger too (many cards' worth of text/descendants vs. one), so it would otherwise
        // always beat the correct, finer-grained candidate. Whenever a qualifying candidate's
        // nodes sit entirely inside another qualifying candidate's nodes, the outer one is a
        // wrapper, not the record unit — drop it and let the inner one win instead.
        bool Qualifies((List<HtmlNode> Nodes, double StructuralScore, bool IsBareTag, string Signature, double FieldHitRate) c) =>
            c.FieldHitRate > (c.IsBareTag ? 0.65 : 0.4);

        var qualifying = scored.Where(Qualifies).ToList();
        var wrappers = qualifying
            .Where(outer => qualifying.Any(inner =>
                !ReferenceEquals(inner.Nodes, outer.Nodes)
                && inner.Nodes.Count > 0
                && inner.Nodes.All(n => outer.Nodes.Any(o => !ReferenceEquals(o, n) && o.Descendants().Contains(n)))))
            .ToList();
        var candidatePool = qualifying.Except(wrappers).ToList();
        if (candidatePool.Count == 0)
        {
            candidatePool = scored;
        }

        // A page-builder grid often splits its cards across several ROW containers (e.g. 3
        // doctors per row, repeated for every row) rather than one flat list of siblings — each
        // row is a separate DOM parent, so the sibling-grouping pass above naturally produces
        // one small, separate qualifying candidate per row instead of a single group spanning
        // all of them. Once wrapper rows are already excluded above, any surviving qualifying
        // candidates that share the exact same tag/class/testid signature are, by construction,
        // the same kind of card repeated under different row parents — safe to merge into one
        // combined record set rather than arbitrarily keeping only the single highest-scoring
        // row's cards and discarding the rest.
        var merged = candidatePool
            .GroupBy(c => c.Signature)
            .Select(g => g.Count() == 1
                ? g.First()
                : (
                    Nodes: g.SelectMany(c => c.Nodes).ToList(),
                    StructuralScore: g.Sum(c => c.StructuralScore),
                    IsBareTag: g.First().IsBareTag,
                    Signature: g.Key,
                    FieldHitRate: g.Average(c => c.FieldHitRate)
                ))
            .ToList();

        var ranked = merged
            .OrderByDescending(c => c.StructuralScore * (0.5 + c.FieldHitRate))
            .First();

        // Require a meaningful fraction of the requested fields to actually show up in the
        // candidate, not just a trace amount — e.g. a page's practice-location cards each
        // have their own heading (matching a "Name" field by pure coincidence of kind) but
        // none of the other requested fields, which would otherwise look like a weak but
        // nonzero signal. Below this bar, structural repetition alone isn't a reliable
        // signal here (likely a detail page with incidental repeated UI) — better to report
        // "no records detected" and let the caller fall back to a single whole-page record
        // than to return misleading pseudo-records.
        //
        // A candidate whose signature came from the bare-tag fallback (a classless <li>/<tr>/
        // ...) needs a stronger hit rate before it's trusted: a generic single-entity page
        // (a doctor's own profile) very commonly has ITS OWN classless <li> groups too — page
        // section headers, a table of contents — that a classed "real" card almost never
        // does, so the bare-tag case is inherently more likely to be incidental UI rather
        // than a genuine list of distinct entities.
        var minFieldHitRate = ranked.IsBareTag ? 0.65 : 0.4;
        if (ranked.FieldHitRate <= minFieldHitRate)
        {
            return null;
        }

        // A candidate that mostly misses a requested Name field can still clear the
        // average-hit-rate bar above when few other fields are requested (e.g. Name+Phone:
        // a 20% Name hit averaged with a 100% Phone hit is 60%, comfortably over 0.4) — that's
        // exactly what a detail page's own major sections (Clinics/Credentials/Reviews) look
        // like: only ONE of the sections happens to nest the real doctor's name heading deep
        // inside it, the rest have none. A genuine repeated entity list has a per-item
        // name-shaped heading in (almost) every sample, so require most of the sample to hit
        // on Name specifically, not just a decent blended average.
        var nameField = fields.FirstOrDefault(f => (f.Type ?? inferenceEngine.Infer(f.Name)) == FieldKind.Name);
        if (nameField is not null)
        {
            var sample = ranked.Nodes.Take(FieldSampleSize).ToList();
            var nameKind = nameField.Type ?? inferenceEngine.Infer(nameField.Name);
            var nameHitRate = sample.Count == 0 ? 0 : (double)sample.Count(n => NodeLikelyContainsField(n, nameField, nameKind)) / sample.Count;
            if (nameHitRate < 0.5)
            {
                return null;
            }
        }

        return ranked.Nodes;
    }

    private List<(List<HtmlNode> Nodes, double StructuralScore, bool IsBareTag, string Signature)> BuildCandidates(HtmlNode documentNode)
    {
        var candidates = new List<(List<HtmlNode> Nodes, double StructuralScore, bool IsBareTag, string Signature)>();

        foreach (var (group, isBareTag, signature) in GroupBySiblingSignature(documentNode))
        {
            if (group.Count < ScrapingLimits.MinRepeatedSiblingsForRecord || group.Count > 300)
            {
                continue;
            }

            var avgTextLength = group.Average(n => HtmlExtractionUtils.GetVisibleText(n).Length);
            var avgDescendantCount = group.Average(n => n.Descendants().Count());

            // Require some minimal richness — filters out repeated nav links, bullet icons, etc.
            if (avgTextLength < 15 || avgDescendantCount < 2)
            {
                continue;
            }

            var score = group.Count * Math.Log(avgTextLength + 1) * Math.Log(avgDescendantCount + 1);
            candidates.Add((group, score, isBareTag, signature));
        }

        return candidates;
    }

    /// <summary>
    /// The average hit rate of the *best two* requested fields (or the one field, if only one
    /// was requested) — not averaged across every requested field. A scrape commonly mixes
    /// fields that genuinely appear on the listing page (Name, Location) with ones that only
    /// exist on each record's own detail page (Bio, Qualifications, ...) and necessarily score
    /// 0 here, before any profile page has even been fetched. Averaging across *all* requested
    /// fields would let those detail-only fields dilute a perfectly good candidate below the
    /// acceptance threshold. Requiring two fields (rather than the single best) instead of one
    /// still guards against a single coincidental match — e.g. a heading that happens to
    /// satisfy a generically-named "Name" field on an unrelated repeated widget — from alone
    /// being enough to win.
    /// </summary>
    private double FieldHitRate(List<HtmlNode> nodes, IReadOnlyList<FieldDefinitionDto> fields)
    {
        var sample = nodes.Take(FieldSampleSize).ToList();
        if (sample.Count == 0)
        {
            return 0;
        }

        var rates = fields
            .Select(field =>
            {
                var kind = field.Type ?? inferenceEngine.Infer(field.Name);
                var hits = sample.Count(node => NodeLikelyContainsField(node, field, kind));
                return (double)hits / sample.Count;
            })
            .OrderByDescending(rate => rate)
            .ToList();

        var topCount = Math.Min(2, rates.Count);
        return rates.Take(topCount).Average();
    }

    // Common generic page-section headings (About, Clinics, Reviews, FAQs, etc.) that are
    // structurally identical to a real repeated-card heading but never a person/entity name —
    // this keeps a single detail page's own major sections from being mistaken for
    // "the" record group (e.g. a doctor profile page's "Clinics"/"Credentials"/"Reviews"
    // section headings looking like 6 separate doctor records).
    private static readonly HashSet<string> GenericSectionHeadings = new(StringComparer.OrdinalIgnoreCase)
    {
        "about", "overview", "gallery", "testimonials", "contact", "clinics", "credentials",
        "reviews", "treatments", "professional statement", "frequently asked questions",
        "faq", "faqs", "qualifications", "experience", "specialties", "specialisms",
        "insurance", "fees", "location", "locations", "map", "biography", "bio",
        // A doctor profile page's own tab/accordion section titles (e.g. TopDoctors) — same
        // false-positive shape as the ones above, just a different site's exact wording.
        "professional experience", "subspecialties", "professional memberships",
        "education", "positions in organisations", "positions in organizations",
        "prizes and awards", "languages spoken",
    };

    // Matches 2-5 consecutive Title Case words (e.g. "Alan Buckley") — used only as a fallback
    // when a heading is too short to judge on its own (see NodeLikelyContainsField's Name case).
    private static readonly Regex TitleCaseNamePattern =
        new(@"(?:[A-Z][a-zA-Z'-]*\s+){1,4}[A-Z][a-zA-Z'-]*", RegexOptions.Compiled);

    private static bool NodeLikelyContainsField(HtmlNode node, FieldDefinitionDto field, FieldKind kind)
    {
        if (!string.IsNullOrWhiteSpace(field.Selector))
        {
            return HtmlExtractionUtils.SelectNodes(node, field.Selector, field.SelectorType).Count > 0;
        }

        if (kind == FieldKind.Name)
        {
            // Name-kind hit detection relies solely on a heading — never on attribute/testid
            // token matching. "name" is too generic a word for that: normalizing hyphens to
            // spaces (needed so genuine multi-word signatures like "search-result-card"
            // match) means a completely unrelated "keyword-name" or "field-name" label
            // becomes indistinguishable from a real name attribute once split into words.
            // A heading that's a question ("What is X's specialty?") is never a person/entity
            // name — this specifically keeps an FAQ accordion (each answer a classless <li>
            // with its own <h3> question, structurally identical to a genuine repeated-card
            // list) from being mistaken for "the" record group on a single-entity detail page.
            //
            // Only the FIRST heading in document order counts, not any heading anywhere in the
            // subtree: a real card's own name heading is always right at its top, but a large
            // page-section block (e.g. a doctor profile's whole "Professional statement" div)
            // contains dozens of unrelated sub-headings deeper inside, so an `Any()` search
            // would find some qualifying-looking heading in almost every large block by chance.
            var heading = node.Descendants().FirstOrDefault(d => d.Name is "h1" or "h2" or "h3" or "h4");

            // Some sites (typically page-builder output like WordPress block themes) never use
            // a real heading tag for a card's title at all — just a styled <p>/<a> wrapped in a
            // "title"-classed container (e.g. Gutenberg's "is-style-title"). Only trust this as
            // a fallback, and require "title" as its own whole word after hyphen-normalizing
            // the class (same rule MatchesAnyToken applies elsewhere) — a substring match would
            // also catch unrelated things like "subtitle" or "article-title-wrapper".
            heading ??= node.Descendants().FirstOrDefault(d =>
                HtmlExtractionUtils.BuildAttributeSignature(d).Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("title"));

            if (heading is null) return false;
            var headingText = HtmlExtractionUtils.GetVisibleText(heading).Trim();
            if (headingText.Length == 0 || headingText.EndsWith('?') || GenericSectionHeadings.Contains(headingText))
            {
                return false;
            }

            // A real person's name is Title Case with every word capitalized ("Mr Michael
            // Mouzakis", "Arjun Shankar") — a page's own category label is Sentence case (only
            // its first word capitalized: "Professional experience", "Gallbladder surgery").
            // This is the general, site-agnostic signal that tells the two apart; a curated
            // word list alone could never keep up with every site's own section/treatment/
            // specialty labels (structurally identical to a real repeated-card heading).
            static bool LooksLikeName(string text) =>
                text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Count(w => char.IsUpper(w[0])) >= 2;

            if (LooksLikeName(headingText))
            {
                return true;
            }

            // Some sites (e.g. an "AB" initials avatar as the actual heading) print a short,
            // non-name-shaped heading and put the real full name as plain text right beside it
            // instead. Only worth a broader scan when the heading was implausibly short to BE
            // a name by itself — anything longer that still isn't Title Case is a real label,
            // not a name split across elements, and must not be rescued by this fallback.
            if (headingText.Length > 4)
            {
                return false;
            }

            var nearbyText = HtmlExtractionUtils.GetVisibleText(node);
            var window = nearbyText.Length > 200 ? nearbyText[..200] : nearbyText;
            return TitleCaseNamePattern.IsMatch(window);
        }

        // See SemanticHtmlExtractionStrategy.GetTokens for why Number's synonym bucket is
        // excluded here — its entries (endorsements, reviews, years, ...) aren't
        // interchangeable evidence for any specific Number-kind field.
        var tokens = kind != FieldKind.Number && FieldSynonymCatalog.Synonyms.TryGetValue(kind, out var synonyms)
            ? synonyms.Append(field.Name.ToLowerInvariant()).ToArray()
            : [field.Name.ToLowerInvariant()];

        if (node.Descendants().Any(d => d.NodeType == HtmlNodeType.Element && HtmlExtractionUtils.MatchesAnyToken(HtmlExtractionUtils.BuildAttributeSignature(d), tokens)))
        {
            return true;
        }

        if (FieldSynonymCatalog.Patterns.TryGetValue(kind, out var patterns) && patterns.Length > 0)
        {
            // Every registered pattern for a kind counts here now — Number's old generic
            // catch-all fallback (which would call almost any digit "a hit") was removed
            // entirely (see PatternExtractionStrategy/FieldSynonymCatalog), so there's no
            // longer a noisy pattern to guard against for any kind. Restricting to only the
            // first pattern was otherwise actively harmful for Location: a card with a plain
            // "City County POSTCODE" line (no "X miles |" prefix) only matches the *second*,
            // still perfectly specific postcode-anchored pattern.
            var text = HtmlExtractionUtils.GetVisibleText(node);
            if (patterns.Any(p => p.IsMatch(text)))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<(List<HtmlNode> Nodes, bool IsBareTag, string Signature)> GroupBySiblingSignature(HtmlNode documentNode)
    {
        var groups = new Dictionary<string, (List<HtmlNode> Nodes, bool IsBareTag, string Signature)>();

        foreach (var parent in documentNode.Descendants()
                     .Where(n => n.NodeType == HtmlNodeType.Element)
                     .Take(20_000))
        {
            var childrenByParent = new Dictionary<string, (List<HtmlNode> Nodes, bool IsBareTag)>();

            foreach (var child in parent.ChildNodes)
            {
                if (child.NodeType != HtmlNodeType.Element || IgnoredTags.Contains(child.Name))
                {
                    continue;
                }

                var signature = BuildSignature(child);
                if (signature is null)
                {
                    continue;
                }

                if (!childrenByParent.TryGetValue(signature.Value.Signature, out var entry))
                {
                    entry = ([], signature.Value.IsBareTag);
                    childrenByParent[signature.Value.Signature] = entry;
                }

                entry.Nodes.Add(child);
            }

            foreach (var (signature, entry) in childrenByParent)
            {
                if (entry.Nodes.Count < ScrapingLimits.MinRepeatedSiblingsForRecord)
                {
                    continue;
                }

                var key = $"{parent.XPath}>{signature}";
                groups[key] = (entry.Nodes, entry.IsBareTag, signature);
            }
        }

        return groups.Values;
    }

    // Tags whose own HTML semantics already mean "one item in a repeated list/table" — a
    // <ul> full of bare <li> siblings (no class or id at all, just component-framework scoped-
    // style markers like data-v-xxxx) is unambiguous repetition, so the bare tag name alone is
    // trusted as a signature for these specifically. Generic layout tags (div, span, a, ...)
    // are NOT included here, since bare-tag matching for those would collapse every div/span
    // on the page into one meaningless group.
    private static readonly HashSet<string> SelfDescribingListTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "li", "tr", "dd", "option",
    };

    private static (string Signature, bool IsBareTag)? BuildSignature(HtmlNode node)
    {
        var classAttr = node.GetAttributeValue("class", string.Empty);
        var classes = classAttr
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();
        var testId = node.GetAttributeValue("data-testid", string.Empty);

        // A signature normally needs *something* distinguishing beyond the bare tag name,
        // otherwise every <div> or <span> on the page would collapse into one meaningless
        // group — except for a small set of tags (see SelfDescribingListTags) whose own HTML
        // semantics already say "repeated list item" with no class/id needed at all.
        var isBareTag = classes.Count == 0 && string.IsNullOrEmpty(testId);
        if (isBareTag && !SelfDescribingListTags.Contains(node.Name))
        {
            return null;
        }

        return ($"{node.Name}|{string.Join('.', classes)}|{testId}", isBareTag);
    }
}
