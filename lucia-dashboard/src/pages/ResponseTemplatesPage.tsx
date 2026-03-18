import { useCallback, useMemo, useRef, useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import {
  fetchResponseTemplates,
  fetchCommandPatterns,
  createResponseTemplate,
  updateResponseTemplate,
  deleteResponseTemplate,
  resetResponseTemplates,
} from '../api'
import type {
  ResponseTemplate,
  CommandPattern,
  CreateResponseTemplateRequest,
} from '../api'
import ToastContainer from '../components/ToastContainer'
import ConfirmDialog from '../components/ConfirmDialog'
import { useToast } from '../hooks/useToast'
import {
  Plus, Save, Trash2, RotateCcw, Eye, X, ChevronDown, ChevronRight,
} from 'lucide-react'

/* ------------------------------------------------------------------ */
/*  Helpers                                                            */
/* ------------------------------------------------------------------ */

const SKILL_LABELS: Record<string, string> = {
  LightControlSkill: 'Light Control',
  ClimateControlSkill: 'Climate Control',
  SceneControlSkill: 'Scene Control',
}

function skillDisplayName(skillId: string): string {
  return SKILL_LABELS[skillId] ?? skillId
}

/** Replace known placeholder tokens with sample values for preview. */
function renderPreview(template: string): string {
  return template
    .replace(/\{entityName\}/gi, 'Living Room Light')
    .replace(/\{entity_name\}/gi, 'Living Room Light')
    .replace(/\{entity\}/gi, 'Living Room Light')
    .replace(/\{area\}/gi, 'Living Room')
    .replace(/\{action\}/gi, 'turned on')
    .replace(/\{state\}/gi, 'on')
    .replace(/\{value\}/gi, '75')
    .replace(/\{temperature\}/gi, '72')
    .replace(/\{brightness\}/gi, '80')
    .replace(/\{scene\}/gi, 'Movie Night')
    .replace(/\{color\}/gi, 'warm white')
}

type GroupedTemplates = Record<string, ResponseTemplate[]>

function groupBySkill(templates: ResponseTemplate[]): GroupedTemplates {
  const groups: GroupedTemplates = {}
  for (const t of templates) {
    const key = t.skillId
    if (!groups[key]) groups[key] = []
    groups[key].push(t)
  }
  return groups
}

/** Build a lookup from "skillId::action" to the matching pattern. */
function buildPatternLookup(
  patterns: CommandPattern[] | undefined,
): Map<string, CommandPattern> {
  const map = new Map<string, CommandPattern>()
  if (!patterns) return map
  for (const p of patterns) {
    map.set(`${p.skillId}::${p.action}`, p)
  }
  return map
}

/** Insert a `{token}` at the cursor position of an input and return the new value + cursor pos. */
function insertTokenAtCursor(
  inputEl: HTMLInputElement | null,
  currentValue: string,
  token: string,
): { value: string; cursorPos: number } {
  const start = inputEl?.selectionStart ?? currentValue.length
  const end = inputEl?.selectionEnd ?? currentValue.length
  const insert = `{${token}}`
  const value = currentValue.substring(0, start) + insert + currentValue.substring(end)
  return { value, cursorPos: start + insert.length }
}

/* ------------------------------------------------------------------ */
/*  Token Buttons                                                      */
/* ------------------------------------------------------------------ */

function TokenButtons({
  tokens,
  onInsert,
}: {
  tokens: string[]
  onInsert: (token: string) => void
}) {
  if (tokens.length === 0) return null
  return (
    <div className="flex flex-wrap items-center gap-1.5 mt-1">
      <span className="text-[10px] text-dust">Insert:</span>
      {tokens.map((token) => (
        <button
          key={token}
          type="button"
          onClick={() => onInsert(token)}
          className="rounded-full bg-amber-glow/10 px-2 py-0.5 text-[10px] text-amber hover:bg-amber-glow/20 cursor-pointer transition-colors"
        >
          {`{${token}}`}
        </button>
      ))}
    </div>
  )
}

/* ------------------------------------------------------------------ */
/*  Example Templates Info                                             */
/* ------------------------------------------------------------------ */

function ExampleTemplatesInfo({ examples }: { examples: string[] }) {
  if (examples.length === 0) return null
  return (
    <p className="text-[10px] text-dust font-mono mt-1 leading-relaxed">
      Matched by: {examples.map((e, i) => (
        <span key={i}>
          {i > 0 && ', '}
          &ldquo;{e}&rdquo;
        </span>
      ))}
    </p>
  )
}

/* ------------------------------------------------------------------ */
/*  Template Preview Modal                                             */
/* ------------------------------------------------------------------ */

function PreviewModal({ templates, onClose }: { templates: string[]; onClose: () => void }) {
  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm"
      role="dialog"
      aria-modal="true"
    >
      <div className="w-full max-w-md rounded-xl border border-stone/40 bg-obsidian p-6 shadow-2xl">
        <div className="flex items-center justify-between mb-4">
          <h3 className="text-base font-semibold text-light">Template Preview</h3>
          <button onClick={onClose} className="text-dust hover:text-cloud" aria-label="Close">
            <X className="h-4 w-4" />
          </button>
        </div>
        <div className="space-y-2">
          {templates.map((t, i) => (
            <div key={i} className="rounded-lg border border-stone/40 bg-basalt p-3">
              <p className="text-xs text-dust mb-1">Template {i + 1}</p>
              <p className="text-sm text-light">{renderPreview(t)}</p>
            </div>
          ))}
        </div>
        <p className="mt-3 text-[11px] text-dust">
          Placeholders replaced with sample values for preview.
        </p>
      </div>
    </div>
  )
}

