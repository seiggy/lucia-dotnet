import { useState, useEffect, useCallback } from 'react'
import {
  fetchConfigSchema,
  fetchConfigSection,
  updateConfigSection,
  resetConfig,
  testMusicAssistantIntegration,
} from '../api'
import type {
  ConfigSectionSchema,
  ConfigEntryDto,
  ConfigPropertySchema,
} from '../api'

/* ------------------------------------------------------------------ */
/*  Toast                                                              */
/* ------------------------------------------------------------------ */

interface Toast {
  id: number
  message: string
  type: 'success' | 'error'
}

let toastId = 0

function ToastContainer({ toasts, onDismiss }: { toasts: Toast[]; onDismiss: (id: number) => void }) {
  return (
    <div className="fixed top-4 right-4 z-50 flex flex-col gap-2">
      {toasts.map((t) => (
        <div
          key={t.id}
          className={`flex items-center gap-3 rounded-xl px-4 py-3 shadow-lg text-sm font-medium transition-all duration-300 ${
            t.type === 'success'
              ? 'bg-sage/20 text-light'
              : 'bg-ember/20 text-light'
          }`}
        >
          <span>{t.type === 'success' ? '✓' : '✕'}</span>
          <span className="flex-1">{t.message}</span>
          <button
            onClick={() => onDismiss(t.id)}
            className="ml-2 opacity-70 hover:opacity-100"
          >
            ×
          </button>
        </div>
      ))}
    </div>
  )
}

/* ------------------------------------------------------------------ */
/*  Confirmation Dialog                                                */
/* ------------------------------------------------------------------ */

function ConfirmDialog({
  open,
  title,
  message,
  onConfirm,
  onCancel,
}: {
  open: boolean
  title: string
  message: string
  onConfirm: () => void
  onCancel: () => void
}) {
  if (!open) return null
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60">
      <div className="w-full max-w-md rounded-xl bg-charcoal p-6 shadow-2xl border border-stone">
        <h3 className="text-lg font-semibold text-light">{title}</h3>
        <p className="mt-2 text-sm text-dust">{message}</p>
        <div className="mt-6 flex justify-end gap-3">
          <button
            onClick={onCancel}
            className="rounded-xl bg-basalt px-4 py-2 text-sm font-medium text-fog hover:bg-stone transition-colors"
          >
            Cancel
          </button>
          <button
            onClick={onConfirm}
            className="rounded-xl bg-ember/20 px-4 py-2 text-sm font-medium text-light hover:bg-red-500 transition-colors"
          >
            Reset All
          </button>
        </div>
      </div>
    </div>
  )
}

/* ------------------------------------------------------------------ */
/*  Toggle Switch                                                      */
/* ------------------------------------------------------------------ */

function ToggleSwitch({
  checked,
  onChange,
  disabled,
}: {
  checked: boolean
  onChange: (val: boolean) => void
  disabled?: boolean
}) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      disabled={disabled}
      onClick={() => onChange(!checked)}
      className={`relative inline-flex h-6 w-11 shrink-0 cursor-pointer rounded-full border-2 border-transparent transition-colors duration-200 input-focus focus:ring-2 focus:ring-amber focus:ring-offset-2 focus:ring-offset-void ${
        checked ? 'bg-amber-glow' : 'bg-stone'
      } ${disabled ? 'opacity-50 cursor-not-allowed' : ''}`}
    >
      <span
        className={`pointer-events-none inline-block h-5 w-5 transform rounded-full bg-white shadow ring-0 transition duration-200 ${
          checked ? 'translate-x-5' : 'translate-x-0'
        }`}
      />
    </button>
  )
}

/* ------------------------------------------------------------------ */
/*  Connection Strings Editor                                          */
/* ------------------------------------------------------------------ */

