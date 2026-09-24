# CSV Export

`POST /api/v1/scraper/export` takes the field names and records already shown in the
results preview and returns a `text/csv` file — export is a separate, explicit step from
scraping, so results are always reviewed in the UI before anything is downloaded.

Implemented in `Scrapper.Utils.Helpers.CsvHelper`, used by
`Scrapper.Services.Implementations.CsvExportService`:

- **UTF-8 with BOM** — the file starts with the UTF-8 byte-order mark so Excel and other
  tools that sniff encoding open Unicode content (accented names, non-Latin scripts)
  correctly instead of mangling it.
- **Header row** — one column per configured field name, in the order they were defined.
- **One row per record** — a record missing a given field renders as an empty cell, not
  an error.
- **RFC 4180 quoting** — any value containing a comma, a double quote, or a newline is
  wrapped in double quotes, with internal double quotes doubled (`"` → `""`).
- **Multiline values** — preserved inside quoted cells rather than being flattened or
  truncated.

See `tests/Scrapper.Services.Tests/CsvHelperTests.cs` and `CsvExportServiceTests.cs` for
the full behavioral test coverage (normal data, missing fields, commas, quotes, newlines,
Unicode, and the empty-dataset case).

In the portal, clicking **Export CSV** on the Web Scraper page downloads the file via the
browser's normal download flow (an in-memory `Blob` + a temporary `<a download>` link) —
no data is written to disk on the server.
