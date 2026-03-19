import type { CommandTraceToolCall } from '../types'

interface CommandTimelineProps {
  matchDurationMs: number
  executionDurationMs?: number
  totalDurationMs: number
  toolCalls?: CommandTraceToolCall[]
}

interface Phase {
  label: string
  durationMs: number
  color: string
  children?: { label: string; durationMs: number; color: string }[]
}

export default function CommandTimeline({
  matchDurationMs,
  executionDurationMs,
  totalDurationMs,
  toolCalls,
}: CommandTimelineProps) {
  const timelineWidth = Math.max(totalDurationMs, 1)
  const execMs = executionDurationMs ?? 0
  const otherMs = Math.max(totalDurationMs - matchDurationMs - execMs, 0)

  const phases: Phase[] = [
    { label: 'Pattern Match', durationMs: matchDurationMs, color: 'bg-sky-400/60' },
  ]

  if (executionDurationMs !== undefined && executionDurationMs > 0) {
    const children = toolCalls?.map((tc) => ({
      label: tc.methodName,
      durationMs: tc.durationMs,
      color: 'bg-amber/40',
    }))
    phases.push({
      label: 'Skill Execution',
      durationMs: executionDurationMs,
      color: 'bg-amber/60',
      children,
    })
  }

  if (otherMs > 0) {
    phases.push({ label: 'Other', durationMs: otherMs, color: 'bg-dust/30' })
  }

  let offsetMs = 0

  return (
    <div>
      <h3 className="mb-3 text-xs font-semibold uppercase tracking-wider text-dust">
        Timing Waterfall
        <span className="ml-2 font-normal normal-case tracking-normal text-dust/60">
          {totalDurationMs.toFixed(1)}ms total
        </span>
      </h3>
      <div className="glass-panel rounded-xl p-4">
        <div className="space-y-1">
          {phases.map((phase) => {
            const leftPct = (offsetMs / timelineWidth) * 100
            const widthPct = Math.max((phase.durationMs / timelineWidth) * 100, 0.5)
            const currentOffset = offsetMs
            offsetMs += phase.durationMs

            return (
              <div key={phase.label}>
                <div className="flex items-center gap-2">
                  <div className="w-32 shrink-0 truncate text-right text-xs text-dust">
                    {phase.label}
                  </div>
                  <div className="relative h-5 flex-1 rounded bg-void/40">
                    <div
                      className={`absolute top-0.5 bottom-0.5 rounded ${phase.color}`}
                      style={{ left: `${leftPct}%`, width: `${widthPct}%` }}
                      title={`${phase.label}: ${phase.durationMs.toFixed(1)}ms`}
                    />
                  </div>
                  <span className="w-16 shrink-0 text-right font-mono text-xs text-dust">
                    {phase.durationMs.toFixed(1)}ms
                  </span>
                </div>

                {phase.children && phase.children.length > 0 && (
                  <div className="ml-4 space-y-0.5">
                    {(() => {
                      let childOffset = currentOffset
                      return phase.children.map((child, j) => {
                        const cLeft = (childOffset / timelineWidth) * 100
                        const cWidth = Math.max((child.durationMs / timelineWidth) * 100, 0.3)
                        childOffset += child.durationMs
                        return (
                          <div key={j} className="flex items-center gap-2">
                            <div className="w-28 shrink-0 truncate text-right text-[11px] text-dust/70">
                              {child.label}
                            </div>
                            <div className="relative h-3 flex-1 rounded bg-void/30">
                              <div
                                className={`absolute top-0.5 bottom-0.5 rounded ${child.color}`}
                                style={{ left: `${cLeft}%`, width: `${cWidth}%` }}
                                title={`${child.label}: ${child.durationMs.toFixed(1)}ms`}
                              />
                            </div>
                            <span className="w-16 shrink-0 text-right font-mono text-[11px] text-dust/70">
                              {child.durationMs.toFixed(1)}ms
                            </span>
                          </div>
                        )
                      })
                    })()}
                  </div>
                )}
              </div>
            )
          })}
        </div>

        {/* Time axis */}
        <div className="mt-2 flex justify-between pl-36 text-xs text-dust/60">
          <span>0ms</span>
          <span>{(timelineWidth / 2).toFixed(0)}ms</span>
          <span>{timelineWidth.toFixed(0)}ms</span>
        </div>
      </div>
    </div>
  )
}