function ConnectionStringsEditor({
  entries,
  sensitiveKeys,
  showSecrets,
  onChange,
}: {
  entries: Record<string, string | null>
  sensitiveKeys: Set<string>
  showSecrets: boolean
  onChange: (updated: Record<string, string | null>) => void
}) {
  const keys = Object.keys(entries)

  const handleValueChange = (key: string, value: string) => {
    onChange({ ...entries, [key]: value || null })
  }

  const handleAddNew = () => {
    const name = prompt('Connection name:')
    if (!name || name.trim() === '') return
    if (entries[name] !== undefined) return
    onChange({ ...entries, [name.trim()]: '' })
  }

  const handleRemove = (key: string) => {
    const next = { ...entries }
    next[key] = null
    onChange(next)
  }

  return (
    <div className="space-y-4">
      {keys.map((key) => {
        const value = entries[key]
        if (value === null) return null
        const isSensitive = sensitiveKeys.has(key)
        const isMasked = isSensitive && value === '********'
        return (
          <div key={key} className="rounded-xl border border-stone bg-basalt p-4">
            <div className="flex flex-wrap items-center justify-between gap-2 mb-2">
              <label className="text-sm font-medium text-amber">
                {key}
                {isSensitive && (
                  <span className="ml-2 rounded bg-amber/20/20 px-1.5 py-0.5 text-[10px] font-semibold uppercase text-amber">
                    sensitive
                  </span>
                )}
              </label>
              <button
                type="button"
                onClick={() => handleRemove(key)}
                className="text-xs text-rose hover:text-rose transition-colors"
              >
                Remove
              </button>
            </div>
            <input
              type={isSensitive && !showSecrets ? 'password' : 'text'}
              value={isMasked ? '' : (value ?? '')}
              onChange={(e) => handleValueChange(key, e.target.value)}
              className="w-full rounded-xl border border-stone bg-basalt px-3 py-2 text-sm text-light font-mono placeholder-dust/60 input-focus focus:ring-1 focus:ring-amber input-focus"
              placeholder={isMasked ? '••••••••  (enter new value to change)' : 'Server=...;Database=...;...'}
            />
          </div>
        )
      })}
      <button
        type="button"
        onClick={handleAddNew}
        className="flex items-center gap-2 rounded-xl border border-dashed border-stone px-4 py-2 text-sm text-dust hover:border-amber hover:text-amber transition-colors"
      >
        <span className="text-lg leading-none">+</span>
        Add Connection String
      </button>
    </div>
  )
}

/* ------------------------------------------------------------------ */
/*  Array Section Form                                                 */
/* ------------------------------------------------------------------ */

/** Parse flat indexed keys ("0:AgentName") into an array of item objects. */
function parseArrayItems(
  values: Record<string, string | null>,
  properties: ConfigPropertySchema[],
): Record<string, string | null>[] {
  // Collect all numeric indices
  const indices = new Set<number>()
  for (const key of Object.keys(values)) {
    const match = key.match(/^(\d+):/)
    if (match) indices.add(parseInt(match[1]))
  }

  return Array.from(indices)
    .sort((a, b) => a - b)
    .map((idx) => {
      const item: Record<string, string | null> = {}
      for (const prop of properties) {
        const simpleKey = `${idx}:${prop.name}`
        if (values[simpleKey] !== undefined) {
          item[prop.name] = values[simpleKey]
          continue
        }
        // Array property stored as sub-keys (e.g. 0:AgentSkills:0, 0:AgentSkills:1)
        const subValues: string[] = []
        for (const key of Object.keys(values)) {
          if (key.startsWith(`${idx}:${prop.name}:`) && values[key] !== null) {
            subValues.push(values[key]!)
          }
        }
        item[prop.name] = subValues.length > 0 ? subValues.join(', ') : null
      }
      return item
    })
}

/** Flatten an array of item objects back to indexed keys. */
function flattenArrayItems(
  items: Record<string, string | null>[],
  properties: ConfigPropertySchema[],
): Record<string, string | null> {
  const result: Record<string, string | null> = {}
  items.forEach((item, idx) => {
    for (const prop of properties) {
      if (prop.type === 'array' && item[prop.name]) {
        const parts = item[prop.name]!
          .split(',')
          .map((v) => v.trim())
          .filter((v) => v)
        parts.forEach((v, i) => {
          result[`${idx}:${prop.name}:${i}`] = v
        })
      } else {
        result[`${idx}:${prop.name}`] = item[prop.name]
      }
    }
  })
  return result
}

