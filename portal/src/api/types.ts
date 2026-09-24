export interface ApiResponse<T> {
  success: boolean
  data: T
  message?: string | null
  errors: string[]
}

export type SelectorType = 'Css' | 'XPath'
export type ExtractionType = 'Text' | 'Attribute' | 'Html' | 'Link'

export type FieldKind =
  | 'Auto'
  | 'Text'
  | 'Name'
  | 'Location'
  | 'Currency'
  | 'Email'
  | 'Phone'
  | 'Url'
  | 'Rating'
  | 'Number'
  | 'Biography'

export type ExtractionSource =
  | 'None'
  | 'Selector'
  | 'StructuredData'
  | 'SemanticHtml'
  | 'Pattern'
  | 'Ai'
export type ConfidenceLevel = 'None' | 'Low' | 'Medium' | 'High'

export interface FieldDefinition {
  name: string
  /** Optional — when omitted the field's kind is inferred automatically from its name. */
  type?: FieldKind | null
  required: boolean

  /** Advanced/manual fallback — all optional. Supplying a selector forces explicit extraction. */
  selector?: string | null
  selectorType: SelectorType
  extractionType: ExtractionType
  attribute?: string | null
}

export interface ScraperConfig {
  url: string
  /** Optional — when omitted the engine auto-detects the repeated record/card structure. */
  recordSelector?: string | null
  recordSelectorType?: SelectorType
  fields: FieldDefinition[]
  maxRecords: number
  timeoutSeconds: number

  /** Automatically discover and visit each record's own profile/detail page. Defaults to true. */
  enableProfileCrawl?: boolean
  /** Maximum number of individual profile pages to visit in one scrape. */
  maxProfiles?: number
  /** Maximum number of listing pages to follow via pagination (1 = no pagination). */
  maxPages?: number
  /**
   * Renders each page in a headless browser before extracting fields, for sites whose content
   * only exists after client-side JavaScript runs. Off by default — slower/heavier, and
   * unnecessary for most sites.
   */
  enableJavaScriptRendering?: boolean
}

export interface ValidateUrlRequest {
  url: string
}

export interface ValidateUrlResponse {
  isValid: boolean
  reason?: string | null
  normalizedUrl?: string | null
}

export interface TestSelectorRequest {
  url: string
  selectorType: SelectorType
  selector: string
  extractionType: ExtractionType
  attribute?: string | null
  recordSelector?: string | null
  recordSelectorType?: SelectorType
}

export interface TestSelectorResponse {
  matchCount: number
  samplePreviews: string[]
  warning?: string | null
}

export interface FieldResult {
  value: string | null
  resolvedType: FieldKind
  source: ExtractionSource
  confidence: number
  confidenceLevel: ConfidenceLevel
  isMissing: boolean
}

export interface ScrapedRecord {
  fields: Record<string, string | null>
  missingFields: string[]
  fieldResults: Record<string, FieldResult>
  profileUrl?: string | null
  profileVisited: boolean
  /** Which listing page (1-based) this record was found on. */
  pageNumber: number
}

export interface ScrapeResult {
  totalRecords: number
  records: ScrapedRecord[]
  warnings: string[]
  errors: string[]
  fieldNames: string[]
  recordSelectorAutoDetected: boolean
  pagesProcessed: number
  profilesVisited: number
}

export type CrawlJobStatus = 'NotStarted' | 'Running' | 'Paused' | 'Blocked' | 'Completed' | 'Failed'

export interface PagedCrawlConfig {
  url: string
  recordSelector?: string | null
  recordSelectorType?: SelectorType
  fields: FieldDefinition[]
  totalPages: number
  enableProfileCrawl?: boolean
  maxProfiles?: number
  timeoutSeconds?: number
  /** Sent as-is for the whole crawl — never rotated mid-crawl. Leave blank for a normal default. */
  userAgent?: string | null
  minDelaySeconds: number
  maxDelaySeconds: number
  longPauseEveryPages?: number
  longPauseSeconds?: number
  maxRetriesPerPage?: number
  retryBackoffBaseSeconds?: number
  retryBackoffMaxSeconds?: number
  /** An explicit HTTP 403 always stops the crawl regardless of this setting. */
  stopOnPersistentFailure?: boolean
}

export interface StartCrawlJobRequest {
  jobId?: string | null
  config: PagedCrawlConfig
  /** Discards any existing state/records for this job id and starts over from page 1. */
  fresh?: boolean
}

export interface CrawlJobState {
  jobId: string
  config: PagedCrawlConfig
  status: CrawlJobStatus
  totalPages: number
  lastCompletedPage: number
  currentPage?: number | null
  failedPages: number[]
  recordsSaved: number
  lastError?: string | null
  fieldNames: string[]
  createdAt: string
  updatedAt: string
}

export interface CrawlJobRecordsResponse {
  fieldNames: string[]
  records: ScrapedRecord[]
}

export interface ExportRequest {
  fieldNames: string[]
  records: ScrapedRecord[]
  /** Export one CSV per listing page, packaged as a single .zip (each entry named "{page}.csv"). */
  groupByPage?: boolean
}
