import React, { useEffect, useState } from 'react'
import {
  CButton,
  CCollapse,
  CForm,
  CFormCheck,
  CFormInput,
  CFormLabel,
  CFormSelect,
  CModal,
  CModalBody,
  CModalFooter,
  CModalHeader,
  CModalTitle,
} from '@coreui/react-pro'
import type {
  ExtractionType,
  FieldDefinition,
  FieldKind,
  SelectorType,
  TestSelectorResponse,
} from '../../api/types'

interface FieldEditorModalProps {
  visible: boolean
  initialField?: FieldDefinition | null
  onSave: (field: FieldDefinition) => void
  onClose: () => void
  onTest: (field: FieldDefinition) => Promise<TestSelectorResponse>
  startWithAdvancedOpen?: boolean
}

const emptyField: FieldDefinition = {
  name: '',
  type: null,
  required: false,
  selector: '',
  selectorType: 'Css',
  extractionType: 'Text',
  attribute: '',
}

const FIELD_KIND_OPTIONS: { value: FieldKind | ''; label: string }[] = [
  { value: '', label: 'Auto Detect' },
  { value: 'Text', label: 'Text' },
  { value: 'Name', label: 'Name' },
  { value: 'Location', label: 'Location' },
  { value: 'Currency', label: 'Currency / Price' },
  { value: 'Email', label: 'Email' },
  { value: 'Phone', label: 'Phone' },
  { value: 'Url', label: 'URL' },
  { value: 'Rating', label: 'Rating' },
  { value: 'Number', label: 'Number' },
  { value: 'Biography', label: 'Biography / Long text' },
]