function ArraySectionForm({
  schema,
  values,
  onValuesChange,
}: {
  schema: ConfigSectionSchema
  values: Record<string, string | null>
  onValuesChange: (updated: Record<string, string | null>) => void
}) {
  const items = parseArrayItems(values, schema.properties)

  const handleItemFieldChange = (itemIndex: number, propName: string, value: string | null) => {
    const updated = items.map((item, i) =>
      i === itemIndex ? { ...item, [propName]: value } : item,
    )
    onValuesChange(flattenArrayItems(updated, schema.properties))
  }

  const handleAddItem = () => {
    const newItem: Record<string, string | null> = {}
    for (const prop of schema.properties) {
      newItem[prop.name] = prop.defaultValue || null
    }
    onValuesChange(flattenArrayItems([...items, newItem], schema.properties))
  }

  const handleRemoveItem = (index: number) => {
    const updated = items.filter((_, i) => i !== index)
    onValuesChange(flattenArrayItems(updated, schema.properties))
  }

  return (
    <div className="space-y-4">
      {items.map((item, idx) => (
        <div
          key={idx}
          className="rounded-xl border border-stone bg-basalt p-4"
        >
          <div className="mb-4 flex items-center justify-between">
            <span className="text-sm font-semibold text-amber">
              Item {idx}
            </span>
            <button
              type="button"
              onClick={() => handleRemoveItem(idx)}
              className="text-xs text-rose hover:text-rose transition-colors"
            >
              Remove
            </button>
          </div>
          <div className="space-y-4">
            {schema.properties.map((prop) => (
              <FieldEditor
                key={prop.name}
                property={prop}
                value={item[prop.name] ?? null}
                onChange={(val) => handleItemFieldChange(idx, prop.name, val)}
              />
            ))}
          </div>
        </div>
      ))}
      {items.length === 0 && (
        <p className="py-4 text-center text-sm text-dust">
          No items configured
        </p>
      )}
      <button
        type="button"
        onClick={handleAddItem}
        className="flex items-center gap-2 rounded-xl border border-dashed border-stone px-4 py-2 text-sm text-dust hover:border-amber hover:text-amber transition-colors"
      >
        <span className="text-lg leading-none">+</span>
        Add Item
      </button>
    </div>
  )
}

/* ------------------------------------------------------------------ */
/*  Section Form                                                       */
/* ------------------------------------------------------------------ */

function SectionForm({
  schema,
  values,
  sensitiveKeys,
  showSecrets,
  onValuesChange,
}: {
  schema: ConfigSectionSchema
  values: Record<string, string | null>
  sensitiveKeys: Set<string>
  showSecrets: boolean
  onValuesChange: (updated: Record<string, string | null>) => void
}) {
  // Connection strings get a specialized editor
  if (schema.section === 'ConnectionStrings') {
    return <ConnectionStringsEditor entries={values} sensitiveKeys={sensitiveKeys} showSecrets={showSecrets} onChange={onValuesChange} />
  }

  // Array sections get an indexed item editor
  if (schema.isArray) {
    return <ArraySectionForm schema={schema} values={values} onValuesChange={onValuesChange} />
  }

  const handleFieldChange = (name: string, value: string | null) => {
    onValuesChange({ ...values, [name]: value })
  }

  return (
    <div className="space-y-6">
      {schema.properties.map((prop) => (
        <FieldEditor
          key={prop.name}
          property={prop}
          value={values[prop.name] ?? null}
          showSecrets={showSecrets}
          onChange={(val) => handleFieldChange(prop.name, val)}
        />
      ))}
    </div>
  )
}

/* ------------------------------------------------------------------ */
/*  Field Editor                                                       */
/* ------------------------------------------------------------------ */

