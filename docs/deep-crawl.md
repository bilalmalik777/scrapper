# Doctor/Specialist Profile Deep Crawling

When a listing page only shows a summary per record (name, rating, a short blurb), the
scraper can automatically discover each record's own detail/profile page, visit it, and
merge in whatever the listing didn't show (a full bio, qualifications, registration
details, ...). This is on by default (`enableProfileCrawl: true`) and needs no selectors.

## How it works

```
Listing URL
  → detect records (existing automatic record detection)
  → for each record: IProfileLinkDetector.DetectProfileUrl(record, pageUri)
       finds the link that wraps/contains the record's own heading (or, failing that,
       the first substantial, non-pagination-looking link) — same-host only
  → for each unique discovered profile URL (deduplicated):
       fetch it through the same IHtmlFetcher used for the listing page itself
       (same timeout/size/redirect/SSRF protections, no special-casing)
       run the full extraction pipeline against it as a single-record page
  → merge: a field found on the profile page overrides the listing-page value for
    that field; a field missing on the profile keeps whatever the listing had (or
    stays missing if neither source had it)
  → optionally follow "next page" pagination and repeat, up to MaxPages
```

Implemented in `RecordDetector` → `Scrapper.Services.Implementations.ProfileLinkDetector`
(link + pagination discovery) and `ScraperEngine.CrawlAsync`/`TryCrawlProfileAsync`
(orchestration). Nothing here is Doctify-specific — `ProfileLinkDetector` has no
knowledge of any particular site's markup.

## Configuration (`ScraperConfigDto`)

| Field | Default | Meaning |
|---|---|---|
| `enableProfileCrawl` | `true` | Turn profile crawling off to scrape only the listing page. |
| `maxProfiles` | `50` | Hard cap on how many individual profile pages one scrape will visit (server hard cap: 200). |
| `maxPages` | `1` | How many listing pages to follow via pagination (server hard cap: 20). `1` = no pagination. |

## Safety

Profile URLs go through exactly the same `IUrlValidationService`/`IHtmlFetcher` path as
the listing URL — SSRF/private-IP checks, scheme restriction, timeout, response-size cap,
and redirect cap all apply per profile fetch, not just to the first request.
Additionally:

- **Same-host only**: `ProfileLinkDetector` only considers links whose host matches the
  listing page's host (exact host match, not full public-suffix/eTLD+1 parsing — a
  documented simplification). A link to a different domain is never followed.
- **Deduplication**: profile URLs are tracked in a set across the whole crawl (including
  across paginated listing pages) — the same profile is never fetched twice even if
  multiple listing records happen to link to it.
- **Never fatal**: a blocked, unreachable, or slow profile page produces a warning
  ("Skipped the profile page for 'X': ...") and that record simply keeps its listing-page
  data — it never aborts the whole scrape.

## Pagination

Only **link-based** pagination is supported: a `rel="next"` link/`<link rel="next">`, or
an anchor whose text/aria-label looks like "Next"/"»"/"Next page". A "Load more" button
that fetches content via JavaScript with no underlying page link **is not supported** —
this engine fetches static HTML per request and does not execute JavaScript.

## Known limitations

- **No live streaming progress.** The scrape is a single request/response; the portal
  shows a static "crawling..." message while it runs and the final `pagesProcessed`/
  `profilesVisited` counts once done, not incremental step-by-step progress
  ("Finding doctors... 12 found" while still running). Building true real-time progress
  would need a streaming or polling architecture (SSE/WebSocket + a job-status endpoint)
  — a larger change than this pass covers.
- **Bio/long-text extraction quality varies by page.** `FieldKind.Biography` prefers the
  richest labelled section and joins paragraphs, but on pages with very little semantic
  markup it may include some surrounding boilerplate (e.g. a skills list) alongside the
  actual biography text. It is validated for plausible length and always shown with its
  confidence level — never fabricated — but isn't guaranteed to be perfectly trimmed.
