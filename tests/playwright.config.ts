import { defineConfig, devices } from '@playwright/test'

const PORTAL_URL = process.env.PORTAL_URL || 'http://localhost:3000'
const API_URL = process.env.API_URL || 'https://localhost:7098'

export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  retries: process.env.CI ? 2 : 0,
  workers: process.env.CI ? 2 : undefined,
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL: PORTAL_URL,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
    ignoreHTTPSErrors: true,
  },
  projects: [
    { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
    { name: 'tablet', use: { ...devices['iPad (gen 7)'] } },
    { name: 'mobile-chrome', use: { ...devices['Pixel 7'] } },
  ],
  webServer: [
    {
      command: 'npm --prefix ../portal run start',
      url: PORTAL_URL,
      reuseExistingServer: !process.env.CI,
      timeout: 60_000,
    },
    {
      command: 'dotnet run --project ../src/Scrapper.Api --urls ' + API_URL,
      url: API_URL,
      reuseExistingServer: !process.env.CI,
      timeout: 60_000,
      ignoreHTTPSErrors: true,
    },
  ],
})
