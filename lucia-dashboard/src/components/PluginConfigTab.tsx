import { useEffect, useState, useCallback, useRef } from 'react'
import { Save, RotateCcw, Eye, EyeOff, AlertCircle } from 'lucide-react'
import type { PluginConfigSchema, PluginConfigPropertySchema } from '../types'
import {
  fetchPluginConfigSchemas,
  fetchConfigSection,
  updateConfigSection,
} from '../api'

interface ConfigEntryDto {
  key: string
  value: string | null
  isSensitive: boolean
  updatedAt: string
  updatedBy: string
}

interface Toast {
  id: number
  message: string
  type: 'success' | 'error'
}

export default function PluginConfigTab() {
  const [schemas, setSchemas] = useState<PluginConfigSchema[]>([])
  const [loading, setLoading] = useState(true)
  const [sectionValues, setSectionValues] = useState<Record<string, Record<string, string>>>({})
  const [dirtyFields, setDirtyFields] = useState<Record<string, Set<string>>>({})
  const [showSecrets, setShowSecrets] = useState<Record<string, boolean>>({})
  const [saving, setSaving] = useState<Record<string, boolean>>({})
  const [toasts, setToasts] = useState<Toast[]>([])
  const toastIdRef = useRef(0)

  const addToast = useCallback((message: string, type: 'success' | 'error') => {
    const id = ++toastIdRef.current
    setToasts(prev => [...prev, { id, message, type }])
    setTimeout(() => setToasts(prev => prev.filter(t => t.id !== id)), 3000)
  }, [])

  const loadSchemas = useCallback(async () => {
    setLoading(true)
    try {
      const result = await fetchPluginConfigSchemas()
      setSchemas(result)

      // Load current values for each plugin's config section
      const values: Record<string, Record<string, string>> = {}
      for (const schema of result) {
        try {
          const entries: ConfigEntryDto[] = await fetchConfigSection(schema.section, true)
          const sectionVals: Record<string, string> = {}
          for (const prop of schema.properties) {
            const fullKey = `${schema.section}:${prop.name}`
            const entry = entries.find(e => e.key === fullKey)
            sectionVals[prop.name] = entry?.value ?? prop.defaultValue ?? ''
          }
          values[schema.section] = sectionVals
        } catch {
          // Section not yet stored — use defaults
          const sectionVals: Record<string, string> = {}
          for (const prop of schema.properties) {
            sectionVals[prop.name] = prop.defaultValue ?? ''
          }
          values[schema.section] = sectionVals
        }
      }
      setSectionValues(values)
    } catch {
      addToast('Failed to load plugin configuration', 'error')
    } finally {
      setLoading(false)
    }
  }, [addToast])

  useEffect(() => {
    loadSchemas()
  }, [loadSchemas])

  const handleChange = (section: string, propName: string, value: string) => {
    setSectionValues(prev => ({
      ...prev,
      [section]: { ...prev[section], [propName]: value },
    }))
    setDirtyFields(prev => {
      const sectionDirty = new Set(prev[section] ?? [])
      sectionDirty.add(propName)
      return { ...prev, [section]: sectionDirty }
    })
  }

  const handleSave = async (schema: PluginConfigSchema) => {
    const dirty = dirtyFields[schema.section]
    if (!dirty || dirty.size === 0) return

    setSaving(prev => ({ ...prev, [schema.section]: true }))
    try {
      const vals: Record<string, string | null> = {}
      for (const propName of dirty) {
        vals[`${schema.section}:${propName}`] = sectionValues[schema.section]?.[propName] ?? null
      }
      await updateConfigSection(schema.section, vals)
      setDirtyFields(prev => ({ ...prev, [schema.section]: new Set() }))
      addToast(`${schema.pluginId} configuration saved`, 'success')
    } catch {
      addToast(`Failed to save ${schema.pluginId} configuration`, 'error')
    } finally {
      setSaving(prev => ({ ...prev, [schema.section]: false }))
    }
  }

  const handleDiscard = (schema: PluginConfigSchema) => {
    // Reload values from server
    loadSchemas()
    setDirtyFields(prev => ({ ...prev, [schema.section]: new Set() }))
  }

  const toggleSecrets = (section: string) => {
    setShowSecrets(prev => ({ ...prev, [section]: !prev[section] }))
  }

  const isDirty = (section: string) => (dirtyFields[section]?.size ?? 0) > 0

  if (loading) {
    return <p className="py-8 text-center text-sm text-fog">Loading plugin configuration…</p>
  }

  if (schemas.length === 0) {
    return (
      <div className="flex flex-col items-center gap-2 py-12 text-center">
        <AlertCircle className="h-8 w-8 text-fog/40" />
        <p className="text-sm text-fog">No plugins have configurable settings.</p>
        <p className="text-xs text-fog/60">
          Plugins can declare configuration properties that will appear here.
        </p>
      </div>
    )
  }

  return (
    <div className="space-y-6">
      {schemas.map(schema => (
        <div
          key={schema.pluginId}
          className="rounded-lg border border-stone/40 bg-obsidian"
        >
          {/* Section Header */}
          <div className="flex items-center justify-between border-b border-stone/40 px-4 py-3">
            <div>
              <h3 className="text-sm font-medium text-light">{schema.pluginId}</h3>
              {schema.description && (
                <p className="mt-0.5 text-xs text-fog">{schema.description}</p>
              )}
            </div>
            <div className="flex items-center gap-2">
              {schema.properties.some(p => p.isSensitive) && (
                <button
                  onClick={() => toggleSecrets(schema.section)}
                  className="flex items-center gap-1 rounded px-2 py-1 text-xs text-fog hover:bg-stone/40 hover:text-light"
                  title={showSecrets[schema.section] ? 'Hide secrets' : 'Show secrets'}
                >
                  {showSecrets[schema.section] ? (
                    <EyeOff className="h-3.5 w-3.5" />
                  ) : (
                    <Eye className="h-3.5 w-3.5" />
                  )}
                  {showSecrets[schema.section] ? 'Hide' : 'Show'} secrets
                </button>
              )}
            </div>
          </div>

          {/* Fields */}
          <div className="space-y-4 p-4">
            {schema.properties.map(prop => (
              <FieldRow
                key={prop.name}
                prop={prop}
                value={sectionValues[schema.section]?.[prop.name] ?? ''}
                showSecret={showSecrets[schema.section] ?? false}
                onChange={v => handleChange(schema.section, prop.name, v)}
              />
            ))}
          </div>

          {/* Actions */}
          {isDirty(schema.section) && (
            <div className="flex items-center justify-end gap-2 border-t border-stone/40 px-4 py-3">
              <button
                onClick={() => handleDiscard(schema)}
                className="flex items-center gap-1.5 rounded-lg border border-stone/40 px-3 py-1.5 text-sm text-fog hover:bg-stone/40 hover:text-light"
              >
                <RotateCcw className="h-3.5 w-3.5" />
                Discard
              </button>
              <button
                onClick={() => handleSave(schema)}
                disabled={saving[schema.section]}
                className="flex items-center gap-1.5 rounded-lg bg-amber/20 px-3 py-1.5 text-sm font-medium text-amber hover:bg-amber/30 disabled:opacity-50"
              >
                <Save className="h-3.5 w-3.5" />
                {saving[schema.section] ? 'Saving…' : 'Save'}
              </button>
            </div>
          )}
        </div>
      ))}

      {/* Toasts */}
      <div className="fixed bottom-4 right-4 z-50 flex flex-col gap-2">
        {toasts.map(t => (
          <div
            key={t.id}
            className={`rounded-lg px-4 py-3 text-sm shadow-lg ${
              t.type === 'success' ? 'bg-sage/20 text-light' : 'bg-rose/20 text-light'
            }`}
          >
            {t.message}
          </div>
        ))}
      </div>
    </div>
  )
}

function FieldRow({
  prop,
  value,
  showSecret,
  onChange,
}: {
  prop: PluginConfigPropertySchema
  value: string
  showSecret: boolean
  onChange: (v: string) => void
}) {
  const isMasked = prop.isSensitive && !showSecret

  return (
    <div>
      <label className="mb-1 block text-xs font-medium text-fog">
        {prop.name}
        {prop.isSensitive && (
          <span className="ml-1.5 text-[10px] text-amber/60">sensitive</span>
        )}
      </label>
      <input
        type={isMasked ? 'password' : 'text'}
        value={value}
        onChange={e => onChange(e.target.value)}
        placeholder={prop.defaultValue || prop.description}
        className="w-full rounded-lg border border-stone/40 bg-basalt px-3 py-2 text-sm text-light placeholder:text-fog/40 focus:border-amber/50 focus:outline-none focus:ring-1 focus:ring-amber/30"
      />
      {prop.description && (
        <p className="mt-1 text-[11px] text-fog/60">{prop.description}</p>
      )}
    </div>
  )
}
