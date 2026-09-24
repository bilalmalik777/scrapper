# Automatic Field Extraction

This is the engine that lets a user type just a field name — "Name", "Location", "Fees"
— and get real data back, with no CSS selector, no XPath, no HTML knowledge required. It
never claims perfect accuracy; every value comes back with a confidence level, and a
field the engine genuinely can't find is reported as "Not found", never fabricated.

## Field configuration is three modes, one field type

`FieldDefinitionDto` (`src/Scrapper.Models/DTOs/ScraperDtos.cs`) has exactly one required
property: `Name`. Everything else is an optional escalation, and all three modes can be
mixed freely within the same scrape configuration:

| Mode | What you supply | What happens |
|---|---|---|
| **Automatic** | `Name` only | Kind is inferred from the name; all automatic strategies run. |
| **Semi-automatic** | `Name` + `Type` | Kind is fixed (skips inference), still no selector — narrows which patterns/paths are tried. |
| **Advanced / manual** | `Name` + `Selector` (+ optional `Type`) | The explicit selector is used directly (confidence 99), exactly like the original selector-only engine. |

## How a field's kind is decided — `IFieldInferenceEngine`

If `Type` isn't supplied, `FieldInferenceEngine.Infer(fieldName)` matches the name
against the synonym tables in `Scrapper.Utils/Constants/FieldSynonymCatalog.cs` — the
**single, centralized, data-driven** place all field-name-to-kind heuristics live (never
scattered through the codebase). "Consultation Fee", "Treatment Price", "Cost", and
"Pricing" all resolve to `Currency` because they share tokens in that table; extending
support for a new kind of field name means editing this one file.

## The strategies, in priority order

`IFieldExtractionOrchestrator` (`FieldExtractionOrchestrator.cs`) runs these in order for
every field, on every record, stopping at the first one whose result passes validation:

1. **Selector** (`SelectorExtractionStrategy`) — only "applies" if the field has an
   explicit selector; otherwise it steps aside immediately.
2. **StructuredData** (`StructuredDataExtractionStrategy`) — reads JSON-LD parsed once
   per page by `StructuredDataParser` (unwraps `@graph` and `ItemList`/
   `itemListElement` wrappers into one entity per record), and looks up the field's kind
   against known schema.org paths (e.g. `Location` → `address.addressLocality`).
   Generally the most reliable source because it's the site's own machine-readable data,
   not a visual guess.
3. **SemanticHtml** (`SemanticHtmlExtractionStrategy`) — reads the record's actual HTML:
   the first heading for `Name`, elements whose `class`/`id`/`data-testid`/`aria-label`
   contains a kind synonym (picking the *most specific*, i.e. shortest-text, match to
   avoid swallowing a large wrapper element), `<dt>`/`<dd>` pairs, and "Label: Value"
   text patterns.
4. **Pattern** (`PatternExtractionStrategy`) — recognizable value shapes: currency
   (`£150`, `150 GBP`), email, phone, URL, rating (`4.9/5`), and address-like text
   anchored by a UK postcode or a "12 miles | address" distance prefix.
5. **Ai** (`AiExtractionStrategy`) — see [Pluggable AI extraction](#pluggable-ai-extraction-disabled-by-default) below.

## Automatic record detection — `IRecordDetector`

When no record selector is supplied, `RecordDetector.DetectRecords` groups sibling
elements by a "signature" (tag + sorted class list + `data-testid`), and picks the
richest, most-repeated group as the record structure — e.g. it finds that a Doctify
listing page repeats `[data-testid="search-result-card"]` ten times without anyone
telling it that selector. Groups with too little text/structure (like a row of four nav
links) are filtered out so they don't outrank genuine content cards. If nothing
qualifies, the whole page is treated as a single record.

## Confidence scoring and validation

Every strategy returns a candidate with a base confidence (99 for an explicit selector,
90 for structured data, 80–85 for semantic HTML, 55–65 for pattern matches, 60 for AI).
Before accepting a candidate, `FieldValueValidator.IsPlausible` checks it actually looks
like the right kind of value — e.g. a `Currency` value must match a currency pattern, a
`Location` value must *not* look like a currency or email value. A candidate that fails
this check is not discarded outright: if nothing else was found, it's still surfaced but
at roughly half its confidence (almost always landing in the "Low" band), so the user
sees *something found, but treat it with suspicion* rather than a bare "Not found" when
there was at least a weak signal. If no strategy produces any candidate at all, the field
comes back as `isMissing: true` with `confidenceLevel: "None"` — never a guessed value.

Confidence levels (`Scrapper.Utils.Constants.ScrapingLimits`): **High** ≥ 80, **Medium**
≥ 50, **Low** > 0, **None** = nothing found.

## Pluggable AI extraction (disabled by default)

`IAiExtractionProvider` is deliberately provider-agnostic — nothing in the engine
references a specific AI vendor. The default registration is
`NullAiExtractionProvider`, which always reports `IsConfigured = false`, so
`AiExtractionStrategy` is skipped entirely out of the box. To enable AI-assisted
extraction as the last-resort strategy, register a real implementation in
`ServiceCollectionExtensions.AddScrapperServices` in place of the null provider —
nothing else in the pipeline changes. A provider implementation receives one record's
cleaned text and the field names requested, and must return a value per field or `null`;
it is a controlled, single-field lookup, not open-ended page interpretation, and its
output still passes through the same validation and confidence scoring as every other
strategy.

## Example: what this looks like end-to-end (Doctify)

Given only:

```json
{ "fields": [{ "name": "Name" }, { "name": "Location" }, { "name": "Fees" }] }
```

against the live Doctify prolotherapy listing:

- **Name** → found via `SemanticHtml` (the card's heading), **High** confidence.
- **Location** → found via `Pattern` (postcode-anchored address text), **Medium**
  confidence.
- **Fees** → genuinely **not present** on this listing page (Doctify only shows pricing
  on individual specialist profile pages, not the search results) — correctly reported
  as "Not found" rather than inventing a number. Supplying an explicit selector in
  Advanced Options for the profile page would recover it if needed.

This was verified against the real, live site during development — not a fixture.
