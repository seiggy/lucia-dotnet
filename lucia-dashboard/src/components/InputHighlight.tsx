import { useState } from 'react'
import type { TokenHighlight } from '../types'

interface InputHighlightProps {
  text: string
  highlights: TokenHighlight[]
}

const HIGHLIGHT_STYLES: Record<string, { bg: string; tooltip: string }> = {
  literal: { bg: 'bg-sky-400/20 text-sky-300 border-b border-sky-400/40', tooltip: 'Literal' },
  capture: { bg: 'bg-amber/20 text-amber', tooltip: 'Capture' },
  constrainedCapture: { bg: 'bg-sage/20 text-sage', tooltip: 'Constrained Capture' },
  optional: { bg: 'bg-dust/15 text-dust', tooltip: 'Optional' },
}

export default function InputHighlight({ text, highlights }: InputHighlightProps) {
  const [hoveredIdx, setHoveredIdx] = useState<number | null>(null)

  if (!highlights || highlights.length === 0) {
    return (
      <div className="font-mono text-sm bg-void/50 rounded-lg p-3 border border-stone text-light">
        {text}
      </div>
    )
  }

  const sorted = [...highlights].sort((a, b) => a.start - b.start)

  const segments: { text: string; highlight: TokenHighlight | null; idx: number }[] = []
  let cursor = 0

  sorted.forEach((hl, i) => {
    if (hl.start > cursor) {
      segments.push({ text: text.slice(cursor, hl.start), highlight: null, idx: -1 })
    }
    segments.push({ text: text.slice(hl.start, hl.end), highlight: hl, idx: i })
    cursor = hl.end
  })

  if (cursor < text.length) {
    segments.push({ text: text.slice(cursor), highlight: null, idx: -1 })
  }

  return (
    <div className="font-mono text-sm bg-void/50 rounded-lg p-3 border border-stone relative">
      {segments.map((seg, i) => {
        if (!seg.highlight) {
          return <span key={i} className="text-light">{seg.text}</span>
        }
        const style = HIGHLIGHT_STYLES[seg.highlight.type] ?? { bg: 'bg-dust/10 text-fog', tooltip: seg.highlight.type }
        return (
          <span
            key={i}
            className={`relative inline-block rounded px-0.5 ${style.bg} cursor-help`}
            onMouseEnter={() => setHoveredIdx(seg.idx)}
            onMouseLeave={() => setHoveredIdx(null)}
          >
            {seg.text}
            {hoveredIdx === seg.idx && (
              <span className="absolute bottom-full left-1/2 -translate-x-1/2 mb-1 whitespace-nowrap rounded bg-obsidian border border-stone px-2 py-1 text-[10px] text-fog shadow-lg z-10">
                {style.tooltip}: {seg.highlight.value}
              </span>
            )}
          </span>
        )
      })}
    </div>
  )
}
