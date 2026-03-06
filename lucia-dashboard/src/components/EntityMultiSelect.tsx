import { useState, useRef, useEffect, useCallback } from 'react'
import { createPortal } from 'react-dom'
import type { SkillDeviceInfo } from '../types'

interface EntityMultiSelectProps {
  devices: SkillDeviceInfo[]
  selected: string[]
  onChange: (ids: string[]) => void
}

/**
 * Multi-select dropdown for choosing expected entity IDs.
 * Shows selected entities as chips with remove buttons, and a
 * searchable dropdown for adding more.
 */
export default function EntityMultiSelect({ devices, selected, onChange }: EntityMultiSelectProps) {
  const [open, setOpen] = useState(false)
  const [filter, setFilter] = useState('')
  const containerRef = useRef<HTMLDivElement>(null)
  const dropdownRef = useRef<HTMLDivElement>(null)
  const inputRef = useRef<HTMLInputElement>(null)
  const [dropdownPos, setDropdownPos] = useState({ top: 0, left: 0, width: 0 })

  const selectedSet = new Set(selected)
  const filtered = devices.filter(
    (d) =>
      !selectedSet.has(d.entityId) &&
      (d.friendlyName.toLowerCase().includes(filter.toLowerCase()) ||
        d.entityId.toLowerCase().includes(filter.toLowerCase()))
  )

  const add = useCallback(
    (entityId: string) => {
      onChange([...selected, entityId])
      setFilter('')
    },
    [selected, onChange]
  )

  const remove = useCallback(
    (entityId: string) => {
      onChange(selected.filter((id) => id !== entityId))
    },
    [selected, onChange]
  )

  useEffect(() => {
    if (!open) return
    const handleClick = (e: MouseEvent) => {
      const target = e.target as Node
      if (containerRef.current?.contains(target) || dropdownRef.current?.contains(target)) return
      setOpen(false)
    }
    document.addEventListener('mousedown', handleClick)
    return () => document.removeEventListener('mousedown', handleClick)
  }, [open])

  useEffect(() => {
    if (open && containerRef.current) {
      const rect = containerRef.current.getBoundingClientRect()
      setDropdownPos({ top: rect.bottom + 4, left: rect.left, width: rect.width })
    }
  }, [open])

  const deviceMap = new Map(devices.map((d) => [d.entityId, d.friendlyName]))

  return (
    <div ref={containerRef} className="relative">
      {/* Selected chips + input */}
      <div
        className="flex min-h-[28px] flex-wrap items-center gap-1 rounded border border-stone/30 bg-ash/40 px-1.5 py-0.5 text-xs cursor-text"
        onClick={() => {
          setOpen(true)
          setTimeout(() => inputRef.current?.focus(), 0)
        }}
      >
        {selected.map((id) => (
          <span
            key={id}
            className="inline-flex items-center gap-0.5 rounded bg-amber/20 px-1.5 py-0.5 text-[10px] font-medium text-amber"
          >
            {deviceMap.get(id) ?? id}
            <button
              type="button"
              onClick={(e) => {
                e.stopPropagation()
                remove(id)
              }}
              className="ml-0.5 text-amber/60 hover:text-amber"
            >
              ×
            </button>
          </span>
        ))}
        <input
          ref={inputRef}
          type="text"
          value={filter}
          onChange={(e) => {
            setFilter(e.target.value)
            if (!open) setOpen(true)
          }}
          onFocus={() => setOpen(true)}
          placeholder={selected.length === 0 ? 'Select devices...' : ''}
          className="min-w-[60px] flex-1 bg-transparent text-xs text-light outline-none placeholder:text-fog/50"
        />
      </div>

      {/* Dropdown portal */}
      {open &&
        createPortal(
          <div
            ref={dropdownRef}
            className="fixed z-[9999] max-h-48 overflow-auto rounded-lg border border-stone/40 bg-slate-warm shadow-xl"
            style={{ top: dropdownPos.top, left: dropdownPos.left, width: dropdownPos.width }}
          >
            {filtered.length === 0 ? (
              <div className="px-3 py-2 text-xs text-fog">
                {devices.length === 0 ? 'No devices loaded' : 'No matches'}
              </div>
            ) : (
              filtered.slice(0, 50).map((d) => (
                <button
                  key={d.entityId}
                  type="button"
                  className="flex w-full items-center gap-2 px-3 py-1.5 text-left text-xs text-light hover:bg-ash/60"
                  onClick={() => add(d.entityId)}
                >
                  <span className="truncate">{d.friendlyName}</span>
                  <span className="ml-auto truncate text-[10px] text-fog">{d.entityId}</span>
                </button>
              ))
            )}
          </div>,
          document.body
        )}
    </div>
  )
}