function FieldEditor({
  property,
  value,
  showSecrets,
  onChange,
}: {
  property: ConfigPropertySchema
  value: string | null
  showSecrets?: boolean
  onChange: (val: string | null) => void
}) {
  const { name, type, description, defaultValue, isSensitive } = property
  const displayValue = value ?? ''
  const isMasked = isSensitive && displayValue === '********'
  const usePasswordInput = isSensitive && !showSecrets

  const labelNode = (
    <div className="mb-1.5">
      <label className="block text-sm font-medium text-light">
        {name}
        {isSensitive && (
          <span className="ml-2 rounded bg-amber/20/20 px-1.5 py-0.5 text-[10px] font-semibold uppercase text-amber">
            sensitive
          </span>
        )}
      </label>
      {description && (
        <p className="mt-0.5 text-xs text-dust">{description}</p>
      )}
    </div>
  )

  if (type === 'boolean') {
    const checked = displayValue === 'true' || displayValue === 'True'
    return (
      <div>
        {labelNode}
        <div className="flex items-center gap-3">
          <ToggleSwitch
            checked={checked}
            onChange={(val) => onChange(val ? 'true' : 'false')}
          />
          <span className="text-sm text-dust">
            {checked ? 'Enabled' : 'Disabled'}
          </span>
        </div>
        {defaultValue !== undefined && defaultValue !== '' && (
          <p className="mt-1 text-xs text-dust">Default: {defaultValue}</p>
        )}
      </div>
    )
  }

  if (type === 'number') {
    return (
      <div>
        {labelNode}
        <input
          type="number"
          value={displayValue}
          onChange={(e) => onChange(e.target.value || null)}
          placeholder={defaultValue || '0'}
          className="w-full max-w-xs rounded-xl border border-stone bg-basalt px-3 py-2 text-sm text-light placeholder-dust/60 input-focus focus:ring-1 focus:ring-amber input-focus"
        />
        {defaultValue !== undefined && defaultValue !== '' && (
          <p className="mt-1 text-xs text-dust">Default: {defaultValue}</p>
        )}
      </div>
    )
  }

  if (type === 'array') {
    return (
      <div>
        {labelNode}
        <input
          type="text"
          value={displayValue}
          onChange={(e) => onChange(e.target.value || null)}
          placeholder={defaultValue || 'value1, value2, value3'}
          className="w-full rounded-xl border border-stone bg-basalt px-3 py-2 text-sm text-light placeholder-dust/60 input-focus focus:ring-1 focus:ring-amber input-focus"
        />
        <p className="mt-1 text-xs text-dust">Comma-separated values</p>
        {defaultValue !== undefined && defaultValue !== '' && (
          <p className="mt-0.5 text-xs text-dust">Default: {defaultValue}</p>
        )}
      </div>
    )
  }

  // Default: string (or unknown type)
  return (
    <div>
      {labelNode}
      <input
        type={usePasswordInput ? 'password' : 'text'}
        value={isMasked ? '' : displayValue}
        onChange={(e) => onChange(e.target.value || null)}
        placeholder={isMasked ? '••••••••  (enter new value to change)' : defaultValue || ''}
        className="w-full rounded-xl border border-stone bg-basalt px-3 py-2 text-sm text-light placeholder-dust/60 input-focus focus:ring-1 focus:ring-amber input-focus"
      />
      {defaultValue !== undefined && defaultValue !== '' && !isSensitive && (
        <p className="mt-1 text-xs text-dust">Default: {defaultValue}</p>
      )}
    </div>
  )
}

/* ------------------------------------------------------------------ */
/*  Music Assistant Test Button                                        */
/* ------------------------------------------------------------------ */

