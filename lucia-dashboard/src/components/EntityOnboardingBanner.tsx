import { useState } from 'react'
import { X, ShieldOff, Wand2, Sparkles } from 'lucide-react'

interface EntityOnboardingBannerProps {
  hasFilters: boolean
  entityCount: number
  onStartClean: () => void
  onSmartAssignPreview: () => void
}

export default function EntityOnboardingBanner({ hasFilters, entityCount, onStartClean, onSmartAssignPreview }: EntityOnboardingBannerProps) {
  const [dismissed, setDismissed] = useState(() => localStorage.getItem('entity-onboarding-dismissed') === 'true')

  if (hasFilters || entityCount === 0 || dismissed) return null

  function dismiss() {
    localStorage.setItem('entity-onboarding-dismissed', 'true')
    setDismissed(true)
  }

  return (
    <div className="relative rounded-xl border border-amber/30 bg-amber/5 p-5">
      <button onClick={dismiss} className="absolute right-3 top-3 rounded-lg p-1 text-dust hover:bg-stone/40 hover:text-fog" title="Dismiss">
        <X className="h-4 w-4" />
      </button>

      <div className="mb-4">
        <h3 className="flex items-center gap-2 text-lg font-semibold text-light">
          <Sparkles className="h-5 w-5 text-amber" />
          Set up entity visibility
        </h3>
        <p className="mt-1 text-sm text-dust">
          {entityCount} entities imported from Home Assistant. Choose how to assign them to your agents.
        </p>
      </div>

      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
        <button
          onClick={onStartClean}
          className="group rounded-xl border border-stone bg-basalt p-4 text-left transition-all hover:border-rose/40 hover:bg-rose/5"
        >
          <div className="flex items-center gap-2 text-sm font-semibold text-rose">
            <ShieldOff className="h-4 w-4" />
            Start Clean
          </div>
          <p className="mt-1.5 text-xs text-dust">
            Hide all entities from agents. You choose exactly which devices each agent can see.
          </p>
        </button>

        <button
          onClick={onSmartAssignPreview}
          className="group rounded-xl border border-stone bg-basalt p-4 text-left transition-all hover:border-sky-400/40 hover:bg-sky-400/5"
        >
          <div className="flex items-center gap-2 text-sm font-semibold text-sky-400">
            <Wand2 className="h-4 w-4" />
            Smart Assign
          </div>
          <p className="mt-1.5 text-xs text-dust">
            Auto-assign devices using pattern matching. Network gear, sensors, and infrastructure are filtered out.
          </p>
        </button>
      </div>
    </div>
  )
}
