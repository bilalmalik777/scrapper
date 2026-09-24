# The Generic Scraping Engine

The engine lives in `src/Scrapper.Services` and has no knowledge of any specific website.
The only thing a user is required to supply is a field **name** (e.g. "Name", "Location",
"Fees") — selector and type are both optional escalations. There is no
`if (url.Contains("doctify.com"))` anywhere in the codebase; Doctify is just the first
configuration someone happened to build in the UI (see
[field-configuration.md](field-configuration.md)), and the automatic engine that makes
that possible is documented in full in
[automatic-extraction.md](automatic-extraction.md).

## Pipeline

```
ScraperConfigDto
      │
      ▼
IUrlValidationService     → rejects disallowed schemes / private & internal addresses
      │
      ▼
IHtmlFetcher              → bounded HTTP GET (timeout, redirect cap, max response size)
      │
      ▼
IRecordDetector           → if no record selector was supplied, finds the repeated
      │                       "card"/"row" structure automatically (see below)
      ▼
IStructuredDataParser     → parses JSON-LD once for the whole page
      │
      ▼
IFieldExtractionOrchestrator (per field, per record)
      │   resolves the field's kind (explicit Type, or inferred from its name), then
      │   tries strategies in priority order until one produces a validated value:
      │     1. Selector        (explicit CSS/XPath — Advanced Options)
      │     2. StructuredData  (JSON-LD / schema.org)
      │     3. SemanticHtml    (headings, labelled elements, dt/dd, "Label: Value" text)
      │     4. Pattern         (value-shape regexes: currency, email, phone, URL, ...)
      │     5. Ai              (pluggable, disabled by default)
      ▼
ScrapeResultDto            → records, per-field confidence/source, missing-field
                              tracking, warnings, errors
```

Each stage is a small, independently testable class behind an interface, orchestrated by
`ScraperEngine` (`IScraperEngine`). See `tests/Scrapper.Services.Tests` for unit coverage
of every stage in isolation, and [automatic-extraction.md](automatic-extraction.md) for
how the strategies, confidence scoring, and validation work.

The explicit-selector path (`FieldExtractor` / `IFieldExtractor`) is unchanged from
before and still backs both the **Advanced Options** manual fallback and the **Test
Selector** feature.

## Selectors (optional, Advanced Options only)

Two selector types are supported per field, chosen independently for the record selector
and every field selector:

- **CSS** (via [Fizzler](https://github.com/atifaziz/Fizzler) over HtmlAgilityPack's DOM)
- **XPath** (native to HtmlAgilityPack)

## Extraction types (used only when a selector is supplied)

| Type        | Behavior                                              |
|-------------|--------------------------------------------------------|
| `Text`      | `InnerText`, HTML-entity decoded and trimmed           |
| `Html`      | Raw `InnerHtml` of the matched node                    |
| `Attribute` | Value of a named attribute (`Attribute` field required)|
| `Link`      | `href`, falling back to `src`                          |

## Records

A **record selector** is optional. If supplied, the engine treats every matching node as
one record. If omitted (the normal case), `IRecordDetector` automatically finds the
repeated card/row structure on the page — this is what lets a listing page with N cards
produce N rows without the user ever writing a selector. If no repeated structure is
found either, the whole page is treated as a single record (useful for detail/profile
pages). The response's `recordSelectorAutoDetected` flag tells the client which path was
used.

## Limits (see `Scrapper.Utils/Constants/ScrapingLimits.cs`)

| Limit                     | Value      |
|---------------------------|------------|
| Max response size         | 10 MB      |
| Max timeout               | 30 seconds |
| Max redirects followed    | 5          |
| Max records per scrape    | 1000 (hard cap; user can set lower via `maxRecords`) |
| Max fields per config     | 50         |
| Selector-test preview rows| 5          |
| Min repeated siblings to count as a record structure | 3 |
| Confidence thresholds (High / Medium) | 80 / 50 |

All limits are enforced server-side regardless of what the client requests, so a caller
cannot bypass them by editing the request payload.

## Adding support for a new website

There is nothing to "add" in code, and normally nothing to configure either. From the
portal:

1. Enter the target URL and click **Validate URL**.
2. Add fields by name only — e.g. "Name", "Location", "Fees" — and click **Start
   Scraping**.
3. Review the results and each field's confidence level. For anything at Low confidence
   or "Not found", either accept it (it's clearly flagged as uncertain, never silently
   fabricated) or click **Configure selector** to supply an explicit CSS/XPath selector
   for just that field (Advanced Options), without touching any other field.

Because this is all data (not code), scraper configurations for new websites can also be
saved/reused by the caller of the API (e.g. a script or another tool) simply by keeping a
copy of the `ScraperConfigDto` JSON payload — the API does not persist configurations
itself, since each scrape is a stateless request.