/* ------------------------------------------------------------------ */
/*  Single Template Card (with token buttons for editing)              */
/* ------------------------------------------------------------------ */

function TemplateCard({
  template,
  tokens,
  onSave,
  onDelete,
  saving,
}: {
  template: ResponseTemplate
  tokens: string[]
  onSave: (id: string, templates: string[]) => void
  onDelete: (id: string) => void
  saving: boolean
}) {
  const [localTemplates, setLocalTemplates] = useState(template.templates)
  const [showPreview, setShowPreview] = useState(false)
  const [activeInputIdx, setActiveInputIdx] = useState<number>(0)
  const inputRefs = useRef<Map<number, HTMLInputElement>>(new Map())
  const isDirty = JSON.stringify(localTemplates) !== JSON.stringify(template.templates)

  function handleTemplateChange(index: number, value: string) {
    setLocalTemplates((prev) => prev.map((t, i) => (i === index ? value : t)))
  }

  function addTemplateLine() {
    setLocalTemplates((prev) => [...prev, ''])
  }

  function removeTemplateLine(index: number) {
    setLocalTemplates((prev) => prev.filter((_, i) => i !== index))
  }

  const handleTokenInsert = useCallback(
    (token: string) => {
      const inputEl = inputRefs.current.get(activeInputIdx) ?? null
      const currentValue = localTemplates[activeInputIdx] ?? ''
      const { value, cursorPos } = insertTokenAtCursor(inputEl, currentValue, token)
      setLocalTemplates((prev) => prev.map((t, i) => (i === activeInputIdx ? value : t)))
      // Restore cursor position after React re-render
      requestAnimationFrame(() => {
        const el = inputRefs.current.get(activeInputIdx)
        if (el) {
          el.setSelectionRange(cursorPos, cursorPos)
          el.focus()
        }
      })
    },
    [activeInputIdx, localTemplates],
  )

  return (
    <div className="rounded-xl border border-stone bg-charcoal p-4">
      <div className="flex flex-wrap items-center justify-between gap-2 mb-3">
        <div className="flex items-center gap-2">
          <span className="text-sm font-semibold text-light">{template.action}</span>
          {template.isDefault && (
            <span className="rounded bg-amber/20 px-1.5 py-0.5 text-[10px] font-semibold uppercase text-amber">
              default
            </span>
          )}
        </div>
        <div className="flex items-center gap-2">
          <button
            onClick={() => setShowPreview(true)}
            className="flex items-center gap-1 rounded-lg px-2.5 py-1.5 text-xs text-fog hover:text-cloud hover:bg-stone/40 transition-colors"
          >
            <Eye className="h-3.5 w-3.5" /> Preview
          </button>
          <button
            onClick={() => onSave(template.id, localTemplates)}
            disabled={!isDirty || saving}
            className="flex items-center gap-1 rounded-lg px-2.5 py-1.5 text-xs font-medium text-sage hover:bg-sage/20 disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
          >
            <Save className="h-3.5 w-3.5" /> Save
          </button>
          <button
            onClick={() => onDelete(template.id)}
            className="flex items-center gap-1 rounded-lg px-2.5 py-1.5 text-xs text-rose hover:bg-rose/20 transition-colors"
          >
            <Trash2 className="h-3.5 w-3.5" /> Delete
          </button>
        </div>
      </div>

      <div className="space-y-2">
        {localTemplates.map((t, i) => (
          <div key={i} className="flex items-center gap-2">
            <input
              ref={(el) => {
                if (el) inputRefs.current.set(i, el)
                else inputRefs.current.delete(i)
              }}
              type="text"
              value={t}
              onFocus={() => setActiveInputIdx(i)}
              onChange={(e) => handleTemplateChange(i, e.target.value)}
              className="flex-1 rounded-lg border border-stone bg-basalt px-3 py-2 text-sm text-light placeholder-dust/60 input-focus"
              placeholder="Template text with {entity} placeholders\u2026"
            />
            {localTemplates.length > 1 && (
              <button
                onClick={() => removeTemplateLine(i)}
                className="text-dust hover:text-rose transition-colors"
                aria-label="Remove template line"
              >
                <X className="h-4 w-4" />
              </button>
            )}
          </div>
        ))}
      </div>

      <TokenButtons tokens={tokens} onInsert={handleTokenInsert} />

      <button
        onClick={addTemplateLine}
        className="mt-2 flex items-center gap-1 text-xs text-dust hover:text-amber transition-colors"
      >
        <Plus className="h-3.5 w-3.5" /> Add variant
      </button>

      {showPreview && (
        <PreviewModal templates={localTemplates} onClose={() => setShowPreview(false)} />
      )}
    </div>
  )
}

