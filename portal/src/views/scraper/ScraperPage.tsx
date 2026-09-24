import React, { useMemo, useState } from 'react'
import {
  CAlert,
  CBadge,
  CButton,
  CCard,
  CCardBody,
  CCardHeader,
  CCol,
  CCollapse,
  CForm,
  CFormCheck,
  CFormInput,
  CFormLabel,
  CInputGroup,
  CRow,
  CSpinner,
  CTable,
  CTableBody,
  CTableDataCell,
  CTableHead,
  CTableHeaderCell,
  CTableRow,
} from '@coreui/react-pro'
import CIcon from '@coreui/icons-react'
import { cilPencil, cilPlus, cilTrash } from '@coreui/icons'
import FieldEditorModal from './FieldEditorModal'
import ResultsPreview from './ResultsPreview'
import { scraperApi } from '../../services/scraper.service'
import { ApiError } from '../../api/client'
import type { FieldDefinition, ScraperConfig, ScrapedRecord, ScrapeResult } from '../../api/types'

// Kept in sync with the backend's ScrapingLimits — a plain <input type="number" max="30">
// only hints at the limit in the browser's spinner UI, it doesn't stop someone from typing
// (or pasting) a larger value, so the value is clamped here before it's ever sent.
const MIN_TIMEOUT_SECONDS = 1
const MAX_TIMEOUT_SECONDS = 30

const field = (
  name: string,
  type?: FieldDefinition['type'],
  required = false,
): FieldDefinition => ({
  name,
  type: type ?? null,
  required,
  selectorType: 'Css',
  extractionType: 'Text',
})

// Sensible defaults for a doctor/specialist profile scrape — the user can still add,
// edit, or remove any of these; nothing here is mandatory.
// The engine joins multiple locations for one record with "; " (see PatternExtractionStrategy
// / StructuredDataExtractionStrategy on the backend) — a plain "," is deliberately NOT used as
// the split point here because a single address is itself comma-separated (street, city,
// postcode); splitting on "," would shred one real address into several fake ones.
const LOCATION_SEPARATOR = ';'

function findLocationFieldName(
  configuredFields: FieldDefinition[],
  fieldNames: string[],
): string | null {
  const byType = configuredFields.find((f) => f.type === 'Location')?.name
  if (byType && fieldNames.includes(byType)) {
    return byType
  }
  return fieldNames.find((n) => n.trim().toLowerCase() === 'location') ?? null
}

/**
 * Duplicates each record once per location when its Location field holds more than one
 * (";"-joined) value, so every exported row has exactly one location. Single-location and
 * empty-location rows pass through unchanged. Does not mutate the input records/grid data.
 */
function expandRecordsByLocation(
  records: ScrapedRecord[],
  locationFieldName: string,
): { expanded: ScrapedRecord[]; locationCount: number } {
  const expanded: ScrapedRecord[] = []
  const seenLocations = new Set<string>()

  for (const record of records) {
    const rawValue = record.fields[locationFieldName]
    const locations = (rawValue ?? '')
      .split(LOCATION_SEPARATOR)
      .map((part) => part.trim())
      .filter((part) => part.length > 0)

    if (locations.length <= 1) {
      // Empty, null, or single-location — export the row exactly as-is.
      expanded.push(record)
      if (locations.length === 1) {
        seenLocations.add(locations[0])
      }
      continue
    }

    for (const location of locations) {
      expanded.push({ ...record, fields: { ...record.fields, [locationFieldName]: location } })
      seenLocations.add(location)
    }
  }

  return { expanded, locationCount: seenLocations.size }
}

const DEFAULT_FIELDS: FieldDefinition[] = [
  field('Name', 'Name', true),
  field('Role'),
  field('Location', 'Location'),
  field('Experience'),
  field('Phone', 'Phone'),
  field('Email', 'Email'),
  field('Languages', 'Text'),
  field('Fees', 'Currency'),
]

