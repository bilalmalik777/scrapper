using System.Text.RegularExpressions;
using Scrapper.Models.Enums;

namespace Scrapper.Utils.Constants;

/// <summary>
/// Single, data-driven source of truth for mapping a user-typed field name (e.g. "Consultation Fee")
/// to a <see cref="FieldKind"/>, and for the value-shape patterns used to find and validate that kind
/// on a page. Nothing else in the codebase should hard-code field-name-to-kind heuristics — extend the
/// tables here instead.
/// </summary>
public static class FieldSynonymCatalog
{
    /// <summary>
    /// Synonym/indicator tokens per kind, most-specific first. Field-name inference picks the kind
    /// whose tokens best match the user's field name (see FieldInferenceEngine).
    /// </summary>
    public static readonly Dictionary<FieldKind, string[]> Synonyms = new()
    {
        [FieldKind.Name] =
        [
            "name", "full name", "fullname", "specialist", "doctor", "dr", "consultant",
            "practitioner", "provider", "person", "clinician", "surgeon", "therapist"
        ],
        [FieldKind.Location] =
        [
            "location", "address", "based in", "practice location", "clinic location",
            "city", "town", "postcode", "zip", "region"
            // Deliberately excludes "area" and "place" — both are near-universal generic CSS/id
            // naming conventions unrelated to geography ("widget-area", "footer-area",
            // "content-area", "marketplace"), so matching them as class/id/testid substrings
            // reliably produces false positives (e.g. a page's "footer-widget-area" wrapper
            // being mistaken for a location container) rather than real signal.
            //
            // Also excludes "post code" (as two words) — the word-level fallback matcher
            // splits multi-word tokens and treats "post" alone as a 4+-char match, and "post"
            // by itself is everywhere in unrelated contexts (WordPress's own
            // "menu-item-type-post_type" class fragment, blog "post" listings, ...). "postcode"
            // (one word) is unaffected and stays, since it's only ever matched as a whole
            // 8-character substring, never split.
        ],
        [FieldKind.Currency] =
        [
            "fee", "fees", "price", "pricing", "cost", "consultation fee", "treatment cost",
            "charge", "amount"
            // Deliberately excludes short/generic tokens like "from" and "rate" — real sites
            // frequently use "rate" in unrelated class names (e.g. a "rate-paper-container"
            // rating widget), causing false-positive matches against large wrapper elements.
        ],
        [FieldKind.Email] = ["email", "e-mail", "mail"],
        [FieldKind.Phone] = ["phone", "telephone", "tel", "mobile", "contact number", "call"],
        [FieldKind.Url] = ["website", "url", "link", "profile", "site", "web"],
        [FieldKind.Rating] = ["rating", "score", "stars", "review score"],
        [FieldKind.Number] =
        [
            "reviews", "review count", "count", "number of", "endorsements", "years",
            "experience", "years experience", "years of experience", "years' experience"
        ],
        [FieldKind.Biography] =
        [
            "bio", "biography", "about", "about me", "about us", "overview", "summary",
            "profile description", "introduction", "personal statement"
        ],
    };