/* ------------------------------------------------------------------ */
/*  Skill Group                                                        */
/* ------------------------------------------------------------------ */

function SkillGroup({
  skillId,
  templates,
  patternLookup,
  onSave,
  onDelete,
  saving,
}: {
  skillId: string
  templates: ResponseTemplate[]
  patternLookup: Map<string, CommandPattern>
  onSave: (id: string, templates: string[]) => void
  onDelete: (id: string) => void
  saving: boolean
}) {
  const [expanded, setExpanded] = useState(true)

  // Collect all unique example templates for this skill group
  const skillExamples = useMemo(() => {
    const seen = new Set<string>()
    const examples: string[] = []
    for (const t of templates) {
      const p = patternLookup.get(`${t.skillId}::${t.action}`)
      if (p) {
        for (const ex of p.exampleTemplates) {
          if (!seen.has(ex)) {
            seen.add(ex)
            examples.push(ex)
          }
        }
      }
    }
    return examples
  }, [templates, patternLookup])

  return (
    <div className="rounded-xl border border-stone/40 bg-obsidian/50">
      <button
        onClick={() => setExpanded((prev) => !prev)}
        className="flex w-full items-center gap-2 px-5 py-4 text-left"
      >
        {expanded ? (
          <ChevronDown className="h-4 w-4 text-dust" />
        ) : (
          <ChevronRight className="h-4 w-4 text-dust" />
        )}
        <span className="font-display text-base font-semibold text-light">
          {skillDisplayName(skillId)}
        </span>
        <span className="ml-auto rounded-full bg-stone/60 px-2 py-0.5 text-[11px] text-dust">
          {templates.length} {templates.length === 1 ? 'template' : 'templates'}
        </span>
      </button>

      {expanded && (
        <div className="space-y-3 px-5 pb-5">
          {skillExamples.length > 0 && (
            <ExampleTemplatesInfo examples={skillExamples} />
          )}
          {templates.map((t) => {
            const pattern = patternLookup.get(`${t.skillId}::${t.action}`)
            return (
              <TemplateCard
                key={t.id}
                template={t}
                tokens={pattern?.tokens ?? []}
                onSave={onSave}
                onDelete={onDelete}
                saving={saving}
              />
            )
          })}
        </div>
      )}
    </div>
  )
}

