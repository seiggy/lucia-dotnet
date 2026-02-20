import { useState, useEffect, useCallback } from 'react'
import {
  fetchConfigSchema,
  fetchConfigSection,
  updateConfigSection,
  resetConfig,
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
          className={`flex items-center gap-3 rounded-lg px-4 py-3 shadow-lg text-sm font-medium transition-all duration-300 ${
            t.type === 'success'
              ? 'bg-green-600 text-white'
              : 'bg-red-600 text-white'
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
      <div className="w-full max-w-md rounded-xl bg-gray-800 p-6 shadow-2xl border border-gray-700">
        <h3 className="text-lg font-semibold text-white">{title}</h3>
        <p className="mt-2 text-sm text-gray-400">{message}</p>
        <div className="mt-6 flex justify-end gap-3">
          <button
            onClick={onCancel}
            className="rounded-lg bg-gray-700 px-4 py-2 text-sm font-medium text-gray-300 hover:bg-gray-600 transition-colors"
          >
            Cancel
          </button>
          <button
            onClick={onConfirm}
            className="rounded-lg bg-red-600 px-4 py-2 text-sm font-medium text-white hover:bg-red-500 transition-colors"
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
      className={`relative inline-flex h-6 w-11 shrink-0 cursor-pointer rounded-full border-2 border-transparent transition-colors duration-200 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:ring-offset-2 focus:ring-offset-gray-900 ${
        checked ? 'bg-indigo-500' : 'bg-gray-600'
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
  onChange,
}: {
  entries: Record<string, string | null>
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
        return (
          <div key={key} className="rounded-lg border border-gray-600 bg-gray-750 p-4">
            <div className="flex items-center justify-between mb-2">
              <label className="text-sm font-medium text-indigo-400">{key}</label>
              <button
                type="button"
                onClick={() => handleRemove(key)}
                className="text-xs text-red-400 hover:text-red-300 transition-colors"
              >
                Remove
              </button>
            </div>
            <input
              type="text"
              value={value ?? ''}
              onChange={(e) => handleValueChange(key, e.target.value)}
              className="w-full rounded-lg border border-gray-600 bg-gray-700 px-3 py-2 text-sm text-white font-mono placeholder-gray-500 focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500 focus:outline-none"
              placeholder="Server=...;Database=...;..."
            />
          </div>
        )
      })}
      <button
        type="button"
        onClick={handleAddNew}
        className="flex items-center gap-2 rounded-lg border border-dashed border-gray-600 px-4 py-2 text-sm text-gray-400 hover:border-indigo-500 hover:text-indigo-400 transition-colors"
      >
        <span className="text-lg leading-none">+</span>
        Add Connection String
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
  onValuesChange,
}: {
  schema: ConfigSectionSchema
  values: Record<string, string | null>
  onValuesChange: (updated: Record<string, string | null>) => void
}) {
  // Connection strings get a specialized editor
  if (schema.section === 'ConnectionStrings') {
    return <ConnectionStringsEditor entries={values} onChange={onValuesChange} />
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
  onChange,
}: {
  property: ConfigPropertySchema
  value: string | null
  onChange: (val: string | null) => void
}) {
  const { name, type, description, defaultValue, isSensitive } = property
  const displayValue = value ?? ''
  const isMasked = isSensitive && displayValue === '********'

  const labelNode = (
    <div className="mb-1.5">
      <label className="block text-sm font-medium text-gray-200">
        {name}
        {isSensitive && (
          <span className="ml-2 rounded bg-yellow-600/20 px-1.5 py-0.5 text-[10px] font-semibold uppercase text-yellow-400">
            sensitive
          </span>
        )}
      </label>
      {description && (
        <p className="mt-0.5 text-xs text-gray-500">{description}</p>
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
          <span className="text-sm text-gray-400">
            {checked ? 'Enabled' : 'Disabled'}
          </span>
        </div>
        {defaultValue !== undefined && defaultValue !== '' && (
          <p className="mt-1 text-xs text-gray-600">Default: {defaultValue}</p>
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
          className="w-full max-w-xs rounded-lg border border-gray-600 bg-gray-700 px-3 py-2 text-sm text-white placeholder-gray-500 focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500 focus:outline-none"
        />
        {defaultValue !== undefined && defaultValue !== '' && (
          <p className="mt-1 text-xs text-gray-600">Default: {defaultValue}</p>
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
          className="w-full rounded-lg border border-gray-600 bg-gray-700 px-3 py-2 text-sm text-white placeholder-gray-500 focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500 focus:outline-none"
        />
        <p className="mt-1 text-xs text-gray-500">Comma-separated values</p>
        {defaultValue !== undefined && defaultValue !== '' && (
          <p className="mt-0.5 text-xs text-gray-600">Default: {defaultValue}</p>
        )}
      </div>
    )
  }

  // Default: string (or unknown type)
  return (
    <div>
      {labelNode}
      <input
        type={isSensitive ? 'password' : 'text'}
        value={isMasked ? '' : displayValue}
        onChange={(e) => onChange(e.target.value || null)}
        placeholder={isMasked ? '••••••••  (enter new value to change)' : defaultValue || ''}
        className="w-full rounded-lg border border-gray-600 bg-gray-700 px-3 py-2 text-sm text-white placeholder-gray-500 focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500 focus:outline-none"
      />
      {defaultValue !== undefined && defaultValue !== '' && !isSensitive && (
        <p className="mt-1 text-xs text-gray-600">Default: {defaultValue}</p>
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

  // Load section values when active section or showSecrets changes
  useEffect(() => {
    if (!activeSection) return
    let cancelled = false
    setSectionLoading(true)

    fetchConfigSection(activeSection, showSecrets)
      .then((entries: ConfigEntryDto[]) => {
        if (cancelled) return
        const vals: Record<string, string | null> = {}
        for (const e of entries) {
          // Strip section prefix (e.g. "MusicAssistant:IntegrationId" → "IntegrationId")
          const shortKey = e.key.includes(':') ? e.key.split(':').slice(1).join(':') : e.key
          vals[shortKey] = e.value
        }
        setFormValues(vals)
        setOriginalValues(vals)
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
  }, [activeSection, showSecrets, showToast])

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
      const vals: Record<string, string | null> = {}
      for (const e of entries) {
        const shortKey = e.key.includes(':') ? e.key.split(':').slice(1).join(':') : e.key
        vals[shortKey] = e.value
      }
      setFormValues(vals)
      setOriginalValues(vals)
    } catch (err) {
      showToast(
        `Save failed: ${err instanceof Error ? err.message : 'Unknown error'}`,
        'error',
      )
    } finally {
      setSaving(false)
    }
  }, [activeSection, formValues, originalValues, showSecrets, showToast])

  const handleReset = useCallback(async () => {
    setResetDialogOpen(false)
    try {
      const msg = await resetConfig()
      showToast(msg || 'Configuration reset to defaults', 'success')
      // Reload current section
      if (activeSection) {
        const entries = await fetchConfigSection(activeSection, showSecrets)
        const vals: Record<string, string | null> = {}
        for (const e of entries) {
          vals[e.key] = e.value
        }
        setFormValues(vals)
        setOriginalValues(vals)
      }
    } catch (err) {
      showToast(
        `Reset failed: ${err instanceof Error ? err.message : 'Unknown error'}`,
        'error',
      )
    }
  }, [activeSection, showSecrets, showToast])

  const handleDiscard = useCallback(() => {
    setFormValues({ ...originalValues })
  }, [originalValues])

  const activeSchema = schemas.find((s) => s.section === activeSection) ?? null

  // ── Loading / Error states ────────────────────────────────────────

  if (loading) {
    return (
      <div className="flex h-full items-center justify-center bg-gray-900 text-gray-400">
        <svg
          className="mr-3 h-5 w-5 animate-spin text-indigo-500"
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
      <div className="flex h-full items-center justify-center bg-gray-900">
        <div className="rounded-xl bg-gray-800 p-8 text-center shadow-lg border border-gray-700">
          <p className="text-red-400 text-lg font-medium">Failed to load configuration</p>
          <p className="mt-2 text-sm text-gray-500">{error}</p>
        </div>
      </div>
    )
  }

  // ── Main layout ───────────────────────────────────────────────────

  return (
    <div className="flex h-full flex-col bg-gray-900 text-white">
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
      <header className="flex items-center justify-between border-b border-gray-700 px-6 py-4">
        <div>
          <h1 className="text-xl font-semibold">Configuration</h1>
          <p className="mt-0.5 text-sm text-gray-400">
            Manage platform settings and connection strings
          </p>
        </div>
        <div className="flex items-center gap-4">
          <div className="flex items-center gap-2">
            <ToggleSwitch checked={showSecrets} onChange={setShowSecrets} />
            <span className="text-sm text-gray-400">
              {showSecrets ? (
                <span className="flex items-center gap-1 text-yellow-400">
                  <span>👁</span> Secrets visible
                </span>
              ) : (
                'Show secrets'
              )}
            </span>
          </div>
          <button
            onClick={() => setResetDialogOpen(true)}
            className="rounded-lg border border-red-500/30 bg-red-500/10 px-4 py-2 text-sm font-medium text-red-400 hover:bg-red-500/20 transition-colors"
          >
            Reset All
          </button>
        </div>
      </header>

      {/* Body: sidebar + form */}
      <div className="flex flex-1 overflow-hidden">
        {/* Section sidebar */}
        <nav className="w-64 shrink-0 overflow-y-auto border-r border-gray-700 bg-gray-800/50 py-2">
          {schemas.map((s) => {
            const isActive = s.section === activeSection
            return (
              <button
                key={s.section}
                onClick={() => handleSectionChange(s.section)}
                className={`flex w-full items-center gap-3 px-4 py-3 text-left text-sm transition-colors ${
                  isActive
                    ? 'bg-indigo-500/10 border-r-2 border-indigo-500 text-white font-medium'
                    : 'text-gray-400 hover:bg-gray-700/50 hover:text-gray-200'
                }`}
              >
                <span
                  className={`flex h-8 w-8 shrink-0 items-center justify-center rounded-lg text-xs font-bold ${
                    isActive
                      ? 'bg-indigo-500/20 text-indigo-400'
                      : 'bg-gray-700 text-gray-500'
                  }`}
                >
                  {s.section.charAt(0).toUpperCase()}
                </span>
                <div className="min-w-0 flex-1">
                  <span className="block truncate">{s.section}</span>
                  {s.description && (
                    <span className="block truncate text-xs text-gray-500">
                      {s.description}
                    </span>
                  )}
                </div>
              </button>
            )
          })}
          {schemas.length === 0 && (
            <p className="px-4 py-6 text-center text-sm text-gray-600">
              No configuration sections found
            </p>
          )}
        </nav>

        {/* Form panel */}
        <main className="flex-1 overflow-y-auto p-6">
          {!activeSchema ? (
            <div className="flex h-full items-center justify-center text-gray-600">
              Select a section to configure
            </div>
          ) : sectionLoading ? (
            <div className="flex h-full items-center justify-center text-gray-400">
              <svg
                className="mr-3 h-5 w-5 animate-spin text-indigo-500"
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
                <h2 className="text-lg font-semibold text-white">{activeSchema.section}</h2>
                {activeSchema.description && (
                  <p className="mt-1 text-sm text-gray-400">{activeSchema.description}</p>
                )}
              </div>

              {/* Form */}
              <div className="rounded-xl border border-gray-700 bg-gray-800 p-6">
                <SectionForm
                  schema={activeSchema}
                  values={formValues}
                  onValuesChange={setFormValues}
                />
              </div>

              {/* Action bar */}
              <div className="mt-6 flex items-center justify-between">
                <div>
                  {isDirty() && (
                    <span className="flex items-center gap-2 text-sm text-yellow-400">
                      <span className="inline-block h-2 w-2 rounded-full bg-yellow-400" />
                      Unsaved changes
                    </span>
                  )}
                </div>
                <div className="flex gap-3">
                  {isDirty() && (
                    <button
                      onClick={handleDiscard}
                      className="rounded-lg bg-gray-700 px-4 py-2 text-sm font-medium text-gray-300 hover:bg-gray-600 transition-colors"
                    >
                      Discard
                    </button>
                  )}
                  <button
                    onClick={handleSave}
                    disabled={saving || !isDirty()}
                    className={`rounded-lg px-5 py-2 text-sm font-medium text-white transition-colors ${
                      saving || !isDirty()
                        ? 'bg-indigo-500/40 cursor-not-allowed'
                        : 'bg-indigo-500 hover:bg-indigo-400'
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
