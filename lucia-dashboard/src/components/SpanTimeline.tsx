import type { TracedSpan } from '../types'

interface SpanTimelineProps {
  spans: TracedSpan[]
  totalDurationMs: number
}

function getSpanColor(span: TracedSpan): string {
  const cacheResult = span.tags['cache.result']
  if (cacheResult === 'hit') return 'bg-sage/60'
  if (cacheResult === 'miss' || cacheResult === 'bypass') return 'bg-amber/60'

  if (span.source.includes('AgentDispatch')) return 'bg-cyan/50'
  if (span.source.includes('AgentInvoker')) return 'bg-violet/50'
  if (span.source.includes('Router')) return 'bg-sky/50'
  if (span.source.includes('ChatCache') || span.source.includes('PromptCache')) return 'bg-amber/40'
  return 'bg-dust/40'
}

function getSpanLabel(span: TracedSpan): string {
  const parts: string[] = [span.operationName]
  const cacheResult = span.tags['cache.result']
  if (cacheResult) parts.push(`(${cacheResult})`)
  const agentId = span.tags['agent.id']
  if (agentId) parts.push(`[${agentId}]`)
  const round = span.tags['cache.round']
  if (round) parts.push(`(${round})`)
  return parts.join(' ')
}

export default function SpanTimeline({ spans, totalDurationMs }: SpanTimelineProps) {
  if (!spans || spans.length === 0) return null

  // Sort by start time
  const sorted = [...spans].sort(
    (a, b) => new Date(a.startTimeUtc).getTime() - new Date(b.startTimeUtc).getTime()
  )

  const timelineStart = new Date(sorted[0].startTimeUtc).getTime()
  // Use max(totalDurationMs, span range) to avoid bars exceeding 100%
  const spanEnd = Math.max(
    ...sorted.map(s => new Date(s.startTimeUtc).getTime() - timelineStart + s.durationMs)
  )
  const timelineWidth = Math.max(totalDurationMs, spanEnd, 1)

  return (
    <div>
      <h3 className="mb-3 text-xs font-semibold uppercase tracking-wider text-dust">
        Span Timeline ({spans.length} spans)
      </h3>
      <div className="glass-panel rounded-xl p-4">
        {/* Legend */}
        <div className="mb-3 flex flex-wrap gap-3 text-xs text-dust">
          <span className="flex items-center gap-1">
            <span className="inline-block h-2.5 w-2.5 rounded-sm bg-sage/60" /> Cache hit
          </span>
          <span className="flex items-center gap-1">
            <span className="inline-block h-2.5 w-2.5 rounded-sm bg-amber/60" /> Cache miss
          </span>
          <span className="flex items-center gap-1">
            <span className="inline-block h-2.5 w-2.5 rounded-sm bg-cyan/50" /> Agent dispatch
          </span>
          <span className="flex items-center gap-1">
            <span className="inline-block h-2.5 w-2.5 rounded-sm bg-violet/50" /> Invoker
          </span>
          <span className="flex items-center gap-1">
            <span className="inline-block h-2.5 w-2.5 rounded-sm bg-sky/50" /> Router
          </span>
        </div>

        {/* Waterfall */}
        <div className="space-y-1">
          {sorted.map((span) => {
            const offsetMs = new Date(span.startTimeUtc).getTime() - timelineStart
            const leftPct = (offsetMs / timelineWidth) * 100
            const widthPct = Math.max((span.durationMs / timelineWidth) * 100, 0.5)

            return (
              <div key={span.spanId} className="flex items-center gap-2">
                <div className="w-48 shrink-0 truncate text-right text-xs text-dust" title={getSpanLabel(span)}>
                  {getSpanLabel(span)}
                </div>
                <div className="relative h-5 flex-1 rounded bg-void/40">
                  <div
                    className={`absolute top-0.5 bottom-0.5 rounded ${getSpanColor(span)}`}
                    style={{ left: `${leftPct}%`, width: `${widthPct}%` }}
                    title={`${span.operationName}: ${span.durationMs.toFixed(1)}ms\n${Object.entries(span.tags).map(([k, v]) => `${k}: ${v}`).join('\n')}`}
                  />
                </div>
                <span className="w-16 shrink-0 text-right font-mono text-xs text-dust">
                  {span.durationMs.toFixed(1)}ms
                </span>
              </div>
            )
          })}
        </div>

        {/* Time axis */}
        <div className="mt-2 flex justify-between pl-50 text-xs text-dust/60">
          <span>0ms</span>
          <span>{(timelineWidth / 2).toFixed(0)}ms</span>
          <span>{timelineWidth.toFixed(0)}ms</span>
        </div>
      </div>
    </div>
  )
}
