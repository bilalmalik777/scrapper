# Local Setup

Requirements: .NET 10 SDK, Node.js 20+, npm.

The project lives at `C:\Users\user\source\Scrapper` in this environment, but nothing in
the application code depends on that path — it's just where the repository happens to be
checked out locally.

## 1. Start the backend

```bash
cd src/Scrapper.Api
dotnet run
```

By default this listens on `https://localhost:7098` and `http://localhost:5186`
(`src/Scrapper.Api/Properties/launchSettings.json`). Logs are written to the console and
to `src/Scrapper.Api/logs/scrapper-.log`.

## 2. Start the portal

```bash
cd portal
npm install   # first time only
npm run start
```

Vite serves the portal at `http://localhost:3000`. The API base URL is read from
`portal/.env` (`VITE_API_BASE_URL`, defaults to `https://localhost:7098/api/v1`) — update
this if you run the backend on a different port.

Open `http://localhost:3000/#/scraper` (or click **Web Scraper** in the sidebar nav) to
use the tool.

## 3. Try it out

Click **Load Doctify Example** on the Web Scraper page to populate a working
configuration against the initial test case
(`https://www.doctify.com/uk/find/prolotherapy/united-kingdom/specialists`), then
**Validate URL** → **Start Scraping** → **Export CSV**. See
[field-configuration.md](field-configuration.md) for building your own configuration
against a different site.

## Running the test suites

```bash
# Backend unit + integration tests
dotnet test Scrapper.slnx

# Frontend type-check and lint
cd portal
npm run type-check
npm run lint

# End-to-end tests (Playwright)
cd tests
npm install               # first time only
npx playwright install    # first time only, installs browser binaries
npm test
```

The Playwright config (`tests/playwright.config.ts`) will start the portal and API
servers for you (`webServer` entries) if they aren't already running. If you already
have both running locally (as in steps 1–2 above), Playwright reuses them
(`reuseExistingServer: !process.env.CI`).

See [testing.md](testing.md) for what each test suite covers.

## Building for production

```bash
dotnet publish src/Scrapper.Api -c Release
cd portal && npm run build   # outputs to portal/build
```

No deployment configuration is included — this is a local-first tool. Serve the built
`portal/build` output with any static file server and point it at your API deployment.
