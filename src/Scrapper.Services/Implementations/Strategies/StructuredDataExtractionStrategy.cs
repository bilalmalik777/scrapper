using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Scrapper.Models.DTOs;
using Scrapper.Models.Enums;
using Scrapper.Services.Interfaces;
using Scrapper.Services.Models;
using Scrapper.Utils.Constants;

namespace Scrapper.Services.Implementations.Strategies;

/// <summary>
/// Priority 2: JSON-LD/schema.org structured data. Generally the most reliable automatic
/// source because it's machine-readable data the site itself publishes, not a visual guess.
/// </summary>
public partial class StructuredDataExtractionStrategy : IFieldExtractionStrategy
{
    public ExtractionSource Source => ExtractionSource.StructuredData;

    public Task<ExtractionCandidate?> TryExtractAsync(
        ExtractionContext context, FieldDefinitionDto field, FieldKind resolvedKind, CancellationToken cancellationToken)
    {
        var entity = context.MatchingStructuredDataObject;
        if (entity is null)
        {
            return Task.FromResult<ExtractionCandidate?>(null);
        }

        // Some sites publish each practice location as its OWN JSON-LD entity (a
        // MedicalClinic/Hospital/LocalBusiness) with both a "name" and an "address" bundled
        // together, alongside the main Physician entity — a strictly better source than the
        // Physician's own bare "address" property (which is just PostalAddress data with no
        // name at all, schema.org has nowhere on it to put one). Preferred whenever it's
        // there; falls through to the existing address-only handling below otherwise.
        if (resolvedKind == FieldKind.Location)
        {
            var fromFacilities = TryBuildLocationsFromClinicEntities(context.StructuredDataObjects);
            if (fromFacilities is not null)
            {
                return Task.FromResult<ExtractionCandidate?>(new ExtractionCandidate(fromFacilities, 90, Source));
            }
        }

        // FieldKind.Number is a grab-bag of unrelated concepts (review counts, endorsement
        // counts, years of experience, ...) — its structured-data path ("reviewCount") is
        // only ever right for an actual review-count field, so applying it to any Number
        // field (e.g. "Experience") would silently substitute the wrong number. Only the
        // direct field-name match below is trustworthy for this kind.
        if (resolvedKind != FieldKind.Number
            && FieldSynonymCatalog.StructuredDataPaths.TryGetValue(resolvedKind, out var paths))
        {
            foreach (var path in paths)
            {
                if (TryGetPath(entity, path, resolvedKind, out var value) && !string.IsNullOrWhiteSpace(value))
                {
                    // A PostalAddress has no "name" field, so a JSON-LD-only address never
                    // says which clinic/practice it's for — but sites commonly show a heading
                    // (the clinic name) immediately followed by the address as plain text
                    // right next to it in the DOM. Enrich with that when it's there; if the
                    // page doesn't have this pattern, the plain address list is unchanged.
                    var final = resolvedKind == FieldKind.Location
                        ? TryEnrichLocationsWithClinicNames(context.RecordNode, value!) ?? value!
                        : value!;
                    return Task.FromResult<ExtractionCandidate?>(new ExtractionCandidate(final, 90, Source));
                }
            }
        }

        // "Role"/"Job Title"/"Specialty"/... are still just free text (no dedicated
        // FieldKind), but schema.org has several overlapping properties for the concept
        // depending on the site's domain — looked up by the field's own name rather than kind.
        if (FieldSynonymCatalog.RoleStructuredDataPaths.TryGetValue(field.Name.Trim(), out var rolePaths))
        {
            foreach (var path in rolePaths)
            {
                if (TryGetPath(entity, path, resolvedKind, out var roleValue) && !string.IsNullOrWhiteSpace(roleValue))
                {
                    return Task.FromResult<ExtractionCandidate?>(new ExtractionCandidate(roleValue!, 85, Source));
                }
            }
        }

        // Fall back to a direct property-name match against the field's own name (e.g. a
        // custom "specialistName" property that isn't in the standard schema.org tables).
        if (TryGetPath(entity, field.Name, resolvedKind, out var direct) && !string.IsNullOrWhiteSpace(direct))
        {
            return Task.FromResult<ExtractionCandidate?>(new ExtractionCandidate(direct!, 75, Source));
        }

        // A single-entity detail page often publishes several JSON-LD blocks side by side
        // (Physician, Organization, WebPage, ...); MatchingStructuredDataObject picks only ONE
        // of them (the one with the most keys) as its best guess for "the" entity, which can
        // easily be a different block than the one actually carrying this specific field (e.g.
        // an Organization block edges out the Physician block on key count, but only the
        // Physician block has "telephone"). On a single-record page there's no ambiguity about
        // which entity a field "belongs" to, so it's safe to check every other published entity
        // too rather than let the field fall through to a much weaker whole-page-text strategy.
        //
        // Name is deliberately excluded here even though it's a "normal" kind: its structured-
        // data path is just "name"/"legalName", a property virtually every schema.org type has
        // (Organization, WebPage, Product, ...) — unlike "telephone" or "email", there's no
        // collision-resistance to lean on, so scanning every other entity for it would happily
        // substitute the clinic's own Organization name for the actual person's name.
        if (context.TotalRecords == 1 && resolvedKind is not (FieldKind.Number or FieldKind.Name)
            && FieldSynonymCatalog.StructuredDataPaths.TryGetValue(resolvedKind, out var fallbackPaths))
        {
            foreach (var other in context.StructuredDataObjects)
            {
                if (ReferenceEquals(other, entity)) continue;

                // Never fall back to a purely navigational/SEO block (WebPage, WebSite, ...) —
                // its "name" is the page's own <title> (often "Real Name | Site Name"), never
                // the record's actual name, and it would otherwise win here precisely because
                // it has the field the real entity might be missing under a different property.
                var otherType = other.GetValueOrDefault("@type")?.ToString() ?? string.Empty;
                if (Models.ExtractionContext.NonContentTypes.Contains(otherType)) continue;

                foreach (var path in fallbackPaths)
                {
                    if (TryGetPath(other, path, resolvedKind, out var value) && !string.IsNullOrWhiteSpace(value))
                    {
                        return Task.FromResult<ExtractionCandidate?>(new ExtractionCandidate(value!, 80, Source));
                    }
                }
            }
        }

        return Task.FromResult<ExtractionCandidate?>(null);
    }

