import { Page, Locator, expect } from '@playwright/test'

export class ScraperPage {
  readonly page: Page
  readonly urlInput: Locator
  readonly validateUrlButton: Locator
  readonly startScrapingButton: Locator
  readonly addFieldButton: Locator
  readonly exportCsvButton: Locator
  readonly loadExampleButton: Locator

  constructor(page: Page) {
    this.page = page
    this.urlInput = page.getByPlaceholder('https://example.com/listing-page')
    this.validateUrlButton = page.getByRole('button', { name: 'Validate URL' })
    this.startScrapingButton = page.getByRole('button', { name: 'Start Scraping' })
    this.addFieldButton = page.getByRole('button', { name: 'Add Field' })
    this.exportCsvButton = page.getByRole('button', { name: 'Export CSV' })
    this.loadExampleButton = page.getByRole('button', { name: 'Load Doctify Example' })
  }

  async goto() {
    await this.page.goto('/')

    const scraperLink = this.page.getByRole('link', { name: 'Web Scraper' })
    try {
      await scraperLink.click({ timeout: 3_000 })
    } catch {
      // On tablet/mobile viewports the sidebar starts off-canvas behind a hamburger toggle.
      await this.page.locator('button.header-toggler').first().click()
      await scraperLink.click()
    }
    await expect(this.page.getByText('Fields to Extract')).toBeVisible()
  }

  async setUrl(url: string) {
    await this.urlInput.fill(url)
  }

  async validateUrl() {
    await this.validateUrlButton.click()
  }

  /**
   * Adds a field. `selector` is optional — omit it to exercise fully automatic extraction
   * (name-only or name+type mode); supply it to exercise the advanced/manual fallback.
   */
  async addField(field: {
    name: string
    type?: 'Text' | 'Name' | 'Location' | 'Currency' | 'Email' | 'Phone' | 'Url' | 'Rating' | 'Number'
    selector?: string
    extractionType?: 'Text' | 'Attribute' | 'Html' | 'Link'
    attribute?: string
    required?: boolean
  }) {
    await this.addFieldButton.click()
    const modal = this.page.getByRole('dialog')
    await modal.locator('#field-name').fill(field.name)

    if (field.type) {
      await modal.locator('#field-type').selectOption(field.type)
    }

    await modal.locator('#field-required').setChecked(!!field.required)

    if (field.selector) {
      const advancedToggle = modal.getByRole('button', { name: /Advanced Options/ })
      if (!(await modal.locator('#field-selector').isVisible())) {
        await advancedToggle.click()
      }

      if (field.extractionType) {
        await modal.locator('#field-extraction-type').selectOption(field.extractionType)
      }

      await modal.locator('#field-selector').fill(field.selector)

      if (field.attribute) {
        await modal.locator('#field-attribute').fill(field.attribute)
      }
    }

    await modal.getByRole('button', { name: 'Save Field' }).click()
    await expect(modal).toBeHidden()
  }

  async testSelectorInModal(selector: string) {
    await this.addFieldButton.click()
    const modal = this.page.getByRole('dialog')
    await modal.getByRole('button', { name: /Advanced Options/ }).click()
    await modal.locator('#field-selector').fill(selector)
    await modal.getByRole('button', { name: 'Test Selector' }).click()
    return modal
  }

  async startScraping() {
    await this.startScrapingButton.click()
  }

  resultsTable() {
    return this.page.locator('table').last()
  }

  get errorAlert() {
    return this.page.locator('.alert-danger')
  }

  get warningAlert() {
    return this.page.locator('.alert-warning')
  }
}
