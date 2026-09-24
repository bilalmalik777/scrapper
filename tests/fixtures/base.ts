import { test as base } from '@playwright/test'
import { ScraperPage } from '../pages/ScraperPage'

interface Fixtures {
  scraperPage: ScraperPage
}

export const test = base.extend<Fixtures>({
  scraperPage: async ({ page }, use) => {
    const scraperPage = new ScraperPage(page)
    await use(scraperPage)
  },
})

export { expect } from '@playwright/test'