    private static bool TryGetPath(Dictionary<string, object?> root, string path, FieldKind resolvedKind, out string? value)
    {
        object? current = root;

        foreach (var segment in path.Split('.'))
        {
            if (current is Dictionary<string, object?> dict && dict.TryGetValue(segment, out var next))
            {
                current = next;
            }
            else
            {
                value = null;
                return false;
            }
        }

        // A specialist can have several practice addresses — schema.org allows "address" to
        // be an array of PostalAddress objects for exactly this case, so join all of them
        // rather than only describing the first (mirrors PatternExtractionStrategy's
        // multi-location handling for the same reason).
        if (resolvedKind == FieldKind.Location && current is List<object?> addresses && addresses.Count > 0)
        {
            var formatted = addresses
                .Select(Stringify)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct()
                .Take(10)
                .ToList();

            value = formatted.Count > 0 ? string.Join("; ", formatted) : null;
            return !string.IsNullOrWhiteSpace(value);
        }

        value = Stringify(current);
        if (value is not null)
        {
            value = CollapseDuplicateSegments(value);
        }

        return !string.IsNullOrWhiteSpace(value);
    }

    /// <summary>
    /// Renders a JSON-LD value as plain text. Deliberately returns null (rather than a
    /// fabricated/garbage string) for a nested object it doesn't recognize how to describe —
    /// the engine never surfaces a value it has no real basis for.
    /// </summary>
    private static string? Stringify(object? value) => value switch
    {
        null => null,
        string s => s,
        List<object?> list when list.Count > 0 => Stringify(list[0]),
        Dictionary<string, object?> dict => StringifyObject(dict),
        _ => value.ToString(),
    };

    private static string? StringifyObject(Dictionary<string, object?> dict)
    {
        foreach (var key in new[] { "name", "text", "value" })
        {
            if (dict.TryGetValue(key, out var v) && v is string s && !string.IsNullOrWhiteSpace(s))
            {
                return s;
            }
        }

        // A schema.org PostalAddress (or similar address-shaped object) has no single
        // "name" field but can be composed into a readable address from its parts.
        var addressParts = new[] { "streetAddress", "addressLocality", "addressRegion", "postalCode" }
            .Select(k => dict.GetValueOrDefault(k) as string)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        return addressParts.Count > 0 ? string.Join(", ", addressParts) : null;
    }

    /// <summary>
    /// Some sites publish the same value repeated verbatim as a comma-joined string (a data
    /// quality quirk, not multiple distinct values) — collapse those down to one occurrence
    /// without touching genuinely distinct comma-separated parts (e.g. an address).
    /// </summary>
    private static string CollapseDuplicateSegments(string value)
    {
        if (!value.Contains(','))
        {
            return value;
        }

        var parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var distinct = parts.Distinct().ToArray();
        return distinct.Length > 0 && distinct.Length < parts.Length ? string.Join(", ", distinct) : value;
    }

