import type { TracedSpan } from '../types'

interface SpanTimelineProps {
  spans: TracedSpan[]
  totalDurationMs: number
}

function getSpanColor(span: TracedSpan): string {
  const cacheResult = span.tags['cache.result']
  if (cacheResult === 'hit') return 'bg-sage/70'
  if (cacheResult === 'miss' || cacheResult === 'bypass') return 'bg-amber/60'
  return 'bg-dust/30'
}

function getSpanLabel(span: TracedSpan): string {
  const parts: string[] = [span.operationName]
  const agentId = span.tags['agent.id']
  if (agentId) parts.push(`[${agentId}]`)
  return parts.join(' ')
}

function getCacheBadge(span: TracedSpan): { text: string; className: string } | null {
  const cacheResult = span.tags['cache.result']
  if (cacheResult === 'hit') return { text: 'cached', className: 'bg-sage/20 text-sage' }
  if (cacheResult === 'miss') return { text: 'LLM', className: 'bg-amber/20 text-amber' }
  if (cacheResult === 'bypass') return { text: 'bypass', className: 'bg-amber/20 text-amber' }
  return null
}

export default function SpanTimeline({ spans, totalDurationMs }: SpanTimelineProps) {
  if (!spans || spans.length === 0) return null

  const sorted = [...spans].sort(
    (a, b) => new Date(a.startTimeUtc).getTime() - new Date(b.startTimeUtc).getTime()
  )

  const timelineStart = new Date(sorted[0].startTimeUtc).getTime()
  const spanEnd = Math.max(
    ...sorted.map(s => new Date(s.startTimeUtc).getTime() - timelineStart + s.durationMs)
  )
  const timelineWidth = Math.max(totalDurationMs, spanEnd, 1)

  const hitCount = sorted.filter(s => s.tags['cache.result'] === 'hit').length
  const llmCount = sorted.filter(s => {
    const r = s.tags['cache.result']
    return r === 'miss' || r === 'bypass'
  }).length

  return (
    <div>
      <h3 className="mb-3 text-xs font-semibold uppercase tracking-wider text-dust">
        Span Timeline
        <span className="ml-2 font-normal normal-case tracking-normal text-dust/60">
          {spans.length} spans
          {hitCount > 0 && <span className="ml-1.5 text-sage">· {hitCount} cached</span>}
          {llmCount > 0 && <span className="ml-1.5 text-amber">· {llmCount} LLM</span>}
        </span>
      </h3>
      <div className="glass-panel rounded-xl p-4">
        {/* Waterfall */}
        <div className="space-y-1">
          {sorted.map((span) => {
            const offsetMs = new Date(span.startTimeUtc).getTime() - timelineStart
            const leftPct = (offsetMs / timelineWidth) * 100
            const widthPct = Math.max((span.durationMs / timelineWidth) * 100, 0.5)
            const badge = getCacheBadge(span)

            return (
              <div key={span.spanId} className="flex items-center gap-2">
                <div className="w-48 shrink-0 flex items-center justify-end gap-1.5">
                  {badge && (
                    <span className={`rounded px-1 py-0.5 text-[10px] font-medium leading-none ${badge.className}`}>
                      {badge.text}
                    </span>
                  )}
                  <span className="truncate text-right text-xs text-dust" title={getSpanLabel(span)}>
                    {getSpanLabel(span)}
                  </span>
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
