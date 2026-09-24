import React from 'react'
import {
  CAlert,
  CBadge,
  CButton,
  CTable,
  CTableBody,
  CTableDataCell,
  CTableHead,
  CTableHeaderCell,
  CTableRow,
} from '@coreui/react-pro'
import type { ConfidenceLevel, ScrapedRecord, ScrapeResult } from '../../api/types'

interface ResultsPreviewProps {
  result: ScrapeResult
  /** Rows to display — defaults to `result.records`. Lets a caller show an expanded view
   * (e.g. one row per location) without mutating the underlying scrape result. */
  records?: ScrapedRecord[]
  onConfigureField?: (fieldName: string) => void
}

const confidenceBadgeColor: Record<ConfidenceLevel, string> = {
  High: 'success',
  Medium: 'warning',
  Low: 'danger',
  None: 'secondary',
}

const ResultsPreview: React.FC<ResultsPreviewProps> = ({ result, records, onConfigureField }) => {
  const displayRecords = records ?? result.records

  return (
    <div>
      <div className="d-flex flex-wrap gap-2 align-items-center mb-3">
        <CBadge color="info">Records Found: {result.totalRecords}</CBadge>
        {displayRecords.length !== result.records.length && (
          <CBadge color="info">Rows shown: {displayRecords.length} (expanded by location)</CBadge>
        )}
        {result.recordSelectorAutoDetected && (
          <CBadge color="secondary">Record structure auto-detected</CBadge>
        )}
        {result.profilesVisited > 0 && (
          <CBadge color="primary">Profiles Visited: {result.profilesVisited}</CBadge>
        )}
        {result.pagesProcessed > 1 && (
          <CBadge color="secondary">Listing Pages: {result.pagesProcessed}</CBadge>
        )}
        {result.warnings.length > 0 && (
          <CBadge color="warning">{result.warnings.length} Warning(s)</CBadge>
        )}
        {result.errors.length > 0 && (
          <CBadge color="danger">{result.errors.length} Error(s)</CBadge>
        )}
      </div>

      {result.errors.map((err, idx) => (
        <CAlert key={`err-${idx}`} color="danger">
          {err}
        </CAlert>
      ))}
      {result.warnings.map((warn, idx) => (
        <CAlert key={`warn-${idx}`} color="warning">
          {warn}
        </CAlert>
      ))}

      {displayRecords.length > 0 && (
        <div className="table-responsive">
          <CTable striped bordered small>
            <CTableHead>
              <CTableRow>
                {result.fieldNames.map((name) => (
                  <CTableHeaderCell key={name}>{name}</CTableHeaderCell>
                ))}
              </CTableRow>
            </CTableHead>
            <CTableBody>
              {displayRecords.map((record, rowIdx) => (
                <CTableRow key={rowIdx}>
                  {result.fieldNames.map((name) => {
                    const isMissing = record.missingFields.includes(name)
                    const fieldResult = record.fieldResults[name]
                    return (
                      <CTableDataCell key={name}>
                        {isMissing ? (
                          <div>
                            <span className="text-danger">
                              <em>Not found</em>
                            </span>
                            {onConfigureField && (
                              <CButton
                                color="link"
                                size="sm"
                                className="p-0 ms-2"
                                onClick={() => onConfigureField(name)}
                              >
                                Configure selector
                              </CButton>
                            )}
                          </div>
                        ) : (
                          <div>
                            <div>
                              {name === 'Profile URL' && record.fields[name] ? (
                                <a href={record.fields[name]!} target="_blank" rel="noreferrer">
                                  {record.fields[name]}
                                </a>
                              ) : (
                                record.fields[name]
                              )}
                            </div>
                            {fieldResult && (
                              <div className="d-flex align-items-center gap-2 mt-1">
                                <CBadge
                                  color={confidenceBadgeColor[fieldResult.confidenceLevel]}
                                  shape="rounded-pill"
                                >
                                  {fieldResult.confidenceLevel} confidence
                                </CBadge>
                                {fieldResult.confidenceLevel === 'Low' && onConfigureField && (
                                  <CButton
                                    color="link"
                                    size="sm"
                                    className="p-0"
                                    onClick={() => onConfigureField(name)}
                                  >
                                    Configure selector
                                  </CButton>
                                )}
                              </div>
                            )}
                          </div>
                        )}
                      </CTableDataCell>
                    )
                  })}
                </CTableRow>
              ))}
            </CTableBody>
          </CTable>
        </div>
      )}
    </div>
  )
}

export default ResultsPreview
