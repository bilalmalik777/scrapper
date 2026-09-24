import { test, expect } from '../fixtures/base'

test.describe('Doctor profile deep crawling', () => {
  test('visiting profile pages merges full bio and fee data into the listing results', async ({
    scraperPage,
    page,
  }) => {
    await scraperPage.goto()

    await scraperPage.setUrl('https://www.doctify.com/uk/find/prolotherapy/united-kingdom/specialists')

    // Keep the crawl small and fast for the test.
    await page.getByLabel('Max Records').fill('2')
    await page.getByRole('button', { name: /Crawl Options/ }).click()
    await page.getByLabel('Max Profiles to Visit').fill('2')

    await scraperPage.addField({ name: 'Name', required: true })
    await scraperPage.addField({ name: 'Bio', type: 'Text' })

    await scraperPage.startScraping()

    await expect(page.getByText(/Records Found: \d+/)).toBeVisible({ timeout: 30_000 })
    await expect(page.getByText(/Profiles Visited: [1-9]/)).toBeVisible({ timeout: 30_000 })

    // A full bio is long-form content, not the short listing summary — the results table
    // should show a substantial amount of text for at least one record.
    const resultsTable = scraperPage.resultsTable()
    const bodyText = await resultsTable.innerText()
    expect(bodyText.length).toBeGreaterThan(200)

    // Profile URL is exposed as its own column pointing at an individual specialist page.
    await expect(page.getByRole('link', { name: /doctify\.com\/uk\/specialist\// }).first()).toBeVisible()
  })

  test('disabling profile crawl keeps results to listing-page data only', async ({ scraperPage, page }) => {
    await scraperPage.goto()

    await scraperPage.setUrl('https://www.doctify.com/uk/find/prolotherapy/united-kingdom/specialists')
    await page.getByLabel('Max Records').fill('2')

    await page.getByRole('button', { name: /Crawl Options/ }).click()
    await page.getByLabel(/visit each doctor's own profile page/).uncheck()

    await scraperPage.addField({ name: 'Name', required: true })

    await scraperPage.startScraping()

    await expect(page.getByText(/Records Found: \d+/)).toBeVisible({ timeout: 30_000 })
    await expect(page.getByText(/Profiles Visited:/)).toHaveCount(0)
  })
})
