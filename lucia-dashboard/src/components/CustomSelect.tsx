import { useState, useRef, useEffect } from 'react';
import { ChevronDown } from 'lucide-react';

interface SelectOption {
  value: string;
  label: string;
}

interface CustomSelectProps {
  options: SelectOption[];
  value: string;
  onChange: (value: string) => void;
  placeholder?: string;
  className?: string;
  size?: 'sm' | 'md';
}

export default function CustomSelect({ options, value, onChange, placeholder, className = '', size = 'md' }: CustomSelectProps) {
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

  const selected = options.find((o) => o.value === value);

  useEffect(() => {
    function handleClick(e: MouseEvent) {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false);
    }
    document.addEventListener('mousedown', handleClick);
    return () => document.removeEventListener('mousedown', handleClick);
  }, []);

  const pad = size === 'sm' ? 'px-2 py-1 text-xs' : 'px-3 py-2 text-sm';
  const borderRadius = size === 'sm' ? 'rounded' : 'rounded-lg';

  return (
    <div ref={ref} className={`relative ${className}`}>
      <button
        type="button"
        onClick={() => setOpen(!open)}
        className={`flex w-full items-center justify-between border border-stone/40 bg-slate-warm px-3 py-2 text-sm text-light ${borderRadius}`}
      >
        <span className="truncate">{selected?.label ?? placeholder ?? 'Select...'}</span>
        <ChevronDown className={`ml-2 h-4 w-4 shrink-0 text-fog transition-transform ${open ? 'rotate-180' : ''}`} />
      </button>

      {open && (
        <ul className={`absolute z-50 mt-1 max-h-60 w-full overflow-auto ${borderRadius} border border-stone/40 bg-basalt shadow-lg shadow-black/60`}>
          {options.map((o) => (
            <li
              key={o.value}
              onClick={() => { onChange(o.value); setOpen(false); }}
              className={`cursor-pointer ${pad} transition-colors hover:bg-stone/50 ${
                o.value === value ? 'bg-stone/30 text-amber' : 'text-light'
              }`}
            >
              {o.label}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
