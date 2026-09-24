using System.Text.RegularExpressions;
using Scrapper.Models.DTOs;
using Scrapper.Models.Enums;
using Scrapper.Services.Interfaces;
using Scrapper.Services.Models;
using Scrapper.Utils.Constants;

namespace Scrapper.Services.Implementations.Strategies;

/// <summary>
/// Priority 4: recognizable value shapes (currency, email, phone, URL, rating) scanned out
/// of the record's raw text — a last deterministic resort when there's no structured data
/// or matching label to key off of.
/// </summary>
public class PatternExtractionStrategy : IFieldExtractionStrategy
{
    private const int MaxJoinedLocations = 10;
    private const int MaxJoinedValues = 10;

    // Kinds where a record can genuinely have more than one value worth keeping (a
    // specialist can have several fees, speak several languages, list several skills) —
    // for these we return every distinct match rather than just the first.
    private static readonly HashSet<FieldKind> MultiValueKinds = [FieldKind.Location, FieldKind.Currency];

    // The loose postcode-anchored fallback pattern's lookback window can snag summary text
    // that merely *ends* near a postcode-shaped string (rating/review/experience blurbs
    // repeated for responsive layouts) rather than an actual address. None of these phrases
    // has any business appearing in a real street address, so a match containing one is
    // almost certainly noise, not a location.
    private static readonly string[] LocationNoiseIndicators =
        ["review", "booking", "years of experience", "/5", "/ 5", "rating"];

    // Some sites render a link's raw href as its own visible text (e.g. a "share this on X"
    // widget printing the literal https://x.com/.../status/1828866784256000... URL) — a long
    // ID/timestamp embedded in a URL like that happens to be exactly digits-with-optional-
    // separators, which is indistinguishable from a phone number to the shape-only patterns
    // below. Stripping URL-looking substrings out of the scanned text first means their
    // digits are never even seen as a candidate.
    private static readonly Regex UrlTextPattern = new(@"https?://\S+", RegexOptions.Compiled);

    // Some sites (Nuxt/Vue apps especially) never render a phone number into the visible page
    // at all — a "Call" button is wired up purely in client-side JS, reading the number out of
    // the framework's own embedded state payload (a <script type="application/json"> blob,
    // invisible to GetVisibleText, which deliberately excludes script/style content). That
    // payload is still there in the initial server-rendered HTML a plain HTTP fetch sees, and
    // international numbers are stored as plain quoted strings inside it — either "+44 ..." or,
    // just as commonly on this site, "(+44)..." with the country code itself parenthesised and
    // no space before the rest of the number. Requiring a leading "+" (optionally wrapped in
    // parens) is what keeps this safe: unlike a bare digit run (see UrlTextPattern above),
    // almost nothing else in a JSON payload is a quoted string starting with a literal "+".
    private static readonly Regex QuotedInternationalPhonePattern =
        new(@"""(\(?\+\d{1,3}\)?[\d\s\-]{6,14}\d)""", RegexOptions.Compiled);

    public ExtractionSource Source => ExtractionSource.Pattern;