/* ------------------------------------------------------------------ */
/*  Add Template Form                                                  */
/* ------------------------------------------------------------------ */

function AddTemplateForm({
  patterns,
  onSubmit,
  submitting,
}: {
  patterns: CommandPattern[] | undefined
  onSubmit: (req: CreateResponseTemplateRequest) => void
  submitting: boolean
}) {
  const [skillId, setSkillId] = useState('')
  const [action, setAction] = useState('')
  const [templates, setTemplates] = useState([''])
  const [activeInputIdx, setActiveInputIdx] = useState<number>(0)
  const inputRefs = useRef<Map<number, HTMLInputElement>>(new Map())

  // Derive unique skill IDs and available actions from patterns
  const uniqueSkillIds = useMemo(() => {
    if (!patterns) return []
    const set = new Set(patterns.map((p) => p.skillId))
    return Array.from(set).sort()
  }, [patterns])

  const availableActions = useMemo(() => {
    if (!patterns || !skillId) return []
    const set = new Set(
      patterns.filter((p) => p.skillId === skillId).map((p) => p.action),
    )
    return Array.from(set).sort()
  }, [patterns, skillId])

  const selectedPattern = useMemo(() => {
    if (!patterns || !skillId || !action) return undefined
    return patterns.find((p) => p.skillId === skillId && p.action === action)
  }, [patterns, skillId, action])

  function handleSkillChange(newSkillId: string) {
    setSkillId(newSkillId)
    setAction('')
  }

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    if (!skillId.trim() || !action.trim() || templates.every((t) => !t.trim())) return
    onSubmit({
      skillId: skillId.trim(),
      action: action.trim(),
      templates: templates.filter((t) => t.trim()),
    })
    setSkillId('')
    setAction('')
    setTemplates([''])
  }

  function handleTemplateChange(index: number, value: string) {
    setTemplates((prev) => prev.map((t, i) => (i === index ? value : t)))
  }

  function addLine() {
    setTemplates((prev) => [...prev, ''])
  }

  function removeLine(index: number) {
    setTemplates((prev) => prev.filter((_, i) => i !== index))
  }

  const handleTokenInsert = useCallback(
    (token: string) => {
      const inputEl = inputRefs.current.get(activeInputIdx) ?? null
      const currentValue = templates[activeInputIdx] ?? ''
      const { value, cursorPos } = insertTokenAtCursor(inputEl, currentValue, token)
      setTemplates((prev) => prev.map((t, i) => (i === activeInputIdx ? value : t)))
      requestAnimationFrame(() => {
        const el = inputRefs.current.get(activeInputIdx)
        if (el) {
          el.setSelectionRange(cursorPos, cursorPos)
          el.focus()
        }
      })
    },
    [activeInputIdx, templates],
  )

  const selectStyle =
    'rounded border border-stone bg-basalt px-2 py-1.5 text-sm text-light'
  const inputStyle =
    'rounded-xl border border-stone bg-basalt px-3 py-2 text-sm text-light placeholder-dust/60 input-focus'

  const hasPatterns = patterns && patterns.length > 0

  return (
    <form onSubmit={handleSubmit} className="glass-panel rounded-xl p-5 space-y-4">
      <h3 className="flex items-center gap-2 text-xs font-semibold uppercase tracking-wider text-dust">
        <Plus className="h-4 w-4" /> Add Template
      </h3>

      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <div>
          <label className="mb-1.5 block text-xs font-medium uppercase tracking-wider text-dust">
            Skill ID
          </label>
          {hasPatterns ? (
            <select
              value={skillId}
              onChange={(e) => handleSkillChange(e.target.value)}
              className={`w-full ${selectStyle}`}
              required
            >
              <option value="">Select a skill\u2026</option>
              {uniqueSkillIds.map((id) => (
                <option key={id} value={id}>
                  {skillDisplayName(id)} ({id})
                </option>
              ))}
            </select>
          ) : (
            <input
              type="text"
              value={skillId}
              onChange={(e) => setSkillId(e.target.value)}
              className={`w-full ${inputStyle}`}
              placeholder="e.g. LightControlSkill"
              required
            />
          )}
        </div>
        <div>
          <label className="mb-1.5 block text-xs font-medium uppercase tracking-wider text-dust">
            Action
          </label>
          {hasPatterns ? (
            <select
              value={action}
              onChange={(e) => setAction(e.target.value)}
              className={`w-full ${selectStyle}`}
              disabled={!skillId}
              required
            >
              <option value="">
                {skillId ? 'Select an action\u2026' : 'Select a skill first'}
              </option>
              {availableActions.map((a) => (
                <option key={a} value={a}>{a}</option>
              ))}
            </select>
          ) : (
            <input
              type="text"
              value={action}
              onChange={(e) => setAction(e.target.value)}
              className={`w-full ${inputStyle}`}
              placeholder="e.g. toggle"
              required
            />
          )}
        </div>
      </div>

      {/* Show matched example patterns when a pattern is selected */}
      {selectedPattern && selectedPattern.exampleTemplates.length > 0 && (
        <ExampleTemplatesInfo examples={selectedPattern.exampleTemplates} />
      )}

      <div>
        <label className="mb-1.5 block text-xs font-medium uppercase tracking-wider text-dust">
          Template Strings
        </label>
        <div className="space-y-2">
          {templates.map((t, i) => (
            <div key={i} className="flex items-center gap-2">
              <input
                ref={(el) => {
                  if (el) inputRefs.current.set(i, el)
                  else inputRefs.current.delete(i)
                }}
                type="text"
                value={t}
                onFocus={() => setActiveInputIdx(i)}
                onChange={(e) => handleTemplateChange(i, e.target.value)}
                className={`flex-1 ${inputStyle}`}
                placeholder="{entity} has been {action}"
              />
              {templates.length > 1 && (
                <button
                  type="button"
                  onClick={() => removeLine(i)}
                  className="text-dust hover:text-rose transition-colors"
                  aria-label="Remove line"
                >
                  <X className="h-4 w-4" />
                </button>
              )}
            </div>
          ))}
        </div>

        {/* Token insertion buttons from selected pattern */}
        {selectedPattern && (
          <TokenButtons tokens={selectedPattern.tokens} onInsert={handleTokenInsert} />
        )}

        <button
          type="button"
          onClick={addLine}
          className="mt-2 flex items-center gap-1 text-xs text-dust hover:text-amber transition-colors"
        >
          <Plus className="h-3.5 w-3.5" /> Add variant
        </button>
      </div>

      <button
        type="submit"
        disabled={submitting || !skillId.trim() || !action.trim()}
        className="rounded-xl bg-amber/20 px-4 py-2 text-sm font-medium text-amber hover:bg-amber/30 disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
      >
        {submitting ? 'Creating\u2026' : 'Create Template'}
      </button>
    </form>
  )
}

