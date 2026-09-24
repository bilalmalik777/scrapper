import { test, expect } from '../fixtures/base'

test.describe('Complete scraping flow', () => {
  test('user can configure fields, scrape a live URL, and export CSV', async ({ scraperPage, page }) => {
    await scraperPage.goto()

    await scraperPage.loadExampleButton.click()

    await expect(scraperPage.urlInput).toHaveValue(/doctify\.com/)

    await scraperPage.validateUrl()
    await expect(page.getByText('URL is reachable and allowed.')).toBeVisible({ timeout: 15_000 })

    await scraperPage.startScraping()

    await expect(page.getByText(/Records Found: \d+/)).toBeVisible({ timeout: 30_000 })

    const recordsText = await page.getByText(/Records Found: \d+/).textContent()
    expect(recordsText).toMatch(/Records Found: [1-9]/)

    await expect(scraperPage.exportCsvButton).toBeVisible()

    const downloadPromise = page.waitForEvent('download')
    await scraperPage.exportCsvButton.click()
    const download = await downloadPromise

    expect(download.suggestedFilename()).toMatch(/scrape-export-.*\.csv/)
  })
})
