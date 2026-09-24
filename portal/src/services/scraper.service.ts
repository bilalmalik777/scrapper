import { api } from '../api/client'
import type {
  ExportRequest,
  ScraperConfig,
  ScrapeResult,
  TestSelectorRequest,
  TestSelectorResponse,
  ValidateUrlResponse,
} from '../api/types'

export const scraperApi = {
  validateUrl: (url: string) => api.post<ValidateUrlResponse>('/scraper/validate-url', { url }),
  testSelector: (request: TestSelectorRequest) =>
    api.post<TestSelectorResponse>('/scraper/test-selector', request),
  scrape: (config: ScraperConfig) => api.post<ScrapeResult>('/scraper/scrape', config),
  exportCsv: (request: ExportRequest) => api.postBlob('/scraper/export', request),
}