function MusicAssistantTestButton({ integrationId }: { integrationId: string }) {
  const [testing, setTesting] = useState(false)
  const [result, setResult] = useState<{ success: boolean; message: string } | null>(null)

  const handleTest = async () => {
    setTesting(true)
    setResult(null)
    try {
      const res = await testMusicAssistantIntegration(integrationId)
      setResult(res)
    } catch (err) {
      setResult({
        success: false,
        message: err instanceof Error ? err.message : 'Test request failed',
      })
    } finally {
      setTesting(false)
    }
  }

  return (
    <div className="mt-4 rounded-xl border border-stone bg-charcoal p-4">
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div>
          <h3 className="text-sm font-medium text-light">Test Integration</h3>
          <p className="mt-0.5 text-xs text-dust">
            Validates the IntegrationId by querying the Music Assistant library
          </p>
        </div>
        <button
          onClick={handleTest}
          disabled={testing || !integrationId}
          className={`shrink-0 rounded-xl px-4 py-2 text-sm font-medium transition-colors ${
            testing || !integrationId
              ? 'bg-stone text-dust cursor-not-allowed'
              : 'bg-amber text-void hover:bg-amber-glow'
          }`}
        >
          {testing ? (
            <span className="flex items-center gap-2">
              <svg className="h-4 w-4 animate-spin" viewBox="0 0 24 24" fill="none">
                <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z" />
              </svg>
              Testing...
            </span>
          ) : (
            'Test Connection'
          )}
        </button>
      </div>
      {result && (
        <div
          className={`mt-3 rounded-xl px-4 py-3 text-sm ${
            result.success
              ? 'bg-green-500/10 border border-green-500/30 text-sage'
              : 'bg-ember/10 border border-red-500/30 text-rose'
          }`}
        >
          {result.success ? '✓' : '✕'} {result.message}
        </div>
      )}
    </div>
  )
}

/* ------------------------------------------------------------------ */
/*  Main Page                                                          */
/* ------------------------------------------------------------------ */