const FieldEditorModal: React.FC<FieldEditorModalProps> = ({
  visible,
  initialField,
  onSave,
  onClose,
  onTest,
  startWithAdvancedOpen,
}) => {
  const [field, setField] = useState<FieldDefinition>(initialField ?? emptyField)
  const [error, setError] = useState<string | null>(null)
  const [testing, setTesting] = useState(false)
  const [testResult, setTestResult] = useState<TestSelectorResponse | null>(null)
  const [advancedOpen, setAdvancedOpen] = useState(false)

  useEffect(() => {
    const next = initialField ?? emptyField
    setField(next)
    setError(null)
    setTestResult(null)
    setAdvancedOpen(!!next.selector?.trim() || !!startWithAdvancedOpen)
  }, [initialField, visible, startWithAdvancedOpen])

  const handleTest = async () => {
    if (!field.selector?.trim()) {
      setError('Enter a selector before testing.')
      return
    }
    setError(null)
    setTesting(true)
    try {
      const result = await onTest(field)
      setTestResult(result)
    } catch {
      setTestResult({ matchCount: 0, samplePreviews: [], warning: 'Unable to test the selector.' })
    } finally {
      setTesting(false)
    }
  }

  const handleSave = () => {
    if (!field.name.trim()) {
      setError('Field name is required.')
      return
    }
    if (
      field.selector?.trim() &&
      field.extractionType === 'Attribute' &&
      !field.attribute?.trim()
    ) {
      setError('Attribute name is required for the Attribute extraction type.')
      return
    }
    onSave(field)
  }

  return (
    <CModal visible={visible} onClose={onClose} alignment="center">
      <CModalHeader>
        <CModalTitle>{initialField ? 'Edit Field' : 'Add Field'}</CModalTitle>
      </CModalHeader>
      <CModalBody>
        <CForm>
          {error && <div className="text-danger mb-3">{error}</div>}
          <div className="mb-3">
            <CFormLabel htmlFor="field-name">Field Name</CFormLabel>
            <CFormInput
              id="field-name"
              value={field.name}
              onChange={(e) => setField({ ...field, name: e.target.value })}
              placeholder="e.g. Name, Location, Fees"
              autoFocus
            />
          </div>
          <div className="mb-3">
            <CFormLabel htmlFor="field-type">Type (Optional)</CFormLabel>
            <CFormSelect
              id="field-type"
              value={field.type ?? ''}
              onChange={(e) =>
                setField({ ...field, type: (e.target.value || null) as FieldKind | null })
              }
            >
              {FIELD_KIND_OPTIONS.map((opt) => (
                <option key={opt.value} value={opt.value}>
                  {opt.label}
                </option>
              ))}
            </CFormSelect>
            <div className="form-text">
              Leave as Auto Detect and the scraper will infer the type from the field name and page
              content.
            </div>
          </div>
          <CFormCheck
            id="field-required"
            label="Required"
            checked={field.required}
            onChange={(e) => setField({ ...field, required: e.target.checked })}
          />

          <div className="mt-3">
            <CButton color="link" className="px-0" onClick={() => setAdvancedOpen((prev) => !prev)}>
              {advancedOpen ? '– Hide' : '+ Show'} Advanced Options
            </CButton>
            <CCollapse visible={advancedOpen}>
              <div className="border rounded p-3 mt-2">
                <p className="text-body-secondary small">
                  Optional. Supply a CSS selector or XPath only if you need to override automatic
                  detection for this field.
                </p>
                <div className="mb-3">
                  <CFormLabel htmlFor="field-selector-type">Selector Type</CFormLabel>
                  <CFormSelect
                    id="field-selector-type"
                    value={field.selectorType}
                    onChange={(e) =>
                      setField({ ...field, selectorType: e.target.value as SelectorType })
                    }
                  >
                    <option value="Css">CSS Selector</option>
                    <option value="XPath">XPath</option>
                  </CFormSelect>
                </div>
                <div className="mb-3">
                  <CFormLabel htmlFor="field-selector">CSS Selector / XPath</CFormLabel>
                  <CFormInput
                    id="field-selector"
                    value={field.selector ?? ''}
                    onChange={(e) => setField({ ...field, selector: e.target.value })}
                    placeholder={field.selectorType === 'Css' ? '.specialist-name' : './/h2'}
                  />
                </div>
                <div className="mb-3">
                  <CFormLabel htmlFor="field-extraction-type">Extraction Type</CFormLabel>
                  <CFormSelect
                    id="field-extraction-type"
                    value={field.extractionType}
                    onChange={(e) =>
                      setField({ ...field, extractionType: e.target.value as ExtractionType })
                    }
                  >
                    <option value="Text">Text</option>
                    <option value="Attribute">Attribute</option>
                    <option value="Html">HTML</option>
                    <option value="Link">Link (href/src)</option>
                  </CFormSelect>
                </div>
                {field.extractionType === 'Attribute' && (
                  <div className="mb-3">
                    <CFormLabel htmlFor="field-attribute">Attribute Name</CFormLabel>
                    <CFormInput
                      id="field-attribute"
                      value={field.attribute ?? ''}
                      onChange={(e) => setField({ ...field, attribute: e.target.value })}
                      placeholder="e.g. href, src, data-id"
                    />
                  </div>
                )}
                <CButton
                  color="info"
                  variant="outline"
                  size="sm"
                  onClick={handleTest}
                  disabled={testing}
                >
                  {testing ? 'Testing…' : 'Test Selector'}
                </CButton>

                {testResult && (
                  <div className="mt-3 p-2 border rounded">
                    <div>
                      <strong>Matches found:</strong> {testResult.matchCount}
                    </div>
                    {testResult.warning && <div className="text-warning">{testResult.warning}</div>}
                    {testResult.samplePreviews.length > 0 && (
                      <ul className="mb-0">
                        {testResult.samplePreviews.map((preview, idx) => (
                          <li key={idx} className="text-truncate">
                            {preview || <em>(empty)</em>}
                          </li>
                        ))}
                      </ul>
                    )}
                  </div>
                )}
              </div>
            </CCollapse>
          </div>
        </CForm>
      </CModalBody>
      <CModalFooter>
        <CButton color="secondary" variant="outline" onClick={onClose}>
          Cancel
        </CButton>
        <CButton color="primary" onClick={handleSave}>
          Save Field
        </CButton>
      </CModalFooter>
    </CModal>
  )
}

export default FieldEditorModal
