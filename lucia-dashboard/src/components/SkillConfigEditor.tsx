import { useState, useCallback } from 'react'
import type { SkillConfigSectionData } from '../api'
import { updateSkillConfig } from '../api'
import { Save, Plus, X } from 'lucide-react'

interface SkillConfigEditorProps {
  agentId: string
  sections: SkillConfigSectionData[]
  onSaved?: () => void
}

/**
 * Dynamic form editor for agent skill configuration sections.
 * Renders controls based on schema types returned by the skill-config API.
 * Writes to the MongoDB config store, which hot-reloads via IOptionsMonitor.
 */
export default function SkillConfigEditor({ agentId, sections, onSaved }: SkillConfigEditorProps) {
  const [editedValues, setEditedValues] = useState<Record<string, Record<string, unknown>>>(() => {
    const initial: Record<string, Record<string, unknown>> = {}
    for (const s of sections) {
      initial[s.sectionName] = { ...s.values }
    }
    return initial
  })
  const [saving, setSaving] = useState<string | null>(null)
  const [savedSection, setSavedSection] = useState<string | null>(null)

  const updateValue = useCallback((section: string, key: string, value: unknown) => {
    setEditedValues((prev) => ({
      ...prev,
      [section]: { ...prev[section], [key]: value },
    }))
    setSavedSection(null)
  }, [])

  const handleSave = useCallback(
    async (section: string) => {
      setSaving(section)
      try {
        await updateSkillConfig(agentId, section, editedValues[section])
        setSavedSection(section)
        onSaved?.()
      } catch (e) {
        console.error('Failed to save skill config:', e)
      } finally {
        setSaving(null)
      }
    },
    [agentId, editedValues, onSaved]
  )

  if (sections.length === 0) return null

  return (
    <div className="space-y-4">
      <h3 className="text-sm font-semibold text-light">Skill Configuration</h3>
      {sections.map((section) => (
        <div
          key={section.sectionName}
          className="rounded-xl border border-stone/40 bg-basalt/60 p-4 space-y-3"
        >
          <div className="flex items-center justify-between">
            <span className="text-xs font-medium uppercase tracking-wider text-dust">
              {section.displayName}
            </span>
            <button
              onClick={() => handleSave(section.sectionName)}
              disabled={saving === section.sectionName}
              className="flex items-center gap-1.5 rounded-lg bg-amber/15 px-3 py-1 text-xs font-medium text-amber hover:bg-amber/25 disabled:opacity-40"
            >
              <Save className="h-3 w-3" />
              {saving === section.sectionName
                ? 'Saving...'
                : savedSection === section.sectionName
                  ? 'Saved ✓'
                  : 'Save'}
            </button>
          </div>

          {section.schema.map((prop) => (
            <FieldEditor
              key={prop.name}
              name={prop.name}
              type={prop.type}
              value={editedValues[section.sectionName]?.[prop.name]}
              defaultValue={prop.defaultValue}
              onChange={(v) => updateValue(section.sectionName, prop.name, v)}
            />
          ))}
        </div>
      ))}
    </div>
  )
}

function FieldEditor({
  name,
  type,
  value,
  onChange,
}: {
  name: string
  type: string
  value: unknown
  defaultValue: unknown
  onChange: (v: unknown) => void
}) {
  const label = name.replace(/([A-Z])/g, ' $1').trim()

  if (type === 'string[]') {
    const items = (Array.isArray(value) ? value : []) as string[]
    return (
      <div>
        <label className="mb-1 block text-xs font-medium text-fog">{label}</label>
        <div className="flex flex-wrap items-center gap-1.5">
          {items.map((item, i) => (
            <span
              key={i}
              className="inline-flex items-center gap-1 rounded-md bg-sky-400/15 px-2 py-0.5 text-xs font-medium text-sky-400"
            >
              {item}
              <button
                type="button"
                onClick={() => onChange(items.filter((_, j) => j !== i))}
                className="text-sky-400/60 hover:text-sky-400"
              >
                <X className="h-3 w-3" />
              </button>
            </span>
          ))}
          <AddTagButton
            onAdd={(tag) => {
              if (tag && !items.includes(tag)) onChange([...items, tag])
            }}
          />
        </div>
      </div>
    )
  }

  if (type === 'number') {
    const numValue = typeof value === 'number' ? value : parseFloat(String(value ?? '0'))
    return (
      <div>
        <label className="mb-1 block text-xs font-medium text-fog">{label}</label>
        <div className="flex items-center gap-3">
          <input
            type="range"
            min={0}
            max={1}
            step={0.01}
            value={numValue}
            onChange={(e) => onChange(parseFloat(e.target.value))}
            className="flex-1 accent-amber"
          />
          <input
            type="number"
            step={0.01}
            value={numValue}
            onChange={(e) => onChange(parseFloat(e.target.value) || 0)}
            className="w-20 rounded border border-stone/30 bg-ash/40 px-2 py-1 text-xs text-light text-right"
          />
        </div>
      </div>
    )
  }

  if (type === 'integer') {
    return (
      <div>
        <label className="mb-1 block text-xs font-medium text-fog">{label}</label>
        <input
          type="number"
          step={1}
          value={typeof value === 'number' ? value : parseInt(String(value ?? '0'))}
          onChange={(e) => onChange(parseInt(e.target.value) || 0)}
          className="w-24 rounded border border-stone/30 bg-ash/40 px-2 py-1 text-xs text-light"
        />
      </div>
    )
  }

  if (type === 'boolean') {
    return (
      <div className="flex items-center gap-2">
        <input
          type="checkbox"
          checked={!!value}
          onChange={(e) => onChange(e.target.checked)}
          className="accent-amber"
        />
        <label className="text-xs font-medium text-fog">{label}</label>
      </div>
    )
  }

  // Default: string
  return (
    <div>
      <label className="mb-1 block text-xs font-medium text-fog">{label}</label>
      <input
        type="text"
        value={String(value ?? '')}
        onChange={(e) => onChange(e.target.value)}
        className="w-full rounded border border-stone/30 bg-ash/40 px-2 py-1 text-xs text-light"
      />
    </div>
  )
}

function AddTagButton({ onAdd }: { onAdd: (tag: string) => void }) {
  const [editing, setEditing] = useState(false)
  const [text, setText] = useState('')

  if (!editing) {
    return (
      <button
        type="button"
        onClick={() => setEditing(true)}
        className="inline-flex items-center gap-0.5 rounded-md border border-dashed border-stone/40 px-1.5 py-0.5 text-xs text-dust hover:border-amber hover:text-amber"
      >
        <Plus className="h-3 w-3" /> Add
      </button>
    )
  }

  return (
    <input
      autoFocus
      type="text"
      value={text}
      onChange={(e) => setText(e.target.value)}
      onKeyDown={(e) => {
        if (e.key === 'Enter' && text.trim()) {
          onAdd(text.trim())
          setText('')
          setEditing(false)
        }
        if (e.key === 'Escape') {
          setText('')
          setEditing(false)
        }
      }}
      onBlur={() => {
        if (text.trim()) onAdd(text.trim())
        setText('')
        setEditing(false)
      }}
      placeholder="domain name"
      className="w-24 rounded border border-amber/50 bg-ash/40 px-1.5 py-0.5 text-xs text-light outline-none"
    />
  )
}
