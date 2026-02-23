import { useState } from 'react'
import { useParams, useNavigate } from 'react-router-dom'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { fetchTrace, updateLabel, fetchRelatedTraces } from '../api'
import type { RelatedTraceSummary } from '../api'
import { LabelStatus } from '../types'
import type { AgentExecutionRecord } from '../types'
import { ArrowLeft, Clock, Hash, Timer, AlertTriangle, CheckCircle2, XCircle, ThumbsUp, ThumbsDown, Eraser, ChevronDown, Loader2 } from 'lucide-react'

function formatDate(iso: string) {
  return new Date(iso).toLocaleString()
}

function AgentCard({ exec }: { exec: AgentExecutionRecord }) {
  return (
    <div className="glass-panel rounded-xl p-4">
      <div className="mb-3 flex flex-wrap items-center gap-3">
        <span className="rounded-md bg-amber/15 px-2 py-0.5 text-sm font-medium text-amber">
          {exec.agentId}
        </span>
        {exec.modelDeploymentName && (
          <span className="text-xs text-dust">Model: {exec.modelDeploymentName}</span>
        )}
        <span className="font-mono text-xs text-dust">{exec.executionDurationMs} ms</span>
        {exec.success ? (
          <span className="flex items-center gap-1 rounded-full bg-sage/15 px-2 py-0.5 text-xs text-sage">
            <CheckCircle2 className="h-3 w-3" /> Success
          </span>
        ) : (
          <span className="flex items-center gap-1 rounded-full bg-ember/15 px-2 py-0.5 text-xs text-rose">
            <XCircle className="h-3 w-3" /> Error
          </span>
        )}
      </div>

      {exec.errorMessage && (
        <p className="mb-3 rounded-lg border border-ember/20 bg-ember/10 p-2 text-sm text-rose">{exec.errorMessage}</p>
      )}

      {exec.toolCalls.length > 0 && (
        <div className="mb-3">
          <h4 className="mb-2 text-xs font-medium uppercase tracking-wider text-dust">Tool Calls</h4>
          <div className="space-y-2">
            {exec.toolCalls.map((tc, i) => (
              <div key={i} className="rounded-lg border border-stone bg-void/50 p-2 text-xs">
                <span className="font-medium text-amber">{tc.toolName}</span>
                {tc.arguments && (
                  <pre className="mt-1 overflow-x-auto text-dust">{tc.arguments}</pre>
                )}
                {tc.result && (
                  <pre className="mt-1 overflow-x-auto border-t border-stone pt-1 text-fog">
                    {tc.result}
                  </pre>
                )}
              </div>
            ))}
          </div>
        </div>
      )}

      {exec.responseContent && (
        <div>
          <h4 className="mb-1 text-xs font-medium uppercase tracking-wider text-dust">Response</h4>
          <p className="whitespace-pre-wrap text-sm text-fog">{exec.responseContent}</p>
        </div>
      )}
    </div>
  )
}

