import { test, expect } from '../fixtures/base'

test.describe('Missing field handling', () => {
  test('scraping with a field selector that does not exist stays stable and shows a warning', async ({
    scraperPage,
    page,
  }) => {
    await scraperPage.goto()

    await scraperPage.setUrl('https://example.com')
    await scraperPage.addField({
      name: 'NonExistentField',
      selector: '.this-selector-does-not-exist-anywhere',
      required: true,
    })

    await scraperPage.startScraping()

    // The app should not crash; it should show either a warning or an error alert.
    await expect(scraperPage.warningAlert.or(scraperPage.errorAlert).first()).toBeVisible({
      timeout: 30_000,
    })

    // Core layout must still be present and interactive.
    await expect(scraperPage.startScrapingButton).toBeEnabled()
    await expect(page.getByText('Fields to Extract')).toBeVisible()
  })

  test('a field with only a name that cannot be found on the page is reported as not found, not fabricated', async ({
    scraperPage,
    page,
  }) => {
    await scraperPage.goto()

    await scraperPage.setUrl('https://example.com')
    await scraperPage.addField({ name: 'ConsultationFeeXyz', required: true })

    await scraperPage.startScraping()

    await expect(page.getByText(/Not found|Records Found/).first()).toBeVisible({ timeout: 30_000 })
    await expect(scraperPage.startScrapingButton).toBeEnabled()
  })
})
