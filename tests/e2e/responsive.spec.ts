import { test, expect } from '../fixtures/base'

test.describe('Responsive layout', () => {
  test('scraper page renders and is usable at the current viewport', async ({ scraperPage, page }) => {
    await scraperPage.goto()

    await expect(scraperPage.urlInput).toBeVisible()
    await expect(scraperPage.addFieldButton).toBeVisible()

    // No horizontal overflow at the current viewport.
    const hasHorizontalScroll = await page.evaluate(
      () => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1,
    )
    expect(hasHorizontalScroll).toBeFalsy()

    await scraperPage.addFieldButton.click()
    await expect(page.getByRole('dialog')).toBeVisible()
  })
})
