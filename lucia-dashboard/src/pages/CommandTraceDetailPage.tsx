import { useState } from 'react'
import { useParams, useNavigate } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { fetchCommandTrace } from '../api'
import type { CommandTraceOutcome } from '../types'
import InputHighlight from '../components/InputHighlight'
import CommandTimeline from '../components/CommandTimeline'
import {
  ArrowLeft, Clock, Timer, AlertTriangle, CheckCircle2, XCircle,
  ChevronDown, Loader2, Hash, MapPin, Zap, Brain, ExternalLink, Download, Bug,
} from 'lucide-react'
import { downloadJson, buildCommandTraceIssueUrl } from '../utils/traceExport'

function formatDate(iso: string) {
  return new Date(iso).toLocaleString()
}

function outcomeBadge(outcome: CommandTraceOutcome) {
  switch (outcome) {
    case 'commandHandled':
      return <span className="rounded-full bg-sage/15 px-2.5 py-1 text-xs font-medium text-sage">⚡ Command Handled</span>
    case 'llmFallback':
      return <span className="rounded-full bg-violet-500/15 px-2.5 py-1 text-xs font-medium text-violet-400">🤖 LLM Fallback</span>
    case 'llmCompleted':
      return <span className="rounded-full bg-violet-500/15 px-2.5 py-1 text-xs font-medium text-violet-400">🤖 LLM Completed</span>
    case 'error':
      return <span className="rounded-full bg-ember/15 px-2.5 py-1 text-xs font-medium text-rose">Error</span>
    default:
      return <span className="rounded-full bg-stone/50 px-2.5 py-1 text-xs font-medium text-dust">{outcome}</span>
  }
}

function ScoreBar({ score, label }: { score: number; label: string }) {
  const pct = Math.round(score * 100)
  const color = pct >= 80 ? 'bg-sage' : pct >= 50 ? 'bg-amber' : 'bg-rose'
  return (
    <div className="flex items-center gap-2 text-xs">
      <span className="w-20 text-fog shrink-0">{label}</span>
      <div className="flex-1 h-1.5 bg-stone rounded-full overflow-hidden">
        <div
          className={`h-full rounded-full transition-all duration-500 ${color}`}
          style={{ width: `${pct}%` }}
        />
      </div>
      <span className="w-12 text-right font-mono tabular-nums text-amber">
        {pct}%
      </span>
    </div>
  )
}

