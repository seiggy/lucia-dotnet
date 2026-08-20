import { Monitor, Moon, Sun } from 'lucide-react'
import type { LucideIcon } from 'lucide-react'
import { useTheme } from './ThemeContext'
import type { ThemePreference } from './ThemeContext'

interface ThemeOption {
  value: ThemePreference
  label: string
  description: string
  icon: LucideIcon
}

interface ThemeSelectorProps {
  className?: string
}

const OPTIONS: ThemeOption[] = [
  { value: 'system', label: 'System', description: 'Use system theme', icon: Monitor },
  { value: 'light', label: 'Light', description: 'Use light theme', icon: Sun },
  { value: 'dark', label: 'Dark', description: 'Use dark theme', icon: Moon },
]

export function ThemeSelector({ className = '' }: ThemeSelectorProps) {
  const { preference, setPreference } = useTheme()

  return (
    <div
      className={`grid grid-cols-3 gap-1 rounded-lg border border-stone/60 bg-basalt/80 p-1 shadow-sm ${className}`}
      role="group"
      aria-label="Theme"
    >
      {OPTIONS.map(({ value, label, description, icon: Icon }) => {
        const isSelected = preference === value
        return (
          <button
            key={value}
            type="button"
            title={description}
            aria-label={description}
            aria-pressed={isSelected}
            onClick={() => setPreference(value)}
            className={`flex min-w-0 flex-col items-center gap-1 rounded-md px-2 py-1.5 text-[10px] font-medium transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-amber/60 ${
              isSelected
                ? 'bg-obsidian text-amber shadow-sm'
                : 'text-dust hover:bg-stone/40 hover:text-light'
            }`}
          >
            <Icon className="h-3.5 w-3.5" aria-hidden="true" />
            <span>{label}</span>
          </button>
        )
      })}
    </div>
  )
}