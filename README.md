# Scrapper

A generic web scraper: type a URL and the field names you want (e.g. "Name", "Location",
"Fees") and click Scrape. No CSS selector, XPath, or HTML knowledge required — the engine
automatically detects the repeated record structure and finds each field using
structured data, semantic HTML, and pattern matching, with a confidence level shown for
every value. An optional **Type** narrows detection, and an optional **Advanced**
selector always wins when supplied — see
[docs/automatic-extraction.md](docs/automatic-extraction.md).

- Backend: ASP.NET Core (.NET 10), layered to mirror the sibling `clinflux` project
  (`src/Scrapper.Api`, `Scrapper.Services`, `Scrapper.Models`, `Scrapper.Utils`).
- Frontend: the existing CoreUI React + TypeScript `portal`, extended with a Web Scraper
  page (`portal/src/views/scraper`).
- Initial test case: Doctify's UK prolotherapy specialist listing, wired up as an
  in-app example — see [docs/scraping-engine.md](docs/scraping-engine.md).

See [docs/architecture.md](docs/architecture.md) for the full architecture, and
[docs/local-setup.md](docs/local-setup.md) to get running locally.

## Quick start

```bash
# Terminal 1 — backend
cd src/Scrapper.Api && dotnet run

# Terminal 2 — frontend
cd portal && npm install && npm run start
```

Then open `http://localhost:3000` and click **Load Doctify Example**, or type your own
URL and field names.

## Documentation

- [Architecture](docs/architecture.md)
- [Scraping engine](docs/scraping-engine.md)
- [Automatic field extraction](docs/automatic-extraction.md)
- [Doctor/profile deep crawling](docs/deep-crawl.md)
- [Field configuration](docs/field-configuration.md)
- [Security (SSRF & limits)](docs/security.md)
- [CSV export](docs/csv-export.md)
- [Local setup](docs/local-setup.md)
- [Testing](docs/testing.md)