export default function TraceDetailPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const queryClient = useQueryClient()

  const { data: trace, isLoading, isError } = useQuery({
    queryKey: ['trace', id],
    queryFn: () => fetchTrace(id!),
    enabled: !!id,
  })

  const { data: relatedTraces } = useQuery({
    queryKey: ['trace-related', id],
    queryFn: () => fetchRelatedTraces(id!),
    enabled: !!id,
  })

  const [labelStatus, setLabelStatus] = useState<number | null>(null)
  const [notes, setNotes] = useState('')
  const [correction, setCorrection] = useState('')

  const currentLabel = labelStatus ?? trace?.label.status ?? LabelStatus.Unlabeled
  const showNotes = currentLabel !== LabelStatus.Unlabeled
  const showCorrection = currentLabel === LabelStatus.Negative

  const mutation = useMutation({
    mutationFn: () =>
      updateLabel(id!, {
        status: currentLabel as typeof LabelStatus.Positive,
        reviewerNotes: notes || undefined,
        correctionText: showCorrection ? correction || undefined : undefined,
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['trace', id] })
      queryClient.invalidateQueries({ queryKey: ['traces'] })
      queryClient.invalidateQueries({ queryKey: ['stats'] })
    },
  })

  if (isLoading) return (
    <div className="flex items-center gap-2 py-12 text-fog">
      <Loader2 className="h-4 w-4 animate-spin" /> Loading trace…
    </div>
  )
  if (isError || !trace) return <p className="text-rose">Trace not found.</p>

  return (
    <div className="space-y-6">
      {/* Back button */}
      <button
        onClick={() => navigate('/')}
        className="flex items-center gap-1.5 text-sm text-amber transition-colors hover:text-amber-glow"
      >
        <ArrowLeft className="h-4 w-4" /> Back to Traces
      </button>

      {/* Header */}
      <div className="glass-panel rounded-xl p-5">
        <div className="flex flex-wrap gap-6 text-sm">
          <div>
            <span className="flex items-center gap-1 text-xs text-dust"><Clock className="h-3 w-3" /> Timestamp</span>
            <p className="mt-0.5 text-light">{formatDate(trace.timestamp)}</p>
          </div>
          <div>
            <span className="flex items-center gap-1 text-xs text-dust"><Hash className="h-3 w-3" /> Session</span>
            <p className="mt-0.5 font-mono text-xs text-fog">{trace.sessionId}</p>
          </div>
          <div>
            <span className="flex items-center gap-1 text-xs text-dust"><Timer className="h-3 w-3" /> Duration</span>
            <p className="mt-0.5 text-light">{trace.totalDurationMs} ms</p>
          </div>
          {trace.isErrored && (
            <div>
              <span className="flex items-center gap-1 text-xs text-rose"><AlertTriangle className="h-3 w-3" /> Error</span>
              <p className="mt-0.5 text-rose">{trace.errorMessage ?? 'Unknown error'}</p>
            </div>
          )}
        </div>

        {/* Conversation History */}
        {trace.conversationHistory && trace.conversationHistory.length > 0 && (
          <div className="mt-4">
            <span className="text-xs font-medium uppercase tracking-wider text-dust">Conversation History</span>
            <div className="mt-2 space-y-2 rounded-lg border border-stone bg-void/50 p-3">
              {trace.conversationHistory.map((msg, i) => (
                <div key={i} className="flex gap-2 text-sm">
                  <span
                    className={`shrink-0 rounded-md px-1.5 py-0.5 text-xs font-medium ${
                      msg.role === 'user'
                        ? 'bg-amber/15 text-amber'
                        : 'bg-sage/15 text-sage'
                    }`}
                  >
                    {msg.role}
                  </span>
                  <span className="whitespace-pre-wrap text-fog">{msg.content}</span>
                </div>
              ))}
            </div>
          </div>
        )}

        <div className="mt-4">
          <span className="text-xs font-medium uppercase tracking-wider text-dust">User Input</span>
          <p className="mt-1 whitespace-pre-wrap text-light">{trace.userInput}</p>
        </div>
        {trace.finalResponse && (
          <div className="mt-4">
            <span className="text-xs font-medium uppercase tracking-wider text-dust">Final Response</span>
            <p className="mt-1 whitespace-pre-wrap text-fog">{trace.finalResponse}</p>
          </div>
        )}
      </div>

      {/* System Prompt */}
      {trace.systemPrompt && (
        <details className="glass-panel rounded-xl">
          <summary className="flex cursor-pointer items-center gap-2 p-4 text-sm font-semibold uppercase tracking-wider text-dust hover:text-fog">
            <ChevronDown className="h-4 w-4 transition-transform [[open]>&]:rotate-180" />
            Router System Prompt
          </summary>
          <div className="border-t border-stone p-4">
            <pre className="whitespace-pre-wrap text-sm text-fog">{trace.systemPrompt}</pre>
          </div>
        </details>
      )}

      {/* Related Traces (same session) */}
      {relatedTraces && relatedTraces.length > 0 && (
        <div className="glass-panel rounded-xl p-5">
          <h3 className="mb-3 text-xs font-semibold uppercase tracking-wider text-dust">
            Related Traces ({relatedTraces.length})
          </h3>
          <div className="space-y-2">
            {relatedTraces.map((rt: RelatedTraceSummary) => (
              <div
                key={rt.id}
                onClick={() => navigate(`/traces/${rt.id}`)}
                className="flex cursor-pointer items-center gap-3 rounded-lg border border-stone bg-void/50 p-3 transition-colors hover:border-amber/30 hover:bg-basalt/60"
              >
                <span
                  className={`shrink-0 rounded-md px-2 py-0.5 text-xs font-medium ${
                    rt.traceType === 'orchestrator'
                      ? 'bg-amber/15 text-amber'
                      : 'bg-amber/10 text-amber/80'
                  }`}
                >
                  {rt.traceType === 'agent' && rt.agentId ? rt.agentId : rt.traceType}
                </span>
                <span className="min-w-0 flex-1 truncate text-sm text-fog">
                  {rt.userInput}
                </span>
                <span className="shrink-0 font-mono text-xs text-dust">{rt.totalDurationMs} ms</span>
                {rt.isErrored && (
                  <span className="shrink-0 rounded-full bg-ember/15 px-2 py-0.5 text-xs text-rose">
                    Error
                  </span>
                )}
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Routing Decision */}
      {trace.routing && (
        <div className="glass-panel rounded-xl p-5">
          <h3 className="mb-3 text-xs font-semibold uppercase tracking-wider text-dust">Routing Decision</h3>
          <div className="flex flex-wrap gap-6 text-sm">
            <div>
              <span className="text-xs text-dust">Selected Agent</span>
              <p className="text-amber">{trace.routing.selectedAgentId}</p>
            </div>
            <div>
              <span className="text-xs text-dust">Confidence</span>
              <p className="text-light">{(trace.routing.confidence * 100).toFixed(1)}%</p>
            </div>
            <div>
              <span className="text-xs text-dust">Routing Duration</span>
              <p className="text-light">{trace.routing.routingDurationMs} ms</p>
            </div>
            {trace.routing.modelDeploymentName && (
              <div>
                <span className="text-xs text-dust">Model</span>
                <p className="text-light">{trace.routing.modelDeploymentName}</p>
              </div>
            )}
          </div>
          {trace.routing.reasoning && (
            <p className="mt-3 text-sm text-fog">{trace.routing.reasoning}</p>
          )}
          {trace.routing.additionalAgentIds.length > 0 && (
            <div className="mt-3">
              <span className="text-xs text-dust">Additional Agents: </span>
              {trace.routing.additionalAgentIds.map((a) => (
                <span key={a} className="mr-1 rounded-md bg-amber/10 px-1.5 py-0.5 text-xs text-amber">
                  {a}
                </span>
              ))}
            </div>
          )}
          {trace.routing.agentInstructions && trace.routing.agentInstructions.length > 0 && (
            <div className="mt-4">
              <h4 className="mb-2 text-xs font-medium uppercase tracking-wider text-dust">Agent Instructions</h4>
              <div className="space-y-2">
                {trace.routing.agentInstructions.map((ai, i) => (
                  <div key={i} className="rounded-lg border border-stone bg-void/50 p-2 text-sm">
                    <span className="font-medium text-amber">{ai.agentId}</span>
                    <span className="mx-2 text-stone">→</span>
                    <span className="text-fog">{ai.instruction}</span>
                  </div>
                ))}
              </div>
            </div>
          )}
        </div>
      )}

      {/* Agent Executions */}
      {trace.agentExecutions.length > 0 && (
        <div>
          <h3 className="mb-3 text-xs font-semibold uppercase tracking-wider text-dust">
            Agent Executions ({trace.agentExecutions.length})
          </h3>
          <div className="space-y-4">
            {trace.agentExecutions.map((exec, i) => (
              <AgentCard key={i} exec={exec} />
            ))}
          </div>
        </div>
      )}

      {/* Label Section */}
      <div className="glass-panel rounded-xl p-5">
        <h3 className="mb-3 text-xs font-semibold uppercase tracking-wider text-dust">Label</h3>
        <div className="flex gap-2">
          <button
            onClick={() => setLabelStatus(LabelStatus.Positive)}
            className={`flex items-center gap-1.5 rounded-xl px-4 py-2 text-sm font-medium transition-colors ${
              currentLabel === LabelStatus.Positive
                ? 'bg-sage/20 text-sage ring-1 ring-sage/40'
                : 'border border-stone bg-basalt text-fog hover:bg-sage/10 hover:text-sage'
            }`}
          >
            <ThumbsUp className="h-4 w-4" /> Positive
          </button>
          <button
            onClick={() => setLabelStatus(LabelStatus.Negative)}
            className={`flex items-center gap-1.5 rounded-xl px-4 py-2 text-sm font-medium transition-colors ${
              currentLabel === LabelStatus.Negative
                ? 'bg-ember/20 text-rose ring-1 ring-ember/40'
                : 'border border-stone bg-basalt text-fog hover:bg-ember/10 hover:text-rose'
            }`}
          >
            <ThumbsDown className="h-4 w-4" /> Negative
          </button>
          <button
            onClick={() => setLabelStatus(LabelStatus.Unlabeled)}
            className={`flex items-center gap-1.5 rounded-xl px-4 py-2 text-sm font-medium transition-colors ${
              currentLabel === LabelStatus.Unlabeled
                ? 'bg-stone/50 text-light ring-1 ring-stone'
                : 'border border-stone bg-basalt text-fog hover:text-light'
            }`}
          >
            <Eraser className="h-4 w-4" /> Clear
          </button>
        </div>

        {showNotes && (
          <div className="mt-4">
            <label className="mb-1.5 block text-xs font-medium uppercase tracking-wider text-dust">Reviewer Notes</label>
            <textarea
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              rows={2}
              className="w-full rounded-xl border border-stone bg-basalt px-4 py-3 text-sm text-light placeholder-dust/60 input-focus transition-colors"
              placeholder="Add notes about this trace…"
            />
          </div>
        )}

        {showCorrection && (
          <div className="mt-4">
            <label className="mb-1.5 block text-xs font-medium uppercase tracking-wider text-dust">Correction Text</label>
            <textarea
              value={correction}
              onChange={(e) => setCorrection(e.target.value)}
              rows={3}
              className="w-full rounded-xl border border-stone bg-basalt px-4 py-3 text-sm text-light placeholder-dust/60 input-focus transition-colors"
              placeholder="What should the correct response be?"
            />
          </div>
        )}

        <button
          onClick={() => mutation.mutate()}
          disabled={mutation.isPending}
          className="mt-4 rounded-xl bg-amber px-5 py-2.5 text-sm font-semibold text-void transition-all hover:bg-amber-glow disabled:opacity-40"
        >
          {mutation.isPending ? 'Saving…' : 'Save Label'}
        </button>
        {mutation.isError && (
          <p className="mt-2 text-sm text-rose">Failed to save label.</p>
        )}
        {mutation.isSuccess && (
          <p className="mt-2 text-sm text-sage">Label saved.</p>
        )}
      </div>
    </div>
  )
}
