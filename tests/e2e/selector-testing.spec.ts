import { test, expect } from '../fixtures/base'

test.describe('Selector testing', () => {
  test('testing a selector against a real page shows the number of matching elements', async ({
    scraperPage,
    page,
  }) => {
    await scraperPage.goto()

    await scraperPage.setUrl('https://www.doctify.com/uk/find/prolotherapy/united-kingdom/specialists')

    const modal = await scraperPage.testSelectorInModal('[data-testid="search-result-card-full-name"]')

    await expect(modal.getByText(/Matches found:/)).toBeVisible({ timeout: 20_000 })
  })

  test('testing a selector with no matches reports zero results', async ({ scraperPage }) => {
    await scraperPage.goto()

    await scraperPage.setUrl('https://example.com')
    const modal = await scraperPage.testSelectorInModal('.definitely-not-a-real-class-xyz')

    await expect(modal.getByText('Matches found: 0')).toBeVisible({ timeout: 20_000 })
  })
})
