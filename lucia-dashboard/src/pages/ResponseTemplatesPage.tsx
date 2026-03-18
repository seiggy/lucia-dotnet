import { useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import {
  fetchResponseTemplates,
  createResponseTemplate,
  updateResponseTemplate,
  deleteResponseTemplate,
  resetResponseTemplates,
} from '../api'
import type {
  ResponseTemplate,
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
/*  Single Template Card                                               */
/* ------------------------------------------------------------------ */

function TemplateCard({
  template,
  onSave,
  onDelete,
  saving,
}: {
  template: ResponseTemplate
  onSave: (id: string, templates: string[]) => void
  onDelete: (id: string) => void
  saving: boolean
}) {
  const [localTemplates, setLocalTemplates] = useState(template.templates)
  const [showPreview, setShowPreview] = useState(false)
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
              type="text"
              value={t}
              onChange={(e) => handleTemplateChange(i, e.target.value)}
              className="flex-1 rounded-lg border border-stone bg-basalt px-3 py-2 text-sm text-light placeholder-dust/60 input-focus"
              placeholder="Template text with {entityName} placeholders…"
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
  onSave,
  onDelete,
  saving,
}: {
  skillId: string
  templates: ResponseTemplate[]
  onSave: (id: string, templates: string[]) => void
  onDelete: (id: string) => void
  saving: boolean
}) {
  const [expanded, setExpanded] = useState(true)

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
          {templates.map((t) => (
            <TemplateCard
              key={t.id}
              template={t}
              onSave={onSave}
              onDelete={onDelete}
              saving={saving}
            />
          ))}
        </div>
      )}
    </div>
  )
}

/* ------------------------------------------------------------------ */
/*  Add Template Form                                                  */
/* ------------------------------------------------------------------ */

function AddTemplateForm({
  onSubmit,
  submitting,
}: {
  onSubmit: (req: CreateResponseTemplateRequest) => void
  submitting: boolean
}) {
  const [skillId, setSkillId] = useState('')
  const [action, setAction] = useState('')
  const [templates, setTemplates] = useState([''])

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

  const inputStyle =
    'rounded-xl border border-stone bg-basalt px-3 py-2 text-sm text-light placeholder-dust/60 input-focus'

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
          <input
            type="text"
            value={skillId}
            onChange={(e) => setSkillId(e.target.value)}
            className={`w-full ${inputStyle}`}
            placeholder="e.g. LightControlSkill"
            required
          />
        </div>
        <div>
          <label className="mb-1.5 block text-xs font-medium uppercase tracking-wider text-dust">
            Action
          </label>
          <input
            type="text"
            value={action}
            onChange={(e) => setAction(e.target.value)}
            className={`w-full ${inputStyle}`}
            placeholder="e.g. TurnOn"
            required
          />
        </div>
      </div>

      <div>
        <label className="mb-1.5 block text-xs font-medium uppercase tracking-wider text-dust">
          Template Strings
        </label>
        <div className="space-y-2">
          {templates.map((t, i) => (
            <div key={i} className="flex items-center gap-2">
              <input
                type="text"
                value={t}
                onChange={(e) => handleTemplateChange(i, e.target.value)}
                className={`flex-1 ${inputStyle}`}
                placeholder="{entityName} has been {action}"
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
        {submitting ? 'Creating…' : 'Create Template'}
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
      <AddTemplateForm onSubmit={(req) => createMutation.mutate(req)} submitting={createMutation.isPending} />

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
