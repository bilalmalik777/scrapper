# Scrapper — Architecture

Scrapper is a generic, configuration-driven web scraper: a .NET backend engine plus a
CoreUI React/TypeScript portal. Nothing in the engine is specific to any one website —
site-specific behavior (which URL, which selectors, which fields) is entirely user
supplied configuration, not code.

This project mirrors the architectural conventions of the sibling reference project
`clinflux` (layered .NET solution, DTO/service/controller conventions, Serilog +
FluentValidation + global exception middleware, CoreUI portal with a thin fetch-based
API client) so the two codebases stay familiar to work in side by side. Multi-tenancy,
authentication, and billing — all specific to Clinflux's SaaS domain — were intentionally
left out, since Scrapper is a single-user local tool with no persistence requirements.

## Solution layout

```
Scrapper/
├── portal/                          Existing CoreUI React + TypeScript frontend
│   └── src/
│       ├── api/                     fetch-based API client + shared TS types
│       ├── services/                scraper.service.ts (feature API wrapper)
│       └── views/scraper/           ScraperPage, FieldEditorModal, ResultsPreview
├── src/
│   ├── Scrapper.Api                 ASP.NET Core Web API (controllers, DI, middleware)
│   ├── Scrapper.Models              DTOs, common types (ApiResponse, exceptions), enums
│   ├── Scrapper.Services            The scraping engine + supporting services
│   └── Scrapper.Utils               CSV helper, SSRF/URL-security helper, limits
├── tests/
│   ├── Scrapper.Services.Tests      xUnit unit tests for the engine
│   ├── Scrapper.Api.IntegrationTests  WebApplicationFactory-based API tests
│   ├── e2e/, pages/, fixtures/      Playwright end-to-end tests (TypeScript)
├── docs/                            This documentation
└── Scrapper.slnx
```

There is deliberately **no database** — a scrape is a single, stateless request/response:
the caller supplies the URL and field configuration, the engine fetches, extracts, and
returns results in one call. Nothing needs to be persisted between requests, so there's
no `Scrapper.Repository` layer or EF Core, unlike Clinflux.

## Request flow

```
Portal (ScraperPage)
  → POST /api/v1/scraper/validate-url    → IUrlValidationService
  → POST /api/v1/scraper/test-selector   → IHtmlFetcher + IFieldExtractor (explicit selector)
  → POST /api/v1/scraper/scrape          → IScraperEngine
                                              ├─ IUrlValidationService       (SSRF/scheme checks)
                                              ├─ IHtmlFetcher                (bounded HTTP fetch)
                                              ├─ IRecordDetector             (auto record structure)
                                              ├─ IStructuredDataParser       (JSON-LD, page-level)
                                              └─ IFieldExtractionOrchestrator (per field, per record)
                                                    ├─ SelectorExtractionStrategy
                                                    ├─ StructuredDataExtractionStrategy
                                                    ├─ SemanticHtmlExtractionStrategy
                                                    ├─ PatternExtractionStrategy
                                                    └─ AiExtractionStrategy (disabled by default)
  → POST /api/v1/scraper/export          → ICsvExportService
```

See [automatic-extraction.md](automatic-extraction.md) for the full detail on the
automatic (no-selector) pipeline — the normal path for a field that's just a name.

## Backend conventions (mirrors Clinflux)

- **DTOs** live in `Scrapper.Models/DTOs/<Feature>Dtos.cs`, one file per feature.
- **`ApiResponse<T>`** envelope wraps every successful response (`Common/ApiResponse.cs`).
- **Custom exceptions** (`Common/Exceptions.cs`) are mapped to HTTP status codes by
  `GlobalExceptionHandlingMiddleware`, which never leaks a raw stack trace to the client.
- **DI wiring** is explicit, one line per service, in
  `Scrapper.Api/Extensions/ServiceCollectionExtensions.cs` — no assembly scanning.
- **FluentValidation** validators live in `Scrapper.Api/Validators/`, one file per feature,
  auto-registered and auto-invoked before the controller action runs.
- **Serilog** writes to console + rolling file (`logs/scrapper-.log`), configured in
  `Program.cs` exactly as in Clinflux (bootstrap logger + `UseSerilog`).
- **Controllers** inherit `BaseApiController` (thin `Ok<T>`/`Fail<T>` helpers wrapping
  `ApiResponse<T>`), routed under `api/v1/[feature]`.

## Frontend conventions (mirrors Clinflux's portal)

- `api/client.ts` — hand-rolled `fetch` wrapper (no axios), same shape as Clinflux's:
  unwraps the `ApiResponse<T>` envelope, throws a typed `ApiError`, exposes `get`/`post`/`postBlob`.
- `services/scraper.service.ts` — one file per feature, mirrors Clinflux's
  `<feature>.service.ts` pattern.
- `views/scraper/` — a new CoreUI view added into the existing portal's routing
  (`routes.tsx`) and sidebar (`_nav.tsx`), reusing existing CoreUI components
  (`CCard`, `CTable`, `CModal`, `CForm`, `CAlert`, `CBadge`) — no new UI framework,
  no custom CSS beyond Bootstrap utility classes already used throughout the template.

See [scraping-engine.md](scraping-engine.md), [automatic-extraction.md](automatic-extraction.md),
[field-configuration.md](field-configuration.md), [security.md](security.md),
[csv-export.md](csv-export.md), and [local-setup.md](local-setup.md) for details on each
area, and [testing.md](testing.md) for how to run the test suites.