    public Task<ExtractionCandidate?> TryExtractAsync(
        ExtractionContext context, FieldDefinitionDto field, FieldKind resolvedKind, CancellationToken cancellationToken)
    {
        // A mailto:/tel: link carries the real address/number in its href even when the
        // visible label is just a generic "Email"/"Call" button (a click-to-reveal contact
        // widget, common on directory sites) — the scheme itself is an unambiguous signal,
        // so it's checked before falling back to guessing from freeform visible text.
        if (resolvedKind == FieldKind.Email)
        {
            var mailtoCandidate = TryExtractLinkScheme(context, "mailto:");
            if (mailtoCandidate is not null)
            {
                return Task.FromResult<ExtractionCandidate?>(mailtoCandidate);
            }
        }
        else if (resolvedKind == FieldKind.Phone)
        {
            var telCandidate = TryExtractLinkScheme(context, "tel:");
            if (telCandidate is not null)
            {
                return Task.FromResult<ExtractionCandidate?>(telCandidate);
            }
        }

        if (!FieldSynonymCatalog.Patterns.TryGetValue(resolvedKind, out var patterns))
        {
            return Task.FromResult<ExtractionCandidate?>(null);
        }

        var text = UrlTextPattern.Replace(HtmlExtractionUtils.GetVisibleText(context.RecordNode), " ");

        foreach (var pattern in patterns)
        {
            if (MultiValueKinds.Contains(resolvedKind))
            {
                // A specialist can practice at several addresses, or charge several
                // different fees for different services — a single record (e.g. a whole
                // profile page) may genuinely contain more than one match, so it's useful
                // to return all of them rather than just the first.
                var allMatches = pattern.Matches(text)
                    .Select(m => (m.Groups["value"].Success ? m.Groups["value"].Value : m.Value).Trim().TrimEnd(',', '.'))
                    .Where(v => v.Length > 0 && !LocationNoiseIndicators.Any(n => v.Contains(n, StringComparison.OrdinalIgnoreCase)))
                    .Distinct()
                    .Take(resolvedKind == FieldKind.Location ? MaxJoinedLocations : MaxJoinedValues)
                    .ToList();

                if (allMatches.Count > 0)
                {
                    return Task.FromResult<ExtractionCandidate?>(
                        new ExtractionCandidate(string.Join("; ", allMatches), 65, Source));
                }

                continue;
            }

            var match = pattern.Match(text);
            if (match.Success)
            {
                var value = match.Groups["value"].Success ? match.Groups["value"].Value : match.Value;
                var confidence = resolvedKind == FieldKind.Number ? 55 : 65;
                return Task.FromResult<ExtractionCandidate?>(new ExtractionCandidate(value.Trim().TrimEnd(',', '.'), confidence, Source));
            }
        }

        // Nothing found in the visible text — for Phone specifically, fall back to scanning
        // embedded script data (see QuotedInternationalPhonePattern above). Scoped to the
        // record's own script descendants: on a multi-record listing page, individual cards
        // essentially never carry their own <script> tag (those live at document level), so
        // this only ever fires for a single-record/detail page, where a document-wide payload
        // genuinely does belong to "the" record — never accidentally shared across records.
        if (resolvedKind == FieldKind.Phone)
        {
            var scriptCandidate = TryExtractPhoneFromScriptData(context);
            if (scriptCandidate is not null)
            {
                return Task.FromResult<ExtractionCandidate?>(scriptCandidate);
            }
        }

        return Task.FromResult<ExtractionCandidate?>(null);
    }

    private ExtractionCandidate? TryExtractPhoneFromScriptData(ExtractionContext context)
    {
        // A framework's whole-app state payload commonly mixes several unrelated phone
        // numbers in one blob — sitewide nav data, a parent organisation's own number, *and*
        // this specific record's — so grabbing the first quoted match found isn't reliable
        // once there's more than one. A phone number is essentially always printed right next
        // to its own address, so when the record's postal code (already known from JSON-LD) is
        // findable in the same blob, prefer whichever candidate number sits closest to it.
        var postalCode = TryGetPostalCode(context);

        foreach (var script in context.RecordNode.Descendants("script"))
        {
            var content = script.InnerText;
            if (string.IsNullOrEmpty(content))
            {
                continue;
            }

            var matches = QuotedInternationalPhonePattern.Matches(content);
            if (matches.Count == 0)
            {
                continue;
            }

            var postalCodeIndex = postalCode is null ? -1 : content.IndexOf(postalCode, StringComparison.OrdinalIgnoreCase);
            var chosen = postalCodeIndex >= 0
                ? matches.OrderBy(m => Math.Abs(m.Index - postalCodeIndex)).First()
                : matches[0];

            return new ExtractionCandidate(NormalizeInternationalPhone(chosen.Groups[1].Value), 55, Source);
        }

        return null;
    }

    /// <summary>
    /// "(+44)02038840543" -&gt; "+4402038840543" — just drops the parens some sites wrap the
    /// country code in. Deliberately doesn't try to insert a space after the country code:
    /// country codes are 1-3 digits with no way to tell the boundary from the digits alone
    /// (e.g. "+440..." could be misread as a 3-digit code), so guessing would risk mangling
    /// the number rather than just leaving it merged.
    /// </summary>
    private static string NormalizeInternationalPhone(string value) =>
        value.Replace("(", string.Empty).Replace(")", string.Empty).Trim();

    private static string? TryGetPostalCode(ExtractionContext context)
    {
        foreach (var entity in context.StructuredDataObjects)
        {
            if (entity.GetValueOrDefault("address") is Dictionary<string, object?> address
                && address.GetValueOrDefault("postalCode")?.ToString() is { Length: > 0 } postalCode)
            {
                return postalCode;
            }
        }

        return null;
    }

    private ExtractionCandidate? TryExtractLinkScheme(ExtractionContext context, string scheme)
    {
        var href = context.RecordNode.Descendants("a")
            .Select(a => a.GetAttributeValue("href", null))
            .FirstOrDefault(h => h is not null && h.StartsWith(scheme, StringComparison.OrdinalIgnoreCase));

        if (href is null)
        {
            return null;
        }

        // Strip the scheme and any trailing query string (mailto: often carries "?subject=...").
        var value = Uri.UnescapeDataString(href[scheme.Length..].Split('?')[0]).Trim();
        return string.IsNullOrWhiteSpace(value) ? null : new ExtractionCandidate(value, 88, Source);
    }
}
