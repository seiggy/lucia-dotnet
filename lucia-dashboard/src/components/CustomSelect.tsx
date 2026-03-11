import { useState, useRef, useEffect, useLayoutEffect, useCallback } from 'react';
import { createPortal } from 'react-dom';
import { ChevronDown } from 'lucide-react';

/** A single option in the dropdown. */
interface SelectOption {
  value: string;
  label: string;
}

/** Props for the {@link CustomSelect} component. */
interface CustomSelectProps {
  options: SelectOption[];
  value: string;
  onChange: (value: string) => void;
  placeholder?: string;
  className?: string;
  size?: 'sm' | 'md';
  label?: string;
}

/**
 * A styled dropdown select component with search filtering.
 * Renders the dropdown in a portal to avoid clipping by overflow containers.
 */
export default function CustomSelect({ options, value, onChange, placeholder, className = '', size = 'md', label }: CustomSelectProps) {
  const [open, setOpen] = useState(false);
  const [filter, setFilter] = useState('');
  const buttonRef = useRef<HTMLButtonElement>(null);
  const dropdownRef = useRef<HTMLUListElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const highlightRef = useRef<number>(-1);
  const [highlight, setHighlight] = useState(-1);
  const [pos, setPos] = useState<{ top: number; left: number; width: number; direction: 'down' | 'up' }>({
    top: 0, left: 0, width: 0, direction: 'down',
  });
  const listboxId = useRef(`cs-listbox-${Math.random().toString(36).slice(2, 8)}`).current;

  const selected = options.find((o) => o.value === value);

  const filtered = filter
    ? options.filter((o) => o.label.toLowerCase().includes(filter.toLowerCase()))
    : options;

  useEffect(() => {
    highlightRef.current = highlight;
  }, [highlight]);

  // Focus the search input after portal renders
  useEffect(() => {
    if (!open) return;
    requestAnimationFrame(() => inputRef.current?.focus());
  }, [open]);

  // Close when clicking outside
  useEffect(() => {
    if (!open) return;
    function handleClick(e: MouseEvent) {
      const target = e.target as Node;
      if (buttonRef.current?.contains(target) || dropdownRef.current?.contains(target)) return;
      setOpen(false);
    }
    document.addEventListener('mousedown', handleClick);
    return () => document.removeEventListener('mousedown', handleClick);
  }, [open]);

  // Reposition dropdown on open and on scroll/resize
  const reposition = useCallback(() => {
    if (!buttonRef.current) return;
    const rect = buttonRef.current.getBoundingClientRect();
    const spaceBelow = window.innerHeight - rect.bottom;
    const spaceAbove = rect.top;
    const maxH = 240;
    const openUp = spaceBelow < maxH && spaceAbove > spaceBelow;
    setPos({
      top: openUp ? rect.top - 4 : rect.bottom + 4,
      left: rect.left,
      width: rect.width,
      direction: openUp ? 'up' : 'down',
    });
  }, []);

  useLayoutEffect(() => {
    if (open) reposition();
  }, [open, reposition]);

  useEffect(() => {
    if (!open) return;
    window.addEventListener('scroll', reposition, true);
    window.addEventListener('resize', reposition);
    return () => {
      window.removeEventListener('scroll', reposition, true);
      window.removeEventListener('resize', reposition);
    };
  }, [open, reposition]);

  // Scroll highlighted item into view
  useEffect(() => {
    if (highlight < 0 || !dropdownRef.current) return;
    const items = dropdownRef.current.querySelectorAll('[data-option]');
    items[highlight]?.scrollIntoView({ block: 'nearest' });
  }, [highlight]);

  const openDropdown = useCallback(() => {
    setFilter('');
    setHighlight(-1);
    setOpen(true);
  }, []);

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'ArrowDown') {
      e.preventDefault();
      setHighlight((h) => Math.min(h + 1, filtered.length - 1));
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      setHighlight((h) => Math.max(h - 1, 0));
    } else if (e.key === 'Enter') {
      e.preventDefault();
      const idx = highlightRef.current;
      if (idx >= 0 && idx < filtered.length) {
        onChange(filtered[idx].value);
        setOpen(false);
      }
    } else if (e.key === 'Escape') {
      setOpen(false);
      buttonRef.current?.focus();
    }
  };

  const pad = size === 'sm' ? 'px-2 py-1 text-xs' : 'px-3 py-2 text-sm';
  const borderRadius = size === 'sm' ? 'rounded' : 'rounded-lg';

  return (
    <div className={`relative ${className}`}>
      <button
        ref={buttonRef}
        type="button"
        role="combobox"
        aria-haspopup="listbox"
        aria-expanded={open}
        aria-controls={listboxId}
        aria-label={label ?? placeholder ?? 'Select an option'}
        onClick={() => {
          if (open) {
            setOpen(false);
          } else {
            openDropdown();
          }
        }}
        onKeyDown={(e) => {
          if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
            e.preventDefault();
            openDropdown();
          }
        }}
        className={`flex w-full items-center justify-between border border-stone/40 bg-slate-warm px-3 py-2 text-sm text-light ${borderRadius}`}
      >
        <span className="truncate">{selected?.label ?? placeholder ?? 'Select...'}</span>
        <ChevronDown className={`ml-2 h-4 w-4 shrink-0 text-fog transition-transform ${open ? 'rotate-180' : ''}`} />
      </button>

      {open && createPortal(
        <ul
          ref={dropdownRef}
          id={listboxId}
          role="listbox"
          aria-label={label ?? placeholder ?? 'Options'}
          className={`fixed z-[9999] max-h-60 overflow-auto ${borderRadius} border border-stone/40 bg-basalt shadow-lg shadow-black/60`}
          style={{
            top: pos.direction === 'down' ? pos.top : undefined,
            bottom: pos.direction === 'up' ? window.innerHeight - pos.top : undefined,
            left: pos.left,
            width: pos.width,
          }}
        >
          {/* Typeahead search input */}
          <li className="sticky top-0 bg-basalt p-1">
            <input
              ref={inputRef}
              type="text"
              value={filter}
              onChange={(e) => { setFilter(e.target.value); setHighlight(0); }}
              onKeyDown={handleKeyDown}
              aria-label="Filter options"
              className={`w-full rounded border border-stone/30 bg-slate-warm px-2 py-1 text-light placeholder:text-fog/50 outline-none focus:border-amber/50 ${size === 'sm' ? 'text-xs' : 'text-sm'}`}
              placeholder="Type to filter..."
            />
          </li>
          {filtered.length === 0 ? (
            <li className={`${pad} text-fog/60`}>No matches</li>
          ) : (
            filtered.map((o, idx) => (
              <li
                key={o.value}
                role="option"
                aria-selected={o.value === value}
                data-option
                onClick={() => { onChange(o.value); setOpen(false); }}
                className={`cursor-pointer ${pad} transition-colors ${
                  idx === highlight ? 'bg-stone/50' : 'hover:bg-stone/30'
                } ${o.value === value ? 'text-amber' : 'text-light'}`}
              >
                {o.label}
              </li>
            ))
          )}
        </ul>,
        document.body,
      )}
    </div>
  );
}