export default function ConfigurationPage() {
  // Schema & section state
  const [schemas, setSchemas] = useState<ConfigSectionSchema[]>([])
  const [activeSection, setActiveSection] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [sectionLoading, setSectionLoading] = useState(false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Form values & dirty tracking
  const [formValues, setFormValues] = useState<Record<string, string | null>>({})
  const [originalValues, setOriginalValues] = useState<Record<string, string | null>>({})
  const [sensitiveKeys, setSensitiveKeys] = useState<Set<string>>(new Set())
  const [showSecrets, setShowSecrets] = useState(false)

  // Toast notifications
  const [toasts, setToasts] = useState<Toast[]>([])

  // Reset confirmation dialog
  const [resetDialogOpen, setResetDialogOpen] = useState(false)

  const isDirty = useCallback(() => {
    const formKeys = Object.keys(formValues)
    const origKeys = Object.keys(originalValues)
    if (formKeys.length !== origKeys.length) return true
    return formKeys.some((k) => formValues[k] !== originalValues[k])
  }, [formValues, originalValues])

  const showToast = useCallback((message: string, type: 'success' | 'error') => {
    const id = ++toastId
    setToasts((prev) => [...prev, { id, message, type }])
    setTimeout(() => {
      setToasts((prev) => prev.filter((t) => t.id !== id))
    }, 3000)
  }, [])

  const dismissToast = useCallback((id: number) => {
    setToasts((prev) => prev.filter((t) => t.id !== id))
  }, [])

  // Load schemas on mount
  useEffect(() => {
    let cancelled = false
    setLoading(true)
    fetchConfigSchema()
      .then((data) => {
        if (cancelled) return
        setSchemas(data)
        if (data.length > 0) {
          setActiveSection(data[0].section)
        }
      })
      .catch((err) => {
        if (cancelled) return
        setError(err instanceof Error ? err.message : 'Failed to load configuration schema')
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [])

  /** Convert API entries to form values, stripping the section prefix from keys.
   *  Also returns the set of short keys that are sensitive. */
  const entriesToValues = useCallback(
    (entries: ConfigEntryDto[], sectionSchema: ConfigSectionSchema | undefined): {
      values: Record<string, string | null>
      sensitive: Set<string>
    } => {
      const sensitive = new Set<string>()
      if (entries.length > 0) {
        const vals: Record<string, string | null> = {}
        for (const e of entries) {
          // Strip section prefix (e.g. "MusicAssistant:IntegrationId" → "IntegrationId")
          const shortKey = e.key.includes(':') ? e.key.split(':').slice(1).join(':') : e.key
          vals[shortKey] = e.value
          if (e.isSensitive) sensitive.add(shortKey)
        }
        return { values: vals, sensitive }
      }
      // No stored values — populate from schema defaults so the form isn't empty
      if (sectionSchema && !sectionSchema.isArray) {
        const defaults: Record<string, string | null> = {}
        for (const prop of sectionSchema.properties) {
          defaults[prop.name] = prop.defaultValue || null
          if (prop.isSensitive) sensitive.add(prop.name)
        }
        return { values: defaults, sensitive }
      }
      return { values: {}, sensitive }
    },
    [],
  )

  // Load section values when active section or showSecrets changes
  useEffect(() => {
    if (!activeSection) return
    let cancelled = false
    setSectionLoading(true)
    const sectionSchema = schemas.find((s) => s.section === activeSection)

    fetchConfigSection(activeSection, showSecrets)
      .then((entries: ConfigEntryDto[]) => {
        if (cancelled) return
        const { values: vals, sensitive } = entriesToValues(entries, sectionSchema)
        setFormValues(vals)
        setOriginalValues(vals)
        setSensitiveKeys(sensitive)
      })
      .catch((err) => {
        if (cancelled) return
        showToast(
          `Failed to load section "${activeSection}": ${err instanceof Error ? err.message : 'Unknown error'}`,
          'error',
        )
        setFormValues({})
        setOriginalValues({})
      })
      .finally(() => {
        if (!cancelled) setSectionLoading(false)
      })

    return () => {
      cancelled = true
    }
  }, [activeSection, showSecrets, showToast, schemas, entriesToValues])

  const handleSectionChange = useCallback(
    (section: string) => {
      if (isDirty()) {
        const proceed = window.confirm(
          'You have unsaved changes. Discard and switch sections?',
        )
        if (!proceed) return
      }
      setActiveSection(section)
    },
    [isDirty],
  )

  const handleSave = useCallback(async () => {
    if (!activeSection) return
    setSaving(true)
    try {
      // Only send changed values, and skip masked sensitive values the user didn't touch
      const changed: Record<string, string | null> = {}
      for (const key of Object.keys(formValues)) {
        if (formValues[key] !== originalValues[key]) {
          // Skip empty-string overwrites on masked sensitive fields (user didn't type anything)
          if (
            originalValues[key] === '********' &&
            (formValues[key] === '' || formValues[key] === null)
          ) {
            continue
          }
          changed[key] = formValues[key]
        }
      }

      if (Object.keys(changed).length === 0) {
        showToast('No changes to save', 'success')
        setSaving(false)
        return
      }

      const count = await updateConfigSection(activeSection, changed)
      showToast(`Saved ${count} setting${count !== 1 ? 's' : ''} successfully`, 'success')

      // Refresh values
      const entries = await fetchConfigSection(activeSection, showSecrets)
      const sectionSchema = schemas.find((s) => s.section === activeSection)
      const { values: vals, sensitive } = entriesToValues(entries, sectionSchema)
      setFormValues(vals)
      setOriginalValues(vals)
      setSensitiveKeys(sensitive)
    } catch (err) {
      showToast(
        `Save failed: ${err instanceof Error ? err.message : 'Unknown error'}`,
        'error',
      )
    } finally {
      setSaving(false)
    }
  }, [activeSection, formValues, originalValues, showSecrets, showToast, schemas, entriesToValues])

  const handleReset = useCallback(async () => {
    setResetDialogOpen(false)
    try {
      const msg = await resetConfig()
      showToast(msg || 'Configuration reset to defaults', 'success')
      // Reload current section
      if (activeSection) {
        const entries = await fetchConfigSection(activeSection, showSecrets)
        const sectionSchema = schemas.find((s) => s.section === activeSection)
        const { values: vals, sensitive } = entriesToValues(entries, sectionSchema)
        setFormValues(vals)
        setOriginalValues(vals)
        setSensitiveKeys(sensitive)
      }
    } catch (err) {
      showToast(
        `Reset failed: ${err instanceof Error ? err.message : 'Unknown error'}`,
        'error',
      )
    }
  }, [activeSection, showSecrets, showToast, schemas, entriesToValues])

  const handleDiscard = useCallback(() => {
    setFormValues({ ...originalValues })
  }, [originalValues])

  const activeSchema = schemas.find((s) => s.section === activeSection) ?? null

  // ── Loading / Error states ────────────────────────────────────────

  if (loading) {
    return (
      <div className="flex h-full items-center justify-center bg-void/50 text-dust">
        <svg
          className="mr-3 h-5 w-5 animate-spin text-amber"
          viewBox="0 0 24 24"
          fill="none"
        >
          <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
          <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z" />
        </svg>
        Loading configuration…
      </div>
    )
  }

  if (error) {
    return (
      <div className="flex h-full items-center justify-center bg-void/50">
        <div className="rounded-xl bg-charcoal p-8 text-center shadow-lg border border-stone">
          <p className="text-rose text-lg font-medium">Failed to load configuration</p>
          <p className="mt-2 text-sm text-dust">{error}</p>
        </div>
      </div>
    )
  }

  // ── Main layout ───────────────────────────────────────────────────

  return (
    <div className="flex h-full flex-col bg-void/50 text-light">
      {/* Toast notifications */}
      <ToastContainer toasts={toasts} onDismiss={dismissToast} />

      {/* Reset confirmation dialog */}
      <ConfirmDialog
        open={resetDialogOpen}
        title="Reset All Configuration"
        message="This will reset every configuration value back to its default. This action cannot be undone. Are you sure?"
        onConfirm={handleReset}
        onCancel={() => setResetDialogOpen(false)}
      />

      {/* Header */}
      <header className="flex flex-col gap-3 border-b border-stone px-4 py-4 sm:flex-row sm:items-center sm:justify-between sm:px-6">
        <div>
          <h1 className="font-display text-xl font-semibold text-light">Configuration</h1>
          <p className="mt-0.5 text-sm text-dust">
            Manage platform settings and connection strings
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-3">
          <div className="flex items-center gap-2">
            <ToggleSwitch checked={showSecrets} onChange={setShowSecrets} />
            <span className="text-sm text-dust">
              {showSecrets ? (
                <span className="flex items-center gap-1 text-amber">
                  <span>👁</span> Secrets visible
                </span>
              ) : (
                'Show secrets'
              )}
            </span>
          </div>
          <button
            onClick={() => setResetDialogOpen(true)}
            className="rounded-xl border border-red-500/30 bg-ember/10 px-4 py-2 text-sm font-medium text-rose hover:bg-ember/15 transition-colors"
          >
            Reset All
          </button>
        </div>
      </header>

      {/* Body: sidebar + form */}
      <div className="flex flex-1 flex-col overflow-hidden md:flex-row">
        {/* Mobile section selector */}
        <div className="border-b border-stone bg-basalt/60 px-4 py-3 md:hidden">
          <label className="mb-1 block text-xs text-dust">Section</label>
          <select
            value={activeSection ?? ''}
            onChange={(e) => handleSectionChange(e.target.value)}
            className="w-full rounded-xl border border-stone bg-basalt px-3 py-2 text-sm text-light"
          >
            {schemas.map((s) => (
              <option key={s.section} value={s.section}>
                {s.section}{s.description ? ` — ${s.description}` : ''}
              </option>
            ))}
          </select>
        </div>

        {/* Desktop section sidebar */}
        <nav className="hidden w-64 shrink-0 overflow-y-auto border-r border-stone bg-basalt/60 py-2 md:block">
          {schemas.map((s) => {
            const isActive = s.section === activeSection
            return (
              <button
                key={s.section}
                onClick={() => handleSectionChange(s.section)}
                className={`flex w-full items-center gap-3 px-4 py-3 text-left text-sm transition-colors ${
                  isActive
                    ? 'bg-amber-glow/10 border-r-2 border-amber text-light font-medium'
                    : 'text-dust hover:bg-basalt/50 hover:text-light'
                }`}
              >
                <span
                  className={`flex h-8 w-8 shrink-0 items-center justify-center rounded-xl text-xs font-bold ${
                    isActive
                      ? 'bg-amber-glow/20 text-amber'
                      : 'bg-basalt text-dust'
                  }`}
                >
                  {s.section.charAt(0).toUpperCase()}
                </span>
                <div className="min-w-0 flex-1">
                  <span className="block truncate">{s.section}</span>
                  {s.description && (
                    <span className="block truncate text-xs text-dust">
                      {s.description}
                    </span>
                  )}
                </div>
              </button>
            )
          })}
          {schemas.length === 0 && (
            <p className="px-4 py-6 text-center text-sm text-dust">
              No configuration sections found
            </p>
          )}
        </nav>

        {/* Form panel */}
        <main className="flex-1 overflow-y-auto p-4 sm:p-6">
          {!activeSchema ? (
            <div className="flex h-full items-center justify-center text-dust">
              Select a section to configure
            </div>
          ) : sectionLoading ? (
            <div className="flex h-full items-center justify-center text-dust">
              <svg
                className="mr-3 h-5 w-5 animate-spin text-amber"
                viewBox="0 0 24 24"
                fill="none"
              >
                <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z" />
              </svg>
              Loading section…
            </div>
          ) : (
            <div className="mx-auto max-w-3xl">
              {/* Section header */}
              <div className="mb-6">
                <h2 className="text-lg font-semibold text-light">{activeSchema.section}</h2>
                {activeSchema.description && (
                  <p className="mt-1 text-sm text-dust">{activeSchema.description}</p>
                )}
              </div>

              {/* Form */}
              <div className="rounded-xl border border-stone bg-charcoal p-4 sm:p-6">
                <SectionForm
                  schema={activeSchema}
                  values={formValues}
                  sensitiveKeys={sensitiveKeys}
                  showSecrets={showSecrets}
                  onValuesChange={setFormValues}
                />
              </div>

              {/* Section-specific actions */}
              {activeSchema.section === 'MusicAssistant' && (
                <MusicAssistantTestButton
                  integrationId={formValues['IntegrationId'] ?? ''}
                />
              )}

              {/* Action bar */}
              <div className="mt-6 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                <div>
                  {isDirty() && (
                    <span className="flex items-center gap-2 text-sm text-amber">
                      <span className="inline-block h-2 w-2 rounded-full bg-yellow-400" />
                      Unsaved changes
                    </span>
                  )}
                </div>
                <div className="flex gap-3">
                  {isDirty() && (
                    <button
                      onClick={handleDiscard}
                      className="rounded-xl bg-basalt px-4 py-2 text-sm font-medium text-fog hover:bg-stone transition-colors"
                    >
                      Discard
                    </button>
                  )}
                  <button
                    onClick={handleSave}
                    disabled={saving || !isDirty()}
                    className={`rounded-xl px-5 py-2 text-sm font-medium transition-colors ${
                      saving || !isDirty()
                        ? 'bg-amber/40 text-dust cursor-not-allowed'
                        : 'bg-amber text-void hover:bg-amber-glow'
                    }`}
                  >
                    {saving ? (
                      <span className="flex items-center gap-2">
                        <svg
                          className="h-4 w-4 animate-spin"
                          viewBox="0 0 24 24"
                          fill="none"
                        >
                          <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                          <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z" />
                        </svg>
                        Saving…
                      </span>
                    ) : (
                      'Save Changes'
                    )}
                  </button>
                </div>
              </div>
            </div>
          )}
        </main>
      </div>
    </div>
  )
}
