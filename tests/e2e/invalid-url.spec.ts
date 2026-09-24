import { test, expect } from '../fixtures/base'

test.describe('Invalid URL handling', () => {
  test('validating a private/internal URL shows a validation error, not a crash', async ({ scraperPage, page }) => {
    await scraperPage.goto()

    await scraperPage.setUrl('http://192.168.1.1')
    await scraperPage.validateUrl()

    await expect(page.getByText(/not allowed|not valid/i)).toBeVisible({ timeout: 15_000 })
  })

  test('validating a malformed URL shows a validation error', async ({ scraperPage, page }) => {
    await scraperPage.goto()

    await scraperPage.setUrl('not-a-real-url')
    await scraperPage.validateUrl()

    await expect(page.getByText(/not allowed|not valid|malformed/i)).toBeVisible({ timeout: 15_000 })
  })

  test('scraping without any fields configured keeps the Start Scraping button disabled', async ({ scraperPage }) => {
    await scraperPage.goto()
    await scraperPage.setUrl('https://example.com')

    await expect(scraperPage.startScrapingButton).toBeDisabled()
  })
})