export default function CommandTraceDetailPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const [contextOpen, setContextOpen] = useState(false)
  const [promptOpen, setPromptOpen] = useState(false)

  const { data: trace, isLoading, isError } = useQuery({
    queryKey: ['command-trace', id],
    queryFn: () => fetchCommandTrace(id!),
    enabled: !!id,
  })

  if (isLoading) return (
    <div className="flex items-center gap-2 py-12 text-fog">
      <Loader2 className="h-4 w-4 animate-spin" /> Loading command trace…
    </div>
  )
  if (isError || !trace) return <p className="text-rose">Command trace not found.</p>

  const { match, execution, llmFallback, templateRender, requestContext } = trace

  return (
    <div className="space-y-6">
      {/* Back button + actions */}
      <div className="flex items-center gap-3">
        <button
          onClick={() => navigate('/command-traces')}
          className="flex items-center gap-1.5 text-sm text-amber transition-colors hover:text-amber-glow"
        >
          <ArrowLeft className="h-4 w-4" /> Back to Command Traces
        </button>
        <div className="ml-auto flex items-center gap-2">
          <button
            onClick={() => downloadJson(trace, `command-trace-${trace.id}.json`)}
            className="flex items-center gap-1.5 rounded-xl border border-stone bg-basalt px-3 py-1.5 text-sm text-fog transition-colors hover:border-amber/30 hover:text-light"
          >
            <Download className="h-4 w-4" /> Export JSON
          </button>
          <a
            href={buildCommandTraceIssueUrl(trace)}
            target="_blank"
            rel="noopener noreferrer"
            className="flex items-center gap-1.5 rounded-xl border border-stone bg-basalt px-3 py-1.5 text-sm text-fog transition-colors hover:border-amber/30 hover:text-light"
          >
            <Bug className="h-4 w-4" /> Report Issue
          </a>
        </div>
      </div>

      {/* Header card */}
      <div className="glass-panel rounded-xl p-5">
        <div className="flex flex-wrap gap-6 text-sm">
          <div>
            <span className="flex items-center gap-1 text-xs text-dust"><Clock className="h-3 w-3" /> Timestamp</span>
            <p className="mt-0.5 text-light">{formatDate(trace.timestamp)}</p>
          </div>
          {requestContext.conversationId && (
            <div>
              <span className="flex items-center gap-1 text-xs text-dust"><Hash className="h-3 w-3" /> Conversation</span>
              <p className="mt-0.5 font-mono text-xs text-fog">{requestContext.conversationId}</p>
            </div>
          )}
          {requestContext.deviceArea && (
            <div>
              <span className="flex items-center gap-1 text-xs text-dust"><MapPin className="h-3 w-3" /> Area</span>
              <p className="mt-0.5 text-light">{requestContext.deviceArea}</p>
            </div>
          )}
          <div>
            <span className="flex items-center gap-1 text-xs text-dust"><Timer className="h-3 w-3" /> Duration</span>
            <p className="mt-0.5 text-light">{trace.totalDurationMs} ms</p>
          </div>
          <div>
            <span className="text-xs text-dust">Outcome</span>
            <div className="mt-0.5">{outcomeBadge(trace.outcome)}</div>
          </div>
        </div>
        {trace.error && (
          <p className="mt-3 rounded-lg border border-ember/20 bg-ember/10 p-2 text-sm text-rose">
            <AlertTriangle className="mr-1 inline h-3 w-3" />{trace.error}
          </p>
        )}
      </div>

      {/* Input & Highlights */}
      <div className="glass-panel rounded-xl p-5">
        <h3 className="mb-3 text-xs font-semibold uppercase tracking-wider text-dust">Input</h3>
        <div className="space-y-3">
          <div>
            <span className="text-xs text-dust">Raw Text</span>
            <p className="mt-1 whitespace-pre-wrap text-light">{trace.rawText}</p>
          </div>
          <div>
            <span className="text-xs text-dust">Matched Text (with highlights)</span>
            <div className="mt-1">
              {match.tokenHighlights && match.tokenHighlights.length > 0 && trace.normalizedText ? (
                <InputHighlight text={trace.normalizedText} highlights={match.tokenHighlights} />
              ) : (
                <div className="font-mono text-sm bg-void/50 rounded-lg p-3 border border-stone text-light">
                  {trace.cleanText}
                </div>
              )}
            </div>
          </div>
          {!match.isMatch && (
            <div className="flex items-center gap-2 rounded-lg border border-stone bg-void/30 p-2 text-sm text-dust">
              <XCircle className="h-4 w-4 text-dust" />
              No pattern match
            </div>
          )}
        </div>
      </div>

      {/* Pattern Match card */}
      {match.isMatch && (
        <div className="glass-panel rounded-xl p-5">
          <h3 className="mb-3 text-xs font-semibold uppercase tracking-wider text-dust">
            <Zap className="mr-1 inline h-3.5 w-3.5 text-sage" />
            Pattern Match
          </h3>
          <div className="flex flex-wrap gap-6 text-sm mb-4">
            {match.patternId && (
              <div>
                <span className="text-xs text-dust">Pattern ID</span>
                <p className="mt-0.5 font-mono text-xs text-fog">{match.patternId}</p>
              </div>
            )}
            {match.templateUsed && (
              <div>
                <span className="text-xs text-dust">Template</span>
                <p className="mt-0.5 font-mono text-xs text-fog">{match.templateUsed}</p>
              </div>
            )}
            {match.skillId && (
              <div>
                <span className="text-xs text-dust">Skill</span>
                <p className="mt-0.5 text-amber">{match.skillId}</p>
              </div>
            )}
            {match.action && (
              <div>
                <span className="text-xs text-dust">Action</span>
                <p className="mt-0.5 text-light">{match.action}</p>
              </div>
            )}
            <div>
              <span className="text-xs text-dust">Match Duration</span>
              <p className="mt-0.5 font-mono text-xs text-light">{match.matchDurationMs} ms</p>
            </div>
          </div>

          <ScoreBar score={match.confidence} label="Confidence" />

          {match.capturedValues && Object.keys(match.capturedValues).length > 0 && (
            <div className="mt-4">
              <h4 className="mb-2 text-xs font-medium uppercase tracking-wider text-dust">Captured Values</h4>
              <div className="space-y-1">
                {Object.entries(match.capturedValues).map(([key, value]) => (
                  <div key={key} className="flex gap-2 rounded-lg border border-stone bg-void/50 px-3 py-1.5 text-xs">
                    <span className="font-medium text-amber">{key}</span>
                    <span className="text-stone">→</span>
                    <span className="text-fog">{value}</span>
                  </div>
                ))}
              </div>
            </div>
          )}
        </div>
      )}

      {/* Skill Execution card */}
      {execution && (
        <div className="glass-panel rounded-xl p-5">
          <h3 className="mb-3 text-xs font-semibold uppercase tracking-wider text-dust">Skill Execution</h3>
          <div className="flex flex-wrap items-center gap-3 mb-3">
            <span className="rounded-md bg-amber/15 px-2 py-0.5 text-sm font-medium text-amber">
              {execution.skillId}
            </span>
            <span className="text-xs text-dust">/ {execution.action}</span>
            <span className="font-mono text-xs text-dust">{execution.durationMs} ms</span>
            {execution.success ? (
              <span className="flex items-center gap-1 rounded-full bg-sage/15 px-2 py-0.5 text-xs text-sage">
                <CheckCircle2 className="h-3 w-3" /> Success
              </span>
            ) : (
              <span className="flex items-center gap-1 rounded-full bg-ember/15 px-2 py-0.5 text-xs text-rose">
                <XCircle className="h-3 w-3" /> Error
              </span>
            )}
          </div>

          {execution.error && (
            <p className="mb-3 rounded-lg border border-ember/20 bg-ember/10 p-2 text-sm text-rose">{execution.error}</p>
          )}

          {execution.responseText && (
            <div>
              <h4 className="mb-1 text-xs font-medium uppercase tracking-wider text-dust">Response</h4>
              <p className="whitespace-pre-wrap text-sm text-fog">{execution.responseText}</p>
            </div>
          )}
        </div>
      )}

      {/* Tool Calls */}
      {execution?.toolCalls && execution.toolCalls.length > 0 && (
        <div>
          <h3 className="mb-3 text-xs font-semibold uppercase tracking-wider text-dust">
            Tool Calls ({execution.toolCalls.length})
          </h3>
          <div className="space-y-2">
            {execution.toolCalls.map((tc, i) => (
              <div key={i} className="glass-panel rounded-xl p-4">
                <div className="flex flex-wrap items-center gap-3 mb-2">
                  <span className="rounded-md bg-amber/15 px-2 py-0.5 text-sm font-medium text-amber">
                    {tc.methodName}
                  </span>
                  <span className="font-mono text-xs text-dust">{tc.durationMs} ms</span>
                  {tc.success ? (
                    <span className="flex items-center gap-1 rounded-full bg-sage/15 px-2 py-0.5 text-xs text-sage">
                      <CheckCircle2 className="h-3 w-3" /> Success
                    </span>
                  ) : (
                    <span className="flex items-center gap-1 rounded-full bg-ember/15 px-2 py-0.5 text-xs text-rose">
                      <XCircle className="h-3 w-3" /> Error
                    </span>
                  )}
                </div>
                {tc.error && (
                  <p className="mb-2 rounded-lg border border-ember/20 bg-ember/10 p-2 text-sm text-rose">{tc.error}</p>
                )}
                {tc.arguments && (
                  <div className="mb-2">
                    <h4 className="mb-1 text-xs font-medium uppercase tracking-wider text-dust">Arguments</h4>
                    <pre className="overflow-x-auto rounded-lg border border-stone bg-void/50 p-2 text-xs text-dust">
                      {tc.arguments}
                    </pre>
                  </div>
                )}
                {tc.response && (
                  <div>
                    <h4 className="mb-1 text-xs font-medium uppercase tracking-wider text-dust">Response</h4>
                    <pre className="overflow-x-auto rounded-lg border border-stone bg-void/50 p-2 text-xs text-fog">
                      {tc.response}
                    </pre>
                  </div>
                )}
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Response Template */}
      {templateRender && (
        <div className="glass-panel rounded-xl p-5">
          <h3 className="mb-3 text-xs font-semibold uppercase tracking-wider text-dust">Response Template</h3>
          <div className="space-y-3">
            <div className="flex flex-wrap items-center gap-3">
              <span className="rounded-md bg-amber/15 px-2 py-0.5 text-sm font-medium text-amber">
                {templateRender.templateKey}
              </span>
              {templateRender.isFallback ? (
                <span className="rounded-full bg-dust/20 px-2 py-0.5 text-xs text-dust">Fallback (no template found)</span>
              ) : (
                <span className="rounded-full bg-sage/15 px-2 py-0.5 text-xs text-sage">
                  Variant {templateRender.selectedIndex + 1} of {templateRender.variantCount}
                </span>
              )}
            </div>

            {!templateRender.isFallback && (
              <div>
                <span className="text-xs font-medium text-dust">Raw Template</span>
                <div className="mt-1 rounded-lg border border-stone bg-void/50 p-3 font-mono text-sm">
                  {templateRender.rawTemplate.split(/(\{[^}]+\})/).map((part, i) => {
                    const tokenMatch = part.match(/^\{(\w+)\}$/)
                    if (tokenMatch) {
                      const key = tokenMatch[1]
                      const replaced = templateRender.replacedTokens[key]
                      return (
                        <span key={i} className="relative inline-block rounded bg-amber/20 px-0.5 text-amber" title={replaced ? `→ "${replaced}"` : 'not replaced'}>
                          {part}
                        </span>
                      )
                    }
                    return <span key={i} className="text-fog">{part}</span>
                  })}
                </div>
              </div>
            )}

            {Object.keys(templateRender.replacedTokens).length > 0 && (
              <div>
                <span className="text-xs font-medium text-dust">Replaced Tokens</span>
                <div className="mt-1 flex flex-wrap gap-2">
                  {Object.entries(templateRender.replacedTokens).map(([key, value]) => (
                    <span key={key} className="rounded-lg border border-stone bg-void/50 px-2 py-1 text-xs">
                      <span className="text-amber">{`{${key}}`}</span>
                      <span className="mx-1.5 text-stone">→</span>
                      <span className="text-sage">{value}</span>
                    </span>
                  ))}
                </div>
              </div>
            )}

            <div>
              <span className="text-xs font-medium text-dust">Rendered Output</span>
              <p className="mt-1 whitespace-pre-wrap text-sm text-light">{templateRender.renderedText}</p>
            </div>
          </div>
        </div>
      )}

      {/* LLM Fallback */}
      {llmFallback && (
        <div className="glass-panel rounded-xl p-5">
          <h3 className="mb-3 text-xs font-semibold uppercase tracking-wider text-dust">
            <Brain className="mr-1 inline h-3.5 w-3.5 text-violet-400" />
            LLM Fallback
          </h3>
          <div className="flex flex-wrap gap-6 text-sm mb-3">
            <div>
              <span className="text-xs text-dust">Duration</span>
              <p className="mt-0.5 font-mono text-xs text-light">{llmFallback.durationMs} ms</p>
            </div>
            {llmFallback.orchestrationTraceId && (
              <div>
                <span className="text-xs text-dust">LLM Trace</span>
                <button
                  onClick={() => navigate(`/traces/${llmFallback.orchestrationTraceId}`)}
                  className="mt-0.5 flex items-center gap-1 text-sm text-amber transition-colors hover:text-amber-glow"
                >
                  View LLM Trace <ExternalLink className="h-3 w-3" />
                </button>
              </div>
            )}
          </div>

          {llmFallback.prompt && (
            <details open={promptOpen} onToggle={(e) => setPromptOpen((e.target as HTMLDetailsElement).open)}>
              <summary className="flex cursor-pointer items-center gap-2 text-sm text-dust hover:text-fog">
                <ChevronDown className={`h-4 w-4 transition-transform ${promptOpen ? 'rotate-180' : ''}`} />
                Prompt
              </summary>
              <pre className="mt-2 overflow-x-auto rounded-lg border border-stone bg-void/50 p-3 text-xs text-fog whitespace-pre-wrap">
                {llmFallback.prompt}
              </pre>
            </details>
          )}
        </div>
      )}

      {/* Timing Waterfall */}
      <CommandTimeline
        matchDurationMs={match.matchDurationMs}
        executionDurationMs={execution?.durationMs}
        totalDurationMs={trace.totalDurationMs}
        toolCalls={execution?.toolCalls}
      />

      {/* Context card (collapsible) */}
      <details open={contextOpen} onToggle={(e) => setContextOpen((e.target as HTMLDetailsElement).open)}>
        <summary className="glass-panel flex cursor-pointer items-center gap-2 rounded-xl p-4 text-sm font-semibold uppercase tracking-wider text-dust hover:text-fog">
          <ChevronDown className={`h-4 w-4 transition-transform ${contextOpen ? 'rotate-180' : ''}`} />
          Request Context
        </summary>
        <div className="glass-panel mt-1 rounded-xl p-5">
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
            {requestContext.conversationId && (
              <ContextField label="Conversation ID" value={requestContext.conversationId} />
            )}
            {requestContext.deviceId && (
              <ContextField label="Device ID" value={requestContext.deviceId} />
            )}
            {requestContext.deviceArea && (
              <ContextField label="Device Area" value={requestContext.deviceArea} />
            )}
            {requestContext.deviceType && (
              <ContextField label="Device Type" value={requestContext.deviceType} />
            )}
            {requestContext.userId && (
              <ContextField label="User ID" value={requestContext.userId} />
            )}
            {requestContext.speakerId && (
              <ContextField label="Speaker ID" value={requestContext.speakerId} />
            )}
            {requestContext.location && (
              <ContextField label="Location" value={requestContext.location} />
            )}
            {trace.speakerId && (
              <ContextField label="Speaker" value={trace.speakerId} />
            )}
          </div>
        </div>
      </details>

      {/* Response text */}
      {trace.responseText && (
        <div className="glass-panel rounded-xl p-5">
          <h3 className="mb-2 text-xs font-semibold uppercase tracking-wider text-dust">Response</h3>
          <p className="whitespace-pre-wrap text-sm text-fog">{trace.responseText}</p>
        </div>
      )}
    </div>
  )
}

function ContextField({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <span className="text-xs text-dust">{label}</span>
      <p className="mt-0.5 font-mono text-xs text-fog break-all">{value}</p>
    </div>
  )
}