    /// <summary>
    /// A common directory-page layout shows each practice location as a heading (the clinic
    /// name) immediately followed by its address as sibling text — schema.org's PostalAddress
    /// has nowhere to carry that name, so it's picked up from the DOM instead and prefixed
    /// onto the matching address by postcode. Addresses with no matching heading are left
    /// bare; returns null (use the plain address list as-is) if no page has this pattern at
    /// all, rather than partially rewriting a list it found no real signal for.
    /// </summary>
    private static string? TryEnrichLocationsWithClinicNames(HtmlNode record, string joinedAddresses)
    {
        var addresses = joinedAddresses.Split("; ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (addresses.Length == 0)
        {
            return null;
        }

        // Scoped to headings whose sibling text actually looks like an address (ends in a
        // postcode-shaped substring) — not just any short heading+sibling pair, which would
        // also catch unrelated content elsewhere on the page (an article title followed by a
        // byline, say) and throw off the positional pairing below.
        var pairs = record.Descendants()
            .Where(n => n.Name is "h1" or "h2" or "h3" or "h4" or "h5" or "h6")
            .Select(h => (Heading: HtmlExtractionUtils.GetVisibleText(h).Trim(), Sibling: h.NextSiblingElementAny()))
            .Where(x => x.Heading.Length is > 0 and <= 150 && x.Sibling is not null)
            .Select(x => (x.Heading, SiblingText: HtmlExtractionUtils.GetVisibleText(x.Sibling!).Trim()))
            .Where(x => x.SiblingText.Length is > 0 and <= 200 && PostcodeRegex().IsMatch(x.SiblingText))
            .ToList();

        if (pairs.Count == 0)
        {
            return null;
        }

        // Prefer pairing by position when the counts line up exactly — two different practice
        // locations can share the same postcode (a hospital with several entrances/buildings,
        // clinics in the same complex, ...), which makes postcode-substring matching pick the
        // same heading for both. Position is unambiguous when it's available; postcode
        // matching is the fallback for when the two lists don't align 1:1.
        var usePositional = pairs.Count == addresses.Length;

        var enriched = new List<string>();
        var anyMatched = false;

        for (var i = 0; i < addresses.Length; i++)
        {
            var address = addresses[i];
            string? heading = null;

            if (usePositional)
            {
                heading = pairs[i].Heading;
            }
            else
            {
                var postcode = PostcodeRegex().Match(address) is { Success: true } m ? m.Value : null;
                if (postcode is not null)
                {
                    heading = pairs.FirstOrDefault(p => p.SiblingText.Contains(postcode, StringComparison.OrdinalIgnoreCase)).Heading;
                }
            }

            if (heading is not null)
            {
                enriched.Add($"{heading}, {address}");
                anyMatched = true;
            }
            else
            {
                enriched.Add(address);
            }
        }

        return anyMatched ? string.Join("; ", enriched) : null;
    }

    [GeneratedRegex(@"[A-Z]{1,2}\d[A-Z\d]?\s\d[A-Z]{2}$", RegexOptions.IgnoreCase)]
    private static partial Regex PostcodeRegex();

    // schema.org types that represent a physical place a specialist practices at — as opposed
    // to the Physician/Person entity itself, or navigational noise (see ExtractionContext's
    // NonContentTypes).
    private static readonly HashSet<string> FacilityTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "MedicalClinic", "MedicalOrganization", "LocalBusiness", "Hospital", "Clinic", "MedicalBusiness",
    };

    /// <summary>
    /// Builds "Clinic Name, Address" entries directly from sibling facility entities in the
    /// page's own structured data, when the page publishes them — no DOM guessing needed at
    /// all. Returns null (fall back to the address-only path) if the page has no such
    /// entities, or none of them have both a name and an address.
    /// </summary>
    private static string? TryBuildLocationsFromClinicEntities(IReadOnlyList<Dictionary<string, object?>> allEntities)
    {
        var facilities = allEntities
            .Where(e => FacilityTypes.Contains(e.GetValueOrDefault("@type")?.ToString() ?? string.Empty))
            .Select(e => (Name: e.GetValueOrDefault("name") as string, Address: Stringify(e.GetValueOrDefault("address"))))
            .Where(x => !string.IsNullOrWhiteSpace(x.Name) && !string.IsNullOrWhiteSpace(x.Address))
            .Select(x => $"{x.Name}, {x.Address}")
            .Distinct()
            .Take(10)
            .ToList();

        return facilities.Count > 0 ? string.Join("; ", facilities) : null;
    }
}
