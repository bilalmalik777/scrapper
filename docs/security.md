# Security

Every URL and selector submitted to the API is treated as untrusted input.

## SSRF protection

`Scrapper.Utils.Helpers.UrlSecurityHelper` and `Scrapper.Services.Implementations.HtmlFetcher`
enforce, on every request (validate-url, test-selector, scrape) and on **every redirect
hop**, not just the initial URL:

- Only `http`/`https` schemes are allowed (`file://`, `ftp://`, `javascript:`, etc. are
  rejected).
- `localhost`, `127.0.0.1`, `0.0.0.0`, `::1`, and hostnames ending in `.local`/`.internal`
  are rejected outright.
- Literal private/reserved IPv4 ranges are rejected: `10.0.0.0/8`, `172.16.0.0/12`,
  `192.168.0.0/16`, `169.254.0.0/16` (link-local, including the cloud metadata address
  `169.254.169.254`), and loopback.
- IPv6 link-local, site-local, and unique-local (`fc00::/7`) ranges are rejected.
- Hostnames are resolved via DNS and **every resolved address** is checked against the
  same private/reserved ranges before a connection is made, so a public-looking hostname
  that resolves to an internal address (DNS rebinding) is also blocked.
- Redirects are followed manually (not via `HttpClient`'s built-in auto-redirect) so each
  hop's target is re-validated before it's fetched, capped at 5 hops.

## Resource limits

Enforced server-side (see `Scrapper.Utils.Constants.ScrapingLimits`), independent of
whatever the client requests:

- Response size capped at 10 MB (checked against `Content-Length` up front, and against
  actual bytes read as a hard backstop for chunked/unknown-length responses).
- Request timeout capped at 30 seconds.
- Records per scrape capped at 1000.
- Fields per configuration capped at 50.

## What this scraper will not do

Per design, the engine never attempts to:

- Bypass authentication, paywalls, or access controls.
- Solve or work around CAPTCHAs.
- Evade legitimate bot-detection mechanisms beyond sending a standard browser-like
  `User-Agent` header (the same thing any real browser, and most legitimate scraping
  tools, send by default — this is not credential/CAPTCHA bypass).

If a target site returns `403`/blocks the request outright, the engine surfaces this as a
clean, user-facing error ("The server responded with an error...") rather than trying to
work around it.

## No raw exceptions reach the client

`Scrapper.Api.Middleware.GlobalExceptionHandlingMiddleware` catches every unhandled
exception and maps it to an RFC 7807 `ProblemDetails` response with a safe, human-readable
message. In non-Development environments, unexpected (500-level) exceptions have their
real message replaced with a generic message; only a `traceId` is exposed for correlating
with server-side logs (Serilog, `logs/scrapper-.log`). Expected failure cases (bad URL,
selector with no matches, blocked SSRF target, HTTP error from the target site) are
caught explicitly in the service layer and turned into `errors`/`warnings` entries in the
response payload rather than exceptions at all.