/* ------------------------------------------------------------------ */
/*  Main Page                                                          */
/* ------------------------------------------------------------------ */

export default function ResponseTemplatesPage() {
  const { toasts, addToast, dismissToast } = useToast(4000)
  const queryClient = useQueryClient()
  const [confirmReset, setConfirmReset] = useState(false)
  const [deleteTarget, setDeleteTarget] = useState<string | null>(null)

  // ── Queries ──────────────────────────────────────────────────
  const { data: templates, isLoading } = useQuery({
    queryKey: ['response-templates'],
    queryFn: fetchResponseTemplates,
  })

  const { data: patterns } = useQuery({
    queryKey: ['command-patterns'],
    queryFn: fetchCommandPatterns,
  })

  // ── Mutations ────────────────────────────────────────────────
  const createMutation = useMutation({
    mutationFn: (req: CreateResponseTemplateRequest) => createResponseTemplate(req),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['response-templates'] })
      addToast('Template created', 'success')
    },
    onError: () => addToast('Failed to create template', 'error'),
  })

  const updateMutation = useMutation({
    mutationFn: ({ id, templates: t }: { id: string; templates: string[] }) =>
      updateResponseTemplate(id, { templates: t }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['response-templates'] })
      addToast('Template updated', 'success')
    },
    onError: () => addToast('Failed to update template', 'error'),
  })

  const deleteMutation = useMutation({
    mutationFn: (id: string) => deleteResponseTemplate(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['response-templates'] })
      addToast('Template deleted', 'success')
      setDeleteTarget(null)
    },
    onError: () => addToast('Failed to delete template', 'error'),
  })

  const resetMutation = useMutation({
    mutationFn: () => resetResponseTemplates(),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['response-templates'] })
      addToast('Templates reset to defaults', 'success')
      setConfirmReset(false)
    },
    onError: () => addToast('Failed to reset templates', 'error'),
  })

  // ── Handlers ─────────────────────────────────────────────────
  function handleSave(id: string, t: string[]) {
    updateMutation.mutate({ id, templates: t })
  }

  function handleDeleteRequest(id: string) {
    setDeleteTarget(id)
  }

  function handleDeleteConfirm() {
    if (deleteTarget) deleteMutation.mutate(deleteTarget)
  }

  // ── Derived state ────────────────────────────────────────────
  const grouped = templates ? groupBySkill(templates) : {}
  const skillIds = Object.keys(grouped).sort()
  const patternLookup = useMemo(() => buildPatternLookup(patterns), [patterns])

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="font-display text-2xl font-bold text-light">Response Templates</h1>
          <p className="mt-0.5 text-sm text-dust">
            Manage response text templates for the conversation command parser
          </p>
        </div>
        <button
          onClick={() => setConfirmReset(true)}
          className="flex items-center gap-2 rounded-xl bg-stone/40 px-4 py-2 text-sm font-medium text-fog hover:text-cloud hover:bg-stone/60 transition-colors"
        >
          <RotateCcw className="h-4 w-4" /> Reset to Defaults
        </button>
      </div>

      {/* Add Template Form */}
      <AddTemplateForm
        patterns={patterns}
        onSubmit={(req) => createMutation.mutate(req)}
        submitting={createMutation.isPending}
      />

      {/* Template Groups */}
      {isLoading ? (
        <div className="space-y-4">
          {Array.from({ length: 3 }).map((_, i) => (
            <div key={i} className="animate-pulse rounded-xl border border-stone bg-charcoal p-6">
              <div className="h-4 w-40 rounded bg-stone" />
              <div className="mt-3 h-10 rounded bg-stone" />
            </div>
          ))}
        </div>
      ) : skillIds.length === 0 ? (
        <div className="rounded-xl border border-stone bg-charcoal p-8 text-center">
          <p className="text-sm text-dust">No response templates found.</p>
          <p className="mt-1 text-xs text-dust/70">
            Create one above or reset to load the default templates.
          </p>
        </div>
      ) : (
        <div className="space-y-4">
          {skillIds.map((skillId) => (
            <SkillGroup
              key={skillId}
              skillId={skillId}
              templates={grouped[skillId]}
              patternLookup={patternLookup}
              onSave={handleSave}
              onDelete={handleDeleteRequest}
              saving={updateMutation.isPending}
            />
          ))}
        </div>
      )}

      {/* Confirm dialogs */}
      <ConfirmDialog
        open={confirmReset}
        title="Reset to Defaults"
        message="This will replace all custom templates with the built-in defaults. This action cannot be undone."
        confirmLabel="Reset"
        onConfirm={() => resetMutation.mutate()}
        onCancel={() => setConfirmReset(false)}
      />

      <ConfirmDialog
        open={deleteTarget !== null}
        title="Delete Template"
        message="Are you sure you want to delete this response template?"
        confirmLabel="Delete"
        onConfirm={handleDeleteConfirm}
        onCancel={() => setDeleteTarget(null)}
      />

      <ToastContainer toasts={toasts} onDismiss={dismissToast} />
    </div>
  )
}