    /// <summary>Value-shape patterns used both to *find* candidate values on the page and to validate them.</summary>
    public static readonly Dictionary<FieldKind, Regex[]> Patterns = new()
    {
        [FieldKind.Currency] =
        [
            new Regex(@"(?:from\s+)?[£$€]\s?\d[\d,]*(?:\.\d{1,2})?", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex(@"\b\d[\d,]*(?:\.\d{1,2})?\s?(?:GBP|USD|EUR)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        ],
        [FieldKind.Email] =
        [
            new Regex(@"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}", RegexOptions.Compiled),
        ],
        [FieldKind.Phone] =
        [
            new Regex(@"(?:\+?\d{1,3}[\s.-]?)?\(?\d{2,5}\)?[\s.-]?\d{3,4}[\s.-]?\d{3,4}", RegexOptions.Compiled),
        ],
        [FieldKind.Url] =
        [
            new Regex(@"https?://[^\s""'<>]+", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        ],
        [FieldKind.Rating] =
        [
            new Regex(@"\b([0-4](\.\d{1,2})?|5(\.0{1,2})?)\s*/\s*5\b", RegexOptions.Compiled),
            new Regex(@"\b([0-4](\.\d{1,2})?|5(\.0{1,2})?)\s*(?:stars?|★)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        ],
        [FieldKind.Number] =
        [
            // "18 Years experience", "20+ years' experience" — the only Number pattern.
            // Deliberately no generic bare-digits fallback (e.g. \d+) — with no unit word to
            // anchor it, that would happily match a phone number, an ID, or any other
            // unrelated digit string that appears anywhere near the field's label, which is
            // exactly the kind of value this engine must never fabricate/misattribute.
            new Regex(@"\b\d+\+?\s*years?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        ],
        [FieldKind.Location] =
        [
            // Common directory-listing convention: "12.3 miles | 32 St Thomas St, London, SE1 9BS".
            // Non-greedy up to the first UK postcode bounds the address precisely even when the
            // surrounding markup has no separators between this and the next piece of content.
            new Regex(@"\d+(?:\.\d+)?\s*miles?\s*\|\s*(?<value>.+?[A-Z]{1,2}\d[A-Z\d]?\s\d[A-Z]{2})", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            // A UK postcode anchors a trailing address fragment even without a "miles |" prefix.
            new Regex(@"(?<value>[A-Za-z0-9][^|\n]{4,80}?[A-Z]{1,2}\d[A-Z\d]?\s\d[A-Z]{2})", RegexOptions.Compiled),
        ],
    };

    /// <summary>Common schema.org / JSON-LD property paths (dot-separated) to try for each kind, most specific first.</summary>
    public static readonly Dictionary<FieldKind, string[]> StructuredDataPaths = new()
    {
        [FieldKind.Name] = ["name", "legalName", "author.name"],
        [FieldKind.Location] =
        [
            "address.addressLocality", "address.streetAddress", "address", "areaServed", "location.name"
        ],
        [FieldKind.Currency] = ["offers.price", "offers.priceSpecification.price", "priceRange", "price"],
        [FieldKind.Email] = ["email"],
        [FieldKind.Phone] = ["telephone", "phone"],
        [FieldKind.Url] = ["url", "sameAs"],
        [FieldKind.Rating] = ["aggregateRating.ratingValue", "ratingValue"],
        [FieldKind.Number] = ["aggregateRating.reviewCount", "reviewCount"],
        [FieldKind.Biography] = ["description"],
    };

    /// <summary>
    /// Field-name-driven (not kind-driven) structured-data aliases for the "role / job title /
    /// specialty" concept commonly shown as a short line under a person's name. Schema.org
    /// has several overlapping properties for this (jobTitle, medicalSpecialty, occupation)
    /// depending on the site's domain, and the field itself is still just free text (no
    /// dedicated FieldKind), so it's keyed by name here rather than kind.
    /// </summary>
    public static readonly Dictionary<string, string[]> RoleStructuredDataPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        ["role"] = ["jobTitle", "medicalSpecialty.name", "occupation.name", "specialty.name"],
        ["job title"] = ["jobTitle", "medicalSpecialty.name", "occupation.name"],
        ["jobtitle"] = ["jobTitle", "medicalSpecialty.name", "occupation.name"],
        ["title"] = ["jobTitle", "medicalSpecialty.name"],
        ["position"] = ["jobTitle", "occupation.name"],
        ["specialty"] = ["medicalSpecialty.name", "occupation.name"],
        ["speciality"] = ["medicalSpecialty.name", "occupation.name"],
        ["specialization"] = ["medicalSpecialty.name"],
        ["profession"] = ["jobTitle", "occupation.name"],
        ["designation"] = ["jobTitle"],
        ["occupation"] = ["occupation.name", "jobTitle"],
    };
}
