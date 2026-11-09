import { useRef } from 'react'
import type { KeyboardEvent, ReactNode } from 'react'

export interface TabItem<T extends string> {
  id: T
  label: string
  icon?: ReactNode
}

interface TabsProps<T extends string> {
  label: string
  idPrefix: string
  tabs: TabItem<T>[]
  value: T
  onChange: (value: T) => void
  className?: string
}

/** Tab list with roving focus and arrow-key navigation. Pair panels with `tabPanelProps`. */
export default function Tabs<T extends string>({ label, idPrefix, tabs, value, onChange, className = '' }: TabsProps<T>) {
  const refs = useRef(new Map<T, HTMLButtonElement>())

  function onKeyDown(event: KeyboardEvent<HTMLButtonElement>, index: number) {
    const last = tabs.length - 1
    const next = event.key === 'ArrowRight' ? (index === last ? 0 : index + 1)
      : event.key === 'ArrowLeft' ? (index === 0 ? last : index - 1)
        : event.key === 'Home' ? 0
          : event.key === 'End' ? last
            : -1
    if (next < 0) return
    event.preventDefault()
    onChange(tabs[next].id)
    refs.current.get(tabs[next].id)?.focus()
  }

  return (
    <div role="tablist" aria-label={label} className={`flex gap-1 overflow-x-auto border-b border-stone ${className}`}>
      {tabs.map((tab, index) => {
        const selected = tab.id === value
        return (
          <button
            key={tab.id}
            ref={element => {
              if (element) refs.current.set(tab.id, element)
              else refs.current.delete(tab.id)
            }}
            type="button"
            role="tab"
            id={`${idPrefix}-tab-${tab.id}`}
            aria-selected={selected}
            aria-controls={`${idPrefix}-panel-${tab.id}`}
            tabIndex={selected ? 0 : -1}
            onClick={() => onChange(tab.id)}
            onKeyDown={event => onKeyDown(event, index)}
            className={`-mb-px inline-flex min-h-11 shrink-0 items-center gap-2 border-b-2 px-3 text-sm font-medium transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-amber/60 ${
              selected ? 'border-amber text-amber' : 'border-transparent text-fog hover:text-light'
            }`}
          >
            {tab.icon}
            {tab.label}
          </button>
        )
      })}
    </div>
  )
}
