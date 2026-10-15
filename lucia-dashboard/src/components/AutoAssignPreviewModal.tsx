import { useState } from 'react'
import { Loader2, Check, ChevronDown } from 'lucide-react'
import Modal from './Modal'

interface AutoAssignPreview {
  strategy: string
  totalEntities: number
  assignedCount: number
  excludedCount: number
  agentGroups: { agentName: string; count: number; entityIds: string[] }[]
  excludedSample: string[]
}

interface AutoAssignPreviewModalProps {
  preview: AutoAssignPreview | null
  loading: boolean
  onApply: () => void
  onClose: () => void
  /** Error from applying the assignment; shown here because the page behind the modal is inert. */
  error?: string | null
}

export default function AutoAssignPreviewModal({ preview, loading, onApply, onClose, error }: AutoAssignPreviewModalProps) {
  const [expandedAgent, setExpandedAgent] = useState<string | null>(null)
  const [expandExcluded, setExpandExcluded] = useState(false)

  return (
    <Modal open onClose={onClose} title="Smart Assign Preview" size="lg">
      <div className="space-y-4">
        {error && (
          <p role="alert" className="rounded-lg border border-rose/30 bg-rose/10 px-3 py-2 text-sm text-rose">
            {error}
          </p>
        )}
        {preview && <p className="text-xs text-dust">{preview.totalEntities} total entities</p>}

        {loading && !preview ? (
          <div className="flex items-center justify-center gap-2 py-12 text-dust">
            <Loader2 className="h-5 w-5 animate-spin" />
            Analyzing entities…
          </div>
        ) : preview ? (
          <>
            {/* Summary stats */}
            <div className="flex gap-4">
              <div className="flex-1 rounded-xl border border-sky-400/20 bg-sky-400/5 px-4 py-3 text-center">
                <div className="text-2xl font-bold text-sky-400">{preview.assignedCount}</div>
                <div className="text-xs text-dust">Assigned to agents</div>
              </div>
              <div className="flex-1 rounded-xl border border-stone bg-basalt px-4 py-3 text-center">
                <div className="text-2xl font-bold text-dust">{preview.excludedCount}</div>
                <div className="text-xs text-dust">Excluded</div>
              </div>
            </div>

            {/* Agent groups */}
            {preview.agentGroups.map(group => (
              <div key={group.agentName} className="rounded-xl border border-stone bg-basalt">
                <button
                  onClick={() => setExpandedAgent(expandedAgent === group.agentName ? null : group.agentName)}
                  aria-expanded={expandedAgent === group.agentName}
                  className="flex min-h-11 w-full items-center justify-between px-4 py-3 text-left"
                >
                  <div className="flex items-center gap-2">
                    <span className="rounded-md bg-sky-400/15 px-2 py-0.5 text-xs font-medium text-sky-400">
                      {group.agentName}
                    </span>
                    <span className="text-sm text-fog">{group.count} entities</span>
                  </div>
                  <ChevronDown className={`h-4 w-4 text-dust transition-transform ${expandedAgent === group.agentName ? 'rotate-180' : ''}`} />
                </button>
                {expandedAgent === group.agentName && (
                  <div className="border-t border-stone/50 px-4 py-2">
                    <div className="max-h-40 overflow-y-auto">
                      {group.entityIds.map(id => (
                        <div key={id} className="py-0.5 font-mono text-xs text-dust">{id}</div>
                      ))}
                    </div>
                  </div>
                )}
              </div>
            ))}

            {/* Excluded section */}
            {preview.excludedSample.length > 0 && (
              <div className="rounded-xl border border-stone/50 bg-basalt/50">
                <button
                  onClick={() => setExpandExcluded(!expandExcluded)}
                  aria-expanded={expandExcluded}
                  className="flex min-h-11 w-full items-center justify-between px-4 py-3 text-left"
                >
                  <div className="flex items-center gap-2">
                    <span className="rounded-md bg-stone/40 px-2 py-0.5 text-xs font-medium text-dust">excluded</span>
                    <span className="text-sm text-dust">{preview.excludedCount} entities (showing sample)</span>
                  </div>
                  <ChevronDown className={`h-4 w-4 text-dust transition-transform ${expandExcluded ? 'rotate-180' : ''}`} />
                </button>
                {expandExcluded && (
                  <div className="border-t border-stone/30 px-4 py-2">
                    <div className="max-h-40 overflow-y-auto">
                      {preview.excludedSample.map(id => (
                        <div key={id} className="py-0.5 font-mono text-xs text-dust/70">{id}</div>
                      ))}
                      {preview.excludedCount > preview.excludedSample.length && (
                        <div className="mt-1 text-xs italic text-dust/50">
                          …and {preview.excludedCount - preview.excludedSample.length} more
                        </div>
                      )}
                    </div>
                  </div>
                )}
              </div>
            )}
          </>
        ) : null}
      </div>

      <div className="mt-4 flex items-center justify-end gap-3 border-t border-stone pt-4">
        <button
          onClick={onClose}
          className="min-h-11 rounded-lg border border-stone bg-basalt px-4 py-2 text-sm font-medium text-fog hover:bg-stone/40"
        >
          Cancel
        </button>
        <button
          onClick={onApply}
          disabled={loading || !preview}
          className="flex min-h-11 items-center gap-1.5 rounded-lg bg-sky-400/15 px-4 py-2 text-sm font-medium text-sky-400 hover:bg-sky-400/25 disabled:opacity-40"
        >
          {loading && preview ? <Loader2 className="h-4 w-4 animate-spin" /> : <Check className="h-4 w-4" />}
          Apply Assignment
        </button>
      </div>
    </Modal>
  )
}
