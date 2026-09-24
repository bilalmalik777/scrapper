import React, { useEffect, useRef, useState } from 'react'
import {
  CAlert,
  CBadge,
  CButton,
  CCard,
  CCardBody,
  CCardHeader,
  CCol,
  CForm,
  CFormCheck,
  CFormInput,
  CFormLabel,
  CProgress,
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
import { crawlJobsApi } from '../../services/crawlJobs.service'
import { scraperApi } from '../../services/scraper.service'
import { ApiError } from '../../api/client'
import type { CrawlJobState, FieldDefinition, PagedCrawlConfig, ScrapedRecord } from '../../api/types'

// Kept in sync with the backend's PagedCrawlConfigDto defaults — see CrawlJobDtos.cs.
const DEFAULT_CONFIG: PagedCrawlConfig = {
  url: '',
  fields: [
    { name: 'Name', type: 'Name', required: false, selectorType: 'Css', extractionType: 'Text' },
    { name: 'Location', type: 'Location', required: false, selectorType: 'Css', extractionType: 'Text' },
  ],
  totalPages: 1,
  minDelaySeconds: 5,
  maxDelaySeconds: 15,
  longPauseEveryPages: 10,
  longPauseSeconds: 45,
  maxRetriesPerPage: 3,
  stopOnPersistentFailure: true,
}

const STATUS_COLOR: Record<CrawlJobState['status'], string> = {
  NotStarted: 'secondary',
  Running: 'info',
  Paused: 'warning',
  Blocked: 'danger',
  Completed: 'success',
  Failed: 'danger',
}

// While a job is actively running, poll its status every few seconds so the page/records
// counters update without the user needing to refresh manually.
const POLL_INTERVAL_MS = 4000

const CrawlJobsPage: React.FC = () => {
  const [config, setConfig] = useState<PagedCrawlConfig>(DEFAULT_CONFIG)
  const [jobIdInput, setJobIdInput] = useState('')
  const [activeJobId, setActiveJobId] = useState<string | null>(null)
  const [state, setState] = useState<CrawlJobState | null>(null)
  const [records, setRecords] = useState<ScrapedRecord[]>([])
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [exporting, setExporting] = useState(false)
  const [fieldModalOpen, setFieldModalOpen] = useState(false)
  const [editingFieldIndex, setEditingFieldIndex] = useState<number | null>(null)
  const pollRef = useRef<ReturnType<typeof setInterval> | null>(null)

  useEffect(() => {
    return () => {
      if (pollRef.current) clearInterval(pollRef.current)
    }
  }, [])

  const stopPolling = () => {
    if (pollRef.current) {
      clearInterval(pollRef.current)
      pollRef.current = null
    }
  }

  const refreshStatus = async (jobId: string) => {
    try {
      const latest = await crawlJobsApi.getStatus(jobId)
      setState(latest)
      if (latest.status !== 'Running') {
        stopPolling()
        const recordsResponse = await crawlJobsApi.getRecords(jobId)
        setRecords(recordsResponse.records)
      }
    } catch (err) {
      stopPolling()
      setError(err instanceof ApiError ? err.message : 'Unable to fetch crawl job status.')
    }
  }

  const startPolling = (jobId: string) => {
    stopPolling()
    pollRef.current = setInterval(() => void refreshStatus(jobId), POLL_INTERVAL_MS)
  }

  const handleStart = async (fresh: boolean) => {
    setError(null)
    setBusy(true)
    try {
      const jobId = await crawlJobsApi.start({
        jobId: jobIdInput.trim() || undefined,
        config,
        fresh,
      })
      setActiveJobId(jobId)
      setJobIdInput(jobId)
      await refreshStatus(jobId)
      startPolling(jobId)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Unable to start the crawl job.')
    } finally {
      setBusy(false)
    }
  }

  const handleLoadExisting = async () => {
    const jobId = jobIdInput.trim()
    if (!jobId) return
    setError(null)
    setBusy(true)
    try {
      setActiveJobId(jobId)
      await refreshStatus(jobId)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'No crawl job found with that id.')
    } finally {
      setBusy(false)
    }
  }

  const handleResume = async () => {
    if (!activeJobId) return
    setError(null)
    setBusy(true)
    try {
      await crawlJobsApi.resume(activeJobId)
      await refreshStatus(activeJobId)
      startPolling(activeJobId)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Unable to resume the crawl job.')
    } finally {
      setBusy(false)
    }
  }

  const handleRetryFailed = async () => {
    if (!activeJobId) return
    setError(null)
    setBusy(true)
    try {
      await crawlJobsApi.retryFailed(activeJobId)
      await refreshStatus(activeJobId)
      startPolling(activeJobId)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Unable to retry failed pages.')
    } finally {
      setBusy(false)
    }
  }

  const downloadBlob = (blob: Blob, filename: string) => {
    const link = document.createElement('a')
    link.href = URL.createObjectURL(blob)
    link.download = filename
    document.body.appendChild(link)
    link.click()
    document.body.removeChild(link)
  }

  const handleExportCsv = async () => {
    if (!state) return
    setError(null)
    setExporting(true)
    try {
      const blob = await scraperApi.exportCsv({ fieldNames: state.fieldNames, records })
      downloadBlob(blob, `crawl-${state.jobId}-export-${Date.now()}.csv`)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Unable to export the results to CSV.')
    } finally {
      setExporting(false)
    }
  }

  const handleExportByPage = async () => {
    if (!state) return
    setError(null)
    setExporting(true)
    try {
      const blob = await scraperApi.exportCsv({ fieldNames: state.fieldNames, records, groupByPage: true })
      downloadBlob(blob, `crawl-${state.jobId}-export-by-page-${Date.now()}.zip`)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Unable to export the results to CSV.')
    } finally {
      setExporting(false)
    }
  }

  const handleSaveField = (savedField: FieldDefinition) => {
    setConfig((prev) => {
      const fields = [...prev.fields]
      if (editingFieldIndex !== null) {
        fields[editingFieldIndex] = savedField
      } else {
        fields.push(savedField)
      }
      return { ...prev, fields }
    })
    setFieldModalOpen(false)
    setEditingFieldIndex(null)
  }

  const handleRemoveField = (index: number) => {
    setConfig((prev) => ({ ...prev, fields: prev.fields.filter((_, i) => i !== index) }))
  }

  const progressPercent = state && state.totalPages > 0 ? Math.round((state.lastCompletedPage / state.totalPages) * 100) : 0

  const previewResult = state
    ? {
        totalRecords: records.length,
        records,
        warnings: [],
        errors: [],
        fieldNames: state.fieldNames,
        recordSelectorAutoDetected: false,
        pagesProcessed: state.lastCompletedPage,
        profilesVisited: 0,
      }
    : null

  return (
    <CRow>
      <CCol xs={12}>
        <CCard className="mb-4">
          <CCardHeader>
            <strong>Paged Crawl Job</strong>
            <div className="text-body-secondary small">
              For large paginated listings (dozens of pages) that need a slow, resumable crawl
              instead of one single scrape — persistent session, human-paced delays, automatic
              checkpointing, and clean stop-and-resume if the site blocks a page.
            </div>
          </CCardHeader>
          <CCardBody>
            {error && (
              <CAlert color="danger" dismissible onClose={() => setError(null)}>
                {error}
              </CAlert>
            )}

            <CForm>
              <CRow className="mb-3">
                <CCol md={8}>
                  <CFormLabel>Search URL (page 1)</CFormLabel>
                  <CFormInput
                    value={config.url}
                    placeholder="https://example.com/search/?specialty=orthopaedic-surgery&page=1"
                    onChange={(e) => setConfig({ ...config, url: e.target.value })}
                  />
                </CCol>
                <CCol md={4}>
                  <CFormLabel>Total Pages</CFormLabel>
                  <CFormInput
                    type="number"
                    min={1}
                    value={config.totalPages}
                    onChange={(e) => setConfig({ ...config, totalPages: Number(e.target.value) || 1 })}
                  />
                </CCol>
              </CRow>

              <CRow className="mb-3">
                <CCol md={3}>
                  <CFormLabel>Min Delay (s)</CFormLabel>
                  <CFormInput
                    type="number"
                    min={0}
                    value={config.minDelaySeconds}
                    onChange={(e) => setConfig({ ...config, minDelaySeconds: Number(e.target.value) || 0 })}
                  />
                </CCol>
                <CCol md={3}>
                  <CFormLabel>Max Delay (s)</CFormLabel>
                  <CFormInput
                    type="number"
                    min={0}
                    value={config.maxDelaySeconds}
                    onChange={(e) => setConfig({ ...config, maxDelaySeconds: Number(e.target.value) || 0 })}
                  />
                </CCol>
                <CCol md={3}>
                  <CFormLabel>Max Retries / Page</CFormLabel>
                  <CFormInput
                    type="number"
                    min={1}
                    value={config.maxRetriesPerPage}
                    onChange={(e) => setConfig({ ...config, maxRetriesPerPage: Number(e.target.value) || 1 })}
                  />
                </CCol>
                <CCol md={3}>
                  <CFormLabel>User-Agent (optional)</CFormLabel>
                  <CFormInput
                    value={config.userAgent ?? ''}
                    placeholder="Default desktop browser"
                    onChange={(e) => setConfig({ ...config, userAgent: e.target.value || null })}
                  />
                </CCol>
              </CRow>

              <CRow className="mb-3">
                <CCol md={12}>
                  <CFormCheck
                    label="Stop the crawl cleanly if a page keeps failing (recommended) — otherwise it's recorded as failed and the crawl moves on"
                    checked={config.stopOnPersistentFailure ?? true}
                    onChange={(e) => setConfig({ ...config, stopOnPersistentFailure: e.target.checked })}
                  />
                </CCol>
              </CRow>

              <CRow className="mb-3">
                <CCol md={12}>
                  <CFormCheck
                    label="Render JavaScript before scraping (headless browser) — for sites that block plain HTTP requests outright, regardless of headers"
                    checked={config.enableJavaScriptRendering ?? false}
                    onChange={(e) => setConfig({ ...config, enableJavaScriptRendering: e.target.checked })}
                  />
                </CCol>
              </CRow>

              <div className="d-flex justify-content-between align-items-center mb-2">
                <CFormLabel className="mb-0">Fields to Extract</CFormLabel>
                <CButton
                  color="primary"
                  variant="outline"
                  size="sm"
                  onClick={() => {
                    setEditingFieldIndex(null)
                    setFieldModalOpen(true)
                  }}
                >
                  <CIcon icon={cilPlus} className="me-1" />
                  Add Field
                </CButton>
              </div>
              <CTable bordered small className="mb-3">
                <CTableHead>
                  <CTableRow>
                    <CTableHeaderCell>Name</CTableHeaderCell>
                    <CTableHeaderCell>Type</CTableHeaderCell>
                    <CTableHeaderCell></CTableHeaderCell>
                  </CTableRow>
                </CTableHead>
                <CTableBody>
                  {config.fields.map((f, i) => (
                    <CTableRow key={i}>
                      <CTableDataCell>{f.name}</CTableDataCell>
                      <CTableDataCell>{f.type ?? 'Auto Detect'}</CTableDataCell>
                      <CTableDataCell className="text-end">
                        <CButton
                          color="secondary"
                          variant="ghost"
                          size="sm"
                          onClick={() => {
                            setEditingFieldIndex(i)
                            setFieldModalOpen(true)
                          }}
                        >
                          <CIcon icon={cilPencil} />
                        </CButton>
                        <CButton color="danger" variant="ghost" size="sm" onClick={() => handleRemoveField(i)}>
                          <CIcon icon={cilTrash} />
                        </CButton>
                      </CTableDataCell>
                    </CTableRow>
                  ))}
                </CTableBody>
              </CTable>

              <CRow className="mb-3">
                <CCol md={6}>
                  <CFormLabel>Job Id (optional — reuse to resume a specific job)</CFormLabel>
                  <CFormInput
                    value={jobIdInput}
                    placeholder="Leave blank to generate a new job"
                    onChange={(e) => setJobIdInput(e.target.value)}
                  />
                </CCol>
              </CRow>

              <div className="d-flex gap-2 flex-wrap">
                <CButton color="primary" disabled={busy || !config.url || config.fields.length === 0} onClick={() => handleStart(false)}>
                  {busy && <CSpinner size="sm" className="me-2" />}
                  Start / Continue
                </CButton>
                <CButton color="secondary" variant="outline" disabled={busy || !config.url} onClick={() => handleStart(true)}>
                  Start Fresh
                </CButton>
                <CButton color="secondary" variant="outline" disabled={busy || !jobIdInput.trim()} onClick={handleLoadExisting}>
                  Load Job Status
                </CButton>
              </div>
            </CForm>
          </CCardBody>
        </CCard>

        {state && (
          <CCard className="mb-4">
            <CCardHeader className="d-flex justify-content-between align-items-center">
              <strong>Job {state.jobId}</strong>
              <CBadge color={STATUS_COLOR[state.status]}>{state.status}</CBadge>
            </CCardHeader>
            <CCardBody>
              <div className="mb-2">
                Page {state.lastCompletedPage} of {state.totalPages} completed
                {state.currentPage ? ` — currently on page ${state.currentPage}` : ''}
              </div>
              <CProgress className="mb-3" value={progressPercent}>
                {progressPercent}%
              </CProgress>

              <div className="d-flex flex-wrap gap-3 mb-3">
                <CBadge color="info">Records Saved: {state.recordsSaved}</CBadge>
                <CBadge color={state.failedPages.length > 0 ? 'danger' : 'secondary'}>
                  Failed Pages: {state.failedPages.length > 0 ? state.failedPages.join(', ') : 'none'}
                </CBadge>
              </div>

              {state.lastError && (
                <CAlert color="warning" className="mb-3">
                  {state.lastError}
                </CAlert>
              )}

              <div className="d-flex gap-2 flex-wrap">
                {(state.status === 'Paused' || state.status === 'Blocked') && (
                  <CButton color="primary" disabled={busy} onClick={handleResume}>
                    Resume
                  </CButton>
                )}
                {state.failedPages.length > 0 && (
                  <CButton color="warning" disabled={busy} onClick={handleRetryFailed}>
                    Retry Failed Pages
                  </CButton>
                )}
                <CButton color="secondary" variant="outline" disabled={busy} onClick={() => activeJobId && refreshStatus(activeJobId)}>
                  Refresh Now
                </CButton>
              </div>
            </CCardBody>
          </CCard>
        )}

        {previewResult && previewResult.records.length > 0 && (
          <CCard className="mb-4">
            <CCardHeader className="d-flex justify-content-between align-items-center">
              <strong>Saved Records</strong>
              <div className="d-flex gap-2">
                <CButton color="secondary" size="sm" disabled={exporting} onClick={handleExportCsv}>
                  {exporting && <CSpinner size="sm" className="me-2" />}
                  Export CSV
                </CButton>
                <CButton color="secondary" variant="outline" size="sm" disabled={exporting} onClick={handleExportByPage}>
                  Export by Page (ZIP)
                </CButton>
              </div>
            </CCardHeader>
            <CCardBody>
              <ResultsPreview result={previewResult} />
            </CCardBody>
          </CCard>
        )}
      </CCol>

      <FieldEditorModal
        visible={fieldModalOpen}
        initialField={editingFieldIndex !== null ? config.fields[editingFieldIndex] : null}
        onSave={handleSaveField}
        onClose={() => {
          setFieldModalOpen(false)
          setEditingFieldIndex(null)
        }}
        onTest={(f) =>
          scraperApi.testSelector({
            url: config.url,
            selectorType: f.selectorType,
            selector: f.selector ?? '',
            extractionType: f.extractionType,
            attribute: f.attribute,
            recordSelector: config.recordSelector,
            recordSelectorType: config.recordSelectorType,
          })
        }
      />
    </CRow>
  )
}

export default CrawlJobsPage
