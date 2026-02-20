import { useState } from 'react'
import { useParams, useNavigate } from 'react-router-dom'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { fetchTrace, updateLabel } from '../api'
import { LabelStatus } from '../types'
import type { AgentExecutionRecord } from '../types'

function formatDate(iso: string) {
  return new Date(iso).toLocaleString()
}

function AgentCard({ exec }: { exec: AgentExecutionRecord }) {
  return (
    <div className="rounded-lg border border-gray-700 bg-gray-800 p-4">
      <div className="mb-3 flex flex-wrap items-center gap-3">
        <span className="rounded bg-indigo-500/20 px-2 py-0.5 text-sm font-medium text-indigo-300">
          {exec.agentId}
        </span>
        {exec.modelDeploymentName && (
          <span className="text-xs text-gray-400">Model: {exec.modelDeploymentName}</span>
        )}
        <span className="text-xs text-gray-400">{exec.executionDurationMs} ms</span>
        {exec.success ? (
          <span className="rounded-full bg-green-500/20 px-2 py-0.5 text-xs text-green-400">
            Success
          </span>
        ) : (
          <span className="rounded-full bg-red-500/20 px-2 py-0.5 text-xs text-red-400">
            Error
          </span>
        )}
      </div>

      {exec.errorMessage && (
        <p className="mb-3 rounded bg-red-500/10 p-2 text-sm text-red-400">{exec.errorMessage}</p>
      )}

      {exec.toolCalls.length > 0 && (
        <div className="mb-3">
          <h4 className="mb-1 text-xs font-medium uppercase text-gray-400">Tool Calls</h4>
          <div className="space-y-2">
            {exec.toolCalls.map((tc, i) => (
              <div key={i} className="rounded border border-gray-700 bg-gray-900 p-2 text-xs">
                <span className="font-medium text-indigo-300">{tc.toolName}</span>
                {tc.arguments && (
                  <pre className="mt-1 overflow-x-auto text-gray-400">{tc.arguments}</pre>
                )}
                {tc.result && (
                  <pre className="mt-1 overflow-x-auto border-t border-gray-700 pt-1 text-gray-300">
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
          <h4 className="mb-1 text-xs font-medium uppercase text-gray-400">Response</h4>
          <p className="whitespace-pre-wrap text-sm text-gray-300">{exec.responseContent}</p>
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

  const [labelStatus, setLabelStatus] = useState<number | null>(null)
  const [notes, setNotes] = useState('')
  const [correction, setCorrection] = useState('')

  // Sync local state when trace loads
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

  if (isLoading) return <p className="text-gray-400">Loading trace…</p>
  if (isError || !trace) return <p className="text-red-400">Trace not found.</p>

  return (
    <div className="space-y-6">
      {/* Back button */}
      <button
        onClick={() => navigate('/')}
        className="text-sm text-indigo-400 hover:text-indigo-300"
      >
        ← Back to Traces
      </button>

      {/* Header */}
      <div className="rounded-lg bg-gray-800 p-4">
        <div className="flex flex-wrap gap-4 text-sm">
          <div>
            <span className="text-xs text-gray-400">Timestamp</span>
            <p>{formatDate(trace.timestamp)}</p>
          </div>
          <div>
            <span className="text-xs text-gray-400">Session</span>
            <p className="font-mono text-xs">{trace.sessionId}</p>
          </div>
          <div>
            <span className="text-xs text-gray-400">Duration</span>
            <p>{trace.totalDurationMs} ms</p>
          </div>
          {trace.isErrored && (
            <div>
              <span className="text-xs text-red-400">Error</span>
              <p className="text-red-400">{trace.errorMessage ?? 'Unknown error'}</p>
            </div>
          )}
        </div>
        <div className="mt-3">
          <span className="text-xs text-gray-400">User Input</span>
          <p className="mt-1 whitespace-pre-wrap">{trace.userInput}</p>
        </div>
        {trace.finalResponse && (
          <div className="mt-3">
            <span className="text-xs text-gray-400">Final Response</span>
            <p className="mt-1 whitespace-pre-wrap text-gray-300">{trace.finalResponse}</p>
          </div>
        )}
      </div>

      {/* Routing Decision */}
      {trace.routing && (
        <div className="rounded-lg border border-gray-700 bg-gray-800 p-4">
          <h3 className="mb-3 text-sm font-semibold uppercase text-gray-400">Routing Decision</h3>
          <div className="flex flex-wrap gap-4 text-sm">
            <div>
              <span className="text-xs text-gray-400">Selected Agent</span>
              <p className="text-indigo-300">{trace.routing.selectedAgentId}</p>
            </div>
            <div>
              <span className="text-xs text-gray-400">Confidence</span>
              <p>{(trace.routing.confidence * 100).toFixed(1)}%</p>
            </div>
            <div>
              <span className="text-xs text-gray-400">Routing Duration</span>
              <p>{trace.routing.routingDurationMs} ms</p>
            </div>
            {trace.routing.modelDeploymentName && (
              <div>
                <span className="text-xs text-gray-400">Model</span>
                <p>{trace.routing.modelDeploymentName}</p>
              </div>
            )}
          </div>
          {trace.routing.reasoning && (
            <p className="mt-2 text-sm text-gray-300">{trace.routing.reasoning}</p>
          )}
          {trace.routing.additionalAgentIds.length > 0 && (
            <div className="mt-2">
              <span className="text-xs text-gray-400">Additional Agents: </span>
              {trace.routing.additionalAgentIds.map((a) => (
                <span key={a} className="mr-1 rounded bg-gray-700 px-1.5 py-0.5 text-xs">
                  {a}
                </span>
              ))}
            </div>
          )}
          {trace.routing.agentInstructions && trace.routing.agentInstructions.length > 0 && (
            <div className="mt-3">
              <h4 className="mb-2 text-xs font-medium uppercase text-gray-400">Agent Instructions</h4>
              <div className="space-y-2">
                {trace.routing.agentInstructions.map((ai, i) => (
                  <div key={i} className="rounded border border-gray-700 bg-gray-900 p-2 text-sm">
                    <span className="font-medium text-indigo-300">{ai.agentId}</span>
                    <span className="mx-2 text-gray-600">→</span>
                    <span className="text-gray-300">{ai.instruction}</span>
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
          <h3 className="mb-3 text-sm font-semibold uppercase text-gray-400">
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
      <div className="rounded-lg border border-gray-700 bg-gray-800 p-4">
        <h3 className="mb-3 text-sm font-semibold uppercase text-gray-400">Label</h3>
        <div className="flex gap-2">
          <button
            onClick={() => setLabelStatus(LabelStatus.Positive)}
            className={`rounded px-4 py-1.5 text-sm font-medium ${
              currentLabel === LabelStatus.Positive
                ? 'bg-green-600 text-white'
                : 'bg-gray-700 text-gray-300 hover:bg-green-600/30'
            }`}
          >
            Positive
          </button>
          <button
            onClick={() => setLabelStatus(LabelStatus.Negative)}
            className={`rounded px-4 py-1.5 text-sm font-medium ${
              currentLabel === LabelStatus.Negative
                ? 'bg-red-600 text-white'
                : 'bg-gray-700 text-gray-300 hover:bg-red-600/30'
            }`}
          >
            Negative
          </button>
          <button
            onClick={() => setLabelStatus(LabelStatus.Unlabeled)}
            className={`rounded px-4 py-1.5 text-sm font-medium ${
              currentLabel === LabelStatus.Unlabeled
                ? 'bg-gray-600 text-white'
                : 'bg-gray-700 text-gray-300 hover:bg-gray-600/30'
            }`}
          >
            Clear
          </button>
        </div>

        {showNotes && (
          <div className="mt-3">
            <label className="mb-1 block text-xs text-gray-400">Reviewer Notes</label>
            <textarea
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              rows={2}
              className="w-full rounded border border-gray-600 bg-gray-900 px-3 py-2 text-sm text-white placeholder-gray-500 focus:border-indigo-500 focus:outline-none"
              placeholder="Add notes about this trace…"
            />
          </div>
        )}

        {showCorrection && (
          <div className="mt-3">
            <label className="mb-1 block text-xs text-gray-400">Correction Text</label>
            <textarea
              value={correction}
              onChange={(e) => setCorrection(e.target.value)}
              rows={3}
              className="w-full rounded border border-gray-600 bg-gray-900 px-3 py-2 text-sm text-white placeholder-gray-500 focus:border-indigo-500 focus:outline-none"
              placeholder="What should the correct response be?"
            />
          </div>
        )}

        <button
          onClick={() => mutation.mutate()}
          disabled={mutation.isPending}
          className="mt-3 rounded bg-indigo-600 px-4 py-1.5 text-sm font-medium text-white hover:bg-indigo-500 disabled:opacity-50"
        >
          {mutation.isPending ? 'Saving…' : 'Save Label'}
        </button>
        {mutation.isError && (
          <p className="mt-2 text-sm text-red-400">Failed to save label.</p>
        )}
        {mutation.isSuccess && (
          <p className="mt-2 text-sm text-green-400">Label saved.</p>
        )}
      </div>
    </div>
  )
}
