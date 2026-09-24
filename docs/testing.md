# Testing

## Backend unit tests — `tests/Scrapper.Services.Tests`

xUnit + Moq + FluentAssertions, mirroring Clinflux's testing stack. 107 tests covering:

- **URL validation & SSRF** (`UrlSecurityHelperTests`, `UrlValidationServiceTests`) —
  valid public URLs, malformed URLs, unsupported protocols, localhost, private IPv4
  ranges (10.x, 172.16–31.x, 192.168.x, 169.254.x), and boundary cases just outside those
  ranges.
- **Explicit-selector HTML extraction** (`FieldExtractorTests`) — text/attribute/HTML/link
  extraction, repeated records, missing elements, empty values, invalid selectors (never
  throws), and XPath selectors — the advanced/manual path and Test Selector's engine.
- **HTTP handling** (`HtmlFetcherTests`, via a fake `HttpMessageHandler`) — success, 404,
  500, timeout, oversized response, malformed URL, private-IP rejection, and redirect
  following.
- **CSV export** (`CsvHelperTests`, `CsvExportServiceTests`) — normal data, missing
  fields, commas, quotes, newlines, Unicode, and empty datasets.
- **Field name → kind inference** (`FieldInferenceEngineTests`) — "Name"/"Full Name"/
  "Specialist" → `Name`, "Consultation Fee"/"Treatment Price"/"Cost"/"Pricing" → `Currency`,
  "Location"/"Address" → `Location`, plus email/phone/URL/rating, and the
  unrecognized-name fallback to `Text`.
- **Value plausibility validation** (`FieldValueValidatorTests`) — accepts `£150` /
  `john@example.com` / `Manchester` for their respective kinds, rejects `Manchester` as
  Currency and `£150` as Location.
- **Automatic record detection** (`RecordDetectorTests`) — finds repeated cards by
  tag/class/`data-testid` signature, returns null when there's no repeated structure,
  and ignores sparse/low-content groups (e.g. a row of nav links) in favor of genuinely
  content-rich cards.
- **Structured data parsing** (`StructuredDataParserTests`) — a single JSON-LD `Person`
  object, an `ItemList`/`itemListElement`-wrapped list flattened to individual entities,
  malformed JSON handled without throwing, and pages with no JSON-LD at all.
- **Extraction strategies** (`SemanticHtmlExtractionStrategyTests`,
  `PatternExtractionStrategyTests`) — heading-based name extraction, class/label-matched
  values, `<dt>`/`<dd>` pairs, currency/email/postcode-anchored-address pattern matches,
  and the "no candidate" case for each.
- **Orchestration** (`FieldExtractionOrchestratorTests`) — explicit `Type` skips
  inference, strategies are tried in priority order and stop at the first valid hit, a
  candidate that fails validation is downgraded to Low confidence rather than discarded,
  and "nothing found at all" maps to `isMissing: true`.
- **Engine orchestration** (`ScraperEngineTests`) — no-fields short-circuit, invalid-URL
  short-circuit (never calls the fetcher), fetch failures surfaced as errors, successful
  extraction via the automatic pipeline, and record-selector-not-found handling.

Run: `dotnet test tests/Scrapper.Services.Tests`

## Backend integration tests — `tests/Scrapper.Api.IntegrationTests`

`Microsoft.AspNetCore.Mvc.Testing`'s `WebApplicationFactory<Program>`, hitting the real
ASP.NET Core pipeline (routing, model binding, FluentValidation, the global exception
middleware) without a real network call for the SSRF cases. Covers: private-IP
validate-url response shape, malformed-URL 400 from the validator, missing-fields 400,
duplicate-field-name 400, a name-only field passing validation without a selector or
type, a blocked-URL scrape returning a graceful error (not an exception/stack trace), CSV
export content type, and selector-testing against a blocked URL.

Run: `dotnet test tests/Scrapper.Api.IntegrationTests`

## Frontend

`portal/npm run type-check` and `portal/npm run lint` cover the new scraper feature code
(`src/api`, `src/services`, `src/views/scraper`) alongside the existing portal.

## End-to-end tests — `tests/e2e` (Playwright, TypeScript)

Page-object model in `tests/pages/ScraperPage.ts`, shared fixture in
`tests/fixtures/base.ts`, run across three projects (`chromium` desktop, `tablet` — iPad,
`mobile-chrome` — Pixel 7):

- `field-modes.spec.ts` — the three configuration modes against the live Doctify page:
  name-only (automatic), name+type (semi-automatic), and name+type+selector
  (advanced/manual) all successfully scrape records.
- `scrape-flow.spec.ts` — the full flow: load the Doctify example, validate the URL,
  scrape it for real, and export the CSV (asserts the download actually happens).
- `invalid-url.spec.ts` — private-IP and malformed URLs surface a validation message
  instead of crashing; the Start Scraping button stays disabled with no fields configured.
- `missing-field.spec.ts` — a required field with a selector matching nothing, and
  separately a name-only field the automatic engine can't find on the page, both produce
  a clear "not found"/warning outcome and leave the app fully interactive (no crash, no
  fabricated value).
- `selector-testing.spec.ts` — testing an explicit selector (Advanced Options) against a
  live page reports a match count; testing one with no matches reports zero.
- `responsive.spec.ts` — the page renders without horizontal overflow and the field
  modal opens correctly at whatever viewport the project is running.

All 36 tests (12 specs × 3 viewport projects) were run and passed against the live
Doctify site during development. Run: `cd tests && npm test` (see
[local-setup.md](local-setup.md) for first-time setup). When running many specs in
parallel against a single shared local dev server, prefer `npx playwright test
--workers=1` to avoid resource contention with real network calls to the live test site;
CI environments spinning dedicated servers per run aren't affected.