const ScraperPage: React.FC = () => {
  const [url, setUrl] = useState('')
  const [maxRecords, setMaxRecords] = useState(200)
  const [timeoutSeconds, setTimeoutSeconds] = useState(20)
  const [fields, setFields] = useState<FieldDefinition[]>(DEFAULT_FIELDS)

  const [crawlOptionsOpen, setCrawlOptionsOpen] = useState(false)
  const [enableProfileCrawl, setEnableProfileCrawl] = useState(true)
  const [maxProfiles, setMaxProfiles] = useState(50)
  const [maxPages, setMaxPages] = useState(1)
  const [enableJavaScriptRendering, setEnableJavaScriptRendering] = useState(false)

  const [urlStatus, setUrlStatus] = useState<{ valid: boolean; message: string } | null>(null)
  const [validating, setValidating] = useState(false)
  const [scraping, setScraping] = useState(false)
  const [exporting, setExporting] = useState(false)
  const [result, setResult] = useState<ScrapeResult | null>(null)
  const [scrapeError, setScrapeError] = useState<string | null>(null)
  const [expandByLocation, setExpandByLocation] = useState(false)
  const [exportMessage, setExportMessage] = useState<string | null>(null)

  const [modalVisible, setModalVisible] = useState(false)
  const [editingIndex, setEditingIndex] = useState<number | null>(null)
  const [forceAdvancedOpen, setForceAdvancedOpen] = useState(false)

  const handleValidateUrl = async () => {
    setValidating(true)
    setUrlStatus(null)
    try {
      const response = await scraperApi.validateUrl(url)
      setUrlStatus({
        valid: response.isValid,
        message: response.isValid
          ? 'URL is reachable and allowed.'
          : (response.reason ?? 'URL is not valid.'),
      })
    } catch {
      setUrlStatus({ valid: false, message: 'Unable to validate the URL.' })
    } finally {
      setValidating(false)
    }
  }

  const handleAddField = () => {
    setEditingIndex(null)
    setForceAdvancedOpen(false)
    setModalVisible(true)
  }

  const handleEditField = (index: number) => {
    setEditingIndex(index)
    setForceAdvancedOpen(false)
    setModalVisible(true)
  }

  const handleConfigureField = (fieldName: string) => {
    const index = fields.findIndex((f) => f.name === fieldName)
    if (index === -1) return
    setEditingIndex(index)
    setForceAdvancedOpen(true)
    setModalVisible(true)
  }

  const handleDeleteField = (index: number) => {
    setFields(fields.filter((_, i) => i !== index))
  }

  const handleSaveField = (field: FieldDefinition) => {
    if (editingIndex === null) {
      setFields([...fields, field])
    } else {
      setFields(fields.map((f, i) => (i === editingIndex ? field : f)))
    }
    setModalVisible(false)
  }

  const handleTestField = (field: FieldDefinition) => {
    if (!field.selector?.trim()) {
      return Promise.resolve({
        matchCount: 0,
        samplePreviews: [],
        warning: 'Enter a selector to test.',
      })
    }
    return scraperApi.testSelector({
      url,
      selector: field.selector,
      selectorType: field.selectorType,
      extractionType: field.extractionType,
      attribute: field.attribute,
    })
  }

  const handleScrape = async () => {
    setScraping(true)
    setScrapeError(null)
    setResult(null)
    try {
      const config: ScraperConfig = {
        url,
        fields,
        maxRecords,
        timeoutSeconds,
        enableProfileCrawl,
        maxProfiles,
        maxPages,
        enableJavaScriptRendering,
      }
      const scrapeResult = await scraperApi.scrape(config)
      setResult(scrapeResult)
    } catch (err) {
      setScrapeError(
        err instanceof ApiError
          ? err.message
          : 'Unable to complete the scrape. Please check the URL and field configuration.',
      )
    } finally {
      setScraping(false)
    }
  }

  // Drives the on-screen Results table too, so toggling "Expand by Location" shows the same
  // one-location-per-row breakdown the CSV export produces — computed, not stored, so the
  // underlying `result` state (the actual scrape data) is never mutated.
  const previewRecords = useMemo(() => {
    if (!result || !expandByLocation) {
      return result?.records ?? []
    }
    const locationFieldName = findLocationFieldName(fields, result.fieldNames)
    if (!locationFieldName) {
      return result.records
    }
    return expandRecordsByLocation(result.records, locationFieldName).expanded
  }, [result, expandByLocation, fields])

  const handleExport = async () => {
    if (!result) return
    setExporting(true)
    setExportMessage(null)
    try {
      let records = result.records
      let filename = `scrape-export-${Date.now()}.csv`

      if (expandByLocation) {
        const locationFieldName = findLocationFieldName(fields, result.fieldNames)
        if (locationFieldName) {
          const { expanded, locationCount } = expandRecordsByLocation(
            result.records,
            locationFieldName,
          )
          records = expanded
          const isoDate = new Date().toISOString().slice(0, 10)
          filename = `export_expanded_by_location_${isoDate}.csv`
          setExportMessage(
            `Exported ${expanded.length} rows (expanded from ${result.records.length}) across ${locationCount} location${locationCount === 1 ? '' : 's'}.`,
          )
        } else {
          setExportMessage('No Location field found — exported without expansion.')
        }
      }

      const blob = await scraperApi.exportCsv({
        fieldNames: result.fieldNames,
        records,
      })
      const link = document.createElement('a')
      link.href = URL.createObjectURL(blob)
      link.download = filename
      document.body.appendChild(link)
      link.click()
      document.body.removeChild(link)
    } catch {
      setScrapeError('Unable to export the results to CSV.')
    } finally {
      setExporting(false)
    }
  }

  const handleExportByPage = async () => {
    if (!result) return
    setExporting(true)
    setExportMessage(null)
    try {
      const blob = await scraperApi.exportCsv({
        fieldNames: result.fieldNames,
        records: result.records,
        groupByPage: true,
      })
      const pageCount = new Set(result.records.map((r) => r.pageNumber)).size
      const link = document.createElement('a')
      link.href = URL.createObjectURL(blob)
      link.download = `scrape-export-by-page-${Date.now()}.zip`
      document.body.appendChild(link)
      link.click()
      document.body.removeChild(link)
      setExportMessage(
        `Exported ${pageCount} CSV file${pageCount === 1 ? '' : 's'} (one per page, e.g. "1.csv", "2.csv", ...) in the zip.`,
      )
    } catch {
      setScrapeError('Unable to export the results to CSV.')
    } finally {
      setExporting(false)
    }
  }

  return (
    <>
      <CCard className="mb-4">
        <CCardHeader className="d-flex justify-content-between align-items-center flex-wrap gap-2">
          <strong>Web Scraper</strong>
        </CCardHeader>
        <CCardBody>
          <CForm>
            <CRow className="mb-3">
              <CCol md={9}>
                <CFormLabel>Website URL</CFormLabel>
                <CInputGroup>
                  <CFormInput
                    value={url}
                    onChange={(e) => setUrl(e.target.value)}
                    placeholder="https://example.com/listing-page"
                  />
                  <CButton
                    color="primary"
                    variant="outline"
                    onClick={handleValidateUrl}
                    disabled={!url || validating}
                  >
                    {validating ? <CSpinner size="sm" /> : 'Validate URL'}
                  </CButton>
                </CInputGroup>
                {urlStatus && (
                  <CAlert color={urlStatus.valid ? 'success' : 'danger'} className="mt-2 py-2">
                    {urlStatus.message}
                  </CAlert>
                )}
              </CCol>
              <CCol md={3}>
                <CFormLabel htmlFor="max-records">Max Records</CFormLabel>
                <CFormInput
                  id="max-records"
                  type="number"
                  min={1}
                  max={1000}
                  value={maxRecords}
                  onChange={(e) => setMaxRecords(Number(e.target.value))}
                />
              </CCol>
            </CRow>
            <CRow className="mb-3">
              <CCol md={3}>
                <CFormLabel htmlFor="timeout-seconds">Timeout (seconds)</CFormLabel>
                <CFormInput
                  id="timeout-seconds"
                  type="number"
                  min={MIN_TIMEOUT_SECONDS}
                  max={MAX_TIMEOUT_SECONDS}
                  value={timeoutSeconds}
                  onChange={(e) => {
                    const raw = Number(e.target.value)
                    if (Number.isNaN(raw)) return
                    setTimeoutSeconds(
                      Math.min(MAX_TIMEOUT_SECONDS, Math.max(MIN_TIMEOUT_SECONDS, raw)),
                    )
                  }}
                />
                <div className="form-text">Maximum {MAX_TIMEOUT_SECONDS} seconds.</div>
              </CCol>
              <CCol md={3} className="d-flex align-items-end">
                <CFormCheck
                  id="expand-by-location"
                  type="checkbox"
                  button={{ color: expandByLocation ? 'info' : 'secondary', variant: 'outline' }}
                  label="Expand by Location"
                  checked={expandByLocation}
                  onChange={(e) => setExpandByLocation(e.target.checked)}
                />
              </CCol>
            </CRow>

            <CButton
              color="link"
              className="px-0"
              onClick={() => setCrawlOptionsOpen((prev) => !prev)}
            >
              {crawlOptionsOpen ? '– Hide' : '+ Show'} Crawl Options
            </CButton>
            <CCollapse visible={crawlOptionsOpen}>
              <div className="border rounded p-3 mt-2">
                <CFormCheck
                  id="enable-profile-crawl"
                  label="Also visit each doctor's own profile page for fields not shown on the listing (Bio, Qualifications, ...)"
                  checked={enableProfileCrawl}
                  onChange={(e) => setEnableProfileCrawl(e.target.checked)}
                />
                {enableProfileCrawl && (
                  <CRow className="mt-3">
                    <CCol md={4}>
                      <CFormLabel htmlFor="max-profiles">Max Profiles to Visit</CFormLabel>
                      <CFormInput
                        id="max-profiles"
                        type="number"
                        min={0}
                        max={1000}
                        value={maxProfiles}
                        onChange={(e) => setMaxProfiles(Number(e.target.value))}
                      />
                    </CCol>
                    <CCol md={4}>
                      <CFormLabel htmlFor="max-pages">Max Listing Pages</CFormLabel>
                      <CFormInput
                        id="max-pages"
                        type="number"
                        min={1}
                        max={100}
                        value={maxPages}
                        onChange={(e) => setMaxPages(Number(e.target.value))}
                      />
                      <div className="form-text">
                        Follows &ldquo;next page&rdquo; links if the listing is paginated.
                      </div>
                    </CCol>
                  </CRow>
                )}
                <CFormCheck
                  id="enable-js-rendering"
                  className="mt-3"
                  label="Render JavaScript before scraping (for sites whose content only loads dynamically — slower)"
                  checked={enableJavaScriptRendering}
                  onChange={(e) => setEnableJavaScriptRendering(e.target.checked)}
                />
              </div>
            </CCollapse>
          </CForm>
        </CCardBody>
      </CCard>

      <CCard className="mb-4">
        <CCardHeader className="d-flex justify-content-between align-items-center">
          <div>
            <strong>Fields to Extract</strong>
            <div className="text-body-secondary small">
              Just add a name &mdash; type and selector are optional. The scraper automatically
              finds matching data on the page.
            </div>
          </div>
          <CButton size="sm" color="primary" onClick={handleAddField}>
            <CIcon icon={cilPlus} className="me-1" />
            Add Field
          </CButton>
        </CCardHeader>
        <CCardBody>
          {fields.length === 0 ? (
            <p className="text-body-secondary mb-0">
              No fields configured yet. Add at least one field, e.g. &ldquo;Name&rdquo;.
            </p>
          ) : (
            <div className="table-responsive">
              <CTable striped small>
                <CTableHead>
                  <CTableRow>
                    <CTableHeaderCell>Name</CTableHeaderCell>
                    <CTableHeaderCell>Type</CTableHeaderCell>
                    <CTableHeaderCell>Advanced</CTableHeaderCell>
                    <CTableHeaderCell>Required</CTableHeaderCell>
                    <CTableHeaderCell>Actions</CTableHeaderCell>
                  </CTableRow>
                </CTableHead>
                <CTableBody>
                  {fields.map((field, idx) => (
                    <CTableRow key={idx}>
                      <CTableDataCell>{field.name}</CTableDataCell>
                      <CTableDataCell>{field.type || 'Auto Detect'}</CTableDataCell>
                      <CTableDataCell>
                        {field.selector?.trim() ? (
                          <CBadge color="info">Selector set</CBadge>
                        ) : (
                          <span className="text-body-secondary">Automatic</span>
                        )}
                      </CTableDataCell>
                      <CTableDataCell>{field.required ? 'Yes' : 'No'}</CTableDataCell>
                      <CTableDataCell>
                        <CButton
                          size="sm"
                          color="info"
                          variant="ghost"
                          onClick={() => handleEditField(idx)}
                        >
                          <CIcon icon={cilPencil} />
                        </CButton>
                        <CButton
                          size="sm"
                          color="danger"
                          variant="ghost"
                          onClick={() => handleDeleteField(idx)}
                        >
                          <CIcon icon={cilTrash} />
                        </CButton>
                      </CTableDataCell>
                    </CTableRow>
                  ))}
                </CTableBody>
              </CTable>
            </div>
          )}
        </CCardBody>
      </CCard>

      <div className="mb-2 d-flex gap-2">
        <CButton
          color="success"
          onClick={handleScrape}
          disabled={!url || fields.length === 0 || scraping}
        >
          {scraping ? <CSpinner size="sm" className="me-2" /> : null}
          Start Scraping
        </CButton>
        {result && result.records.length > 0 && (
          <>
            <CButton color="secondary" onClick={handleExport} disabled={exporting}>
              {exporting ? <CSpinner size="sm" className="me-2" /> : null}
              Export CSV
            </CButton>
            <CButton
              color="secondary"
              variant="outline"
              onClick={handleExportByPage}
              disabled={exporting}
            >
              {exporting ? <CSpinner size="sm" className="me-2" /> : null}
              Export by Page (ZIP)
            </CButton>
          </>
        )}
      </div>
      {scraping && (
        <p className="text-body-secondary small mb-4">
          {enableJavaScriptRendering
            ? 'Rendering pages in a headless browser before scraping — this is slower than a plain fetch...'
            : enableProfileCrawl
              ? 'Finding records and visiting each profile page for full details — this can take a little longer than a listing-only scrape...'
              : 'Scraping the page...'}
        </p>
      )}

      {scrapeError && <CAlert color="danger">{scrapeError}</CAlert>}
      {exportMessage && (
        <CAlert color="info" dismissible onClose={() => setExportMessage(null)} className="py-2">
          {exportMessage}
        </CAlert>
      )}

      {result && (
        <CCard className="mb-4">
          <CCardHeader>
            <strong>Results</strong>
          </CCardHeader>
          <CCardBody>
            <ResultsPreview
              result={result}
              records={previewRecords}
              onConfigureField={handleConfigureField}
            />
          </CCardBody>
        </CCard>
      )}

      <FieldEditorModal
        visible={modalVisible}
        initialField={editingIndex !== null ? fields[editingIndex] : null}
        onSave={handleSaveField}
        onClose={() => setModalVisible(false)}
        onTest={handleTestField}
        startWithAdvancedOpen={forceAdvancedOpen}
      />
    </>
  )
}

export default ScraperPage
