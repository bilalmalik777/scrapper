import { test, expect } from '../fixtures/base'

test.describe('Field configuration modes', () => {
  test('automatic mode: name-only fields scrape a live listing page without any selector', async ({
    scraperPage,
    page,
  }) => {
    await scraperPage.goto()

    await scraperPage.setUrl('https://www.doctify.com/uk/find/prolotherapy/united-kingdom/specialists')
    await scraperPage.addField({ name: 'Name', required: true })
    await scraperPage.addField({ name: 'Location' })

    await scraperPage.startScraping()

    await expect(page.getByText(/Records Found: [1-9]/)).toBeVisible({ timeout: 30_000 })
    await expect(page.getByText('High confidence').first()).toBeVisible()
  })

  test('semi-automatic mode: name + type narrows extraction without a selector', async ({
    scraperPage,
    page,
  }) => {
    await scraperPage.goto()

    await scraperPage.setUrl('https://www.doctify.com/uk/find/prolotherapy/united-kingdom/specialists')
    await scraperPage.addField({ name: 'Specialist', type: 'Name', required: true })

    await scraperPage.startScraping()

    await expect(page.getByText(/Records Found: [1-9]/)).toBeVisible({ timeout: 30_000 })
  })

  test('advanced/manual mode: name + type + explicit selector still works', async ({ scraperPage, page }) => {
    await scraperPage.goto()

    await scraperPage.setUrl('https://www.doctify.com/uk/find/prolotherapy/united-kingdom/specialists')
    await scraperPage.addField({
      name: 'Name',
      type: 'Name',
      selector: '[data-testid="search-result-card-full-name"]',
      required: true,
    })

    await scraperPage.startScraping()

    await expect(page.getByText(/Records Found: [1-9]/)).toBeVisible({ timeout: 30_000 })
  })
})
