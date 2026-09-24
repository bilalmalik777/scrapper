# Field Configuration

The minimal, and normal, way to configure a scrape is just a URL and field names:

```jsonc
{
  "url": "https://www.doctify.com/uk/find/prolotherapy/united-kingdom/specialists",
  "maxRecords": 200,
  "timeoutSeconds": 20,
  "fields": [
    { "name": "Name", "required": true },
    { "name": "Location" },
    { "name": "Fees" }
  ]
}
```

No selector, no record selector, no extraction type. The engine infers each field's kind
from its name and automatically detects the repeated record structure on the page — see
[automatic-extraction.md](automatic-extraction.md) for how. This exact configuration is
wired up as the **Load Doctify Example** button in the portal's Web Scraper page
(`portal/src/views/scraper/ScraperPage.tsx`) and was verified against the live site
during development.

## The three configuration modes

`FieldDefinitionDto` requires only `Name`. `Type` and `Selector` are independent, optional
escalations you can apply per field:

```jsonc
// Automatic — just a name
{ "name": "Fees" }

// Semi-automatic — name + type, still no selector
{ "name": "Fees", "type": "Currency" }

// Advanced/manual — explicit selector always wins when present
{
  "name": "Fees",
  "type": "Currency",
  "selector": "[data-testid='price']",
  "selectorType": "Css",
  "extractionType": "Text"
}
```

Supplying `Type` without a selector doesn't change *where* the engine looks — it still
runs the automatic strategies — but it skips name-based inference and narrows which
value-shape patterns and structured-data paths are tried, which helps when a field's name
doesn't clearly indicate its kind (e.g. naming a price field "Investment").

Available kinds: `Text`, `Name`, `Location`, `Currency`, `Email`, `Phone`, `Url`,
`Rating`, `Number` (or omit `Type` entirely for automatic inference).

## In the portal UI

The **Fields to Extract** card on the Web Scraper page lets you:

- **Add Field** — opens a modal asking only for a **Field Name** and an optional **Type**
  (defaulting to "Auto Detect") and a **Required** checkbox.
- **Advanced Options** (collapsed by default) — for technical users who want to override
  automatic detection for a specific field: CSS/XPath selector, extraction type, and
  attribute name. Supplying a selector here forces the explicit/manual extraction path
  for that one field only; every other field keeps working automatically.
- **Test Selector** — inside Advanced Options, runs the selector against the currently
  entered URL without doing a full scrape, showing the match count and up to 5 sample
  values.
- **Edit** (pencil icon) / **Delete** (trash icon) — per-field actions in the field table.
- **Configure selector** — shown next to any result cell at Low confidence or "Not
  found"; jumps straight into that field's Advanced Options so you can supply a precise
  selector only where automatic detection struggled, without touching your other fields.

## Required vs optional fields

A field marked **Required** that comes back missing (no strategy found any value at all)
for a given record is reported in that record's `missingFields` list *and* raises a
scrape-level warning ("Required field 'X' could not be extracted for one or more
records."). An optional field that's missing is still listed in `missingFields` (shown as
*Not found* in the results table) but does not raise a warning — this lets you scrape
pages where some fields are only present on some records, or aren't present at all on a
listing page (see the Fees example in
[automatic-extraction.md](automatic-extraction.md#example-what-this-looks-like-end-to-end-doctify)),
without flooding the warnings list.
