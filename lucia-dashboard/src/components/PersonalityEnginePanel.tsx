import { useState, useEffect, useCallback } from 'react'
import {
  fetchConfigSection,
  updateConfigSection,
  fetchModelProviders,
} from '../api'
import type { ModelProvider } from '../types'
import ToggleSwitch from './ToggleSwitch'
import CustomSelect from './CustomSelect'
import { Save, Sparkles } from 'lucide-react'

const SECTION = 'PersonalityPrompt'

interface PersonalityConfig {
  usePersonalityResponses: boolean
  instructions: string
  modelConnectionName: string
  supportVoiceTags: boolean
}

function parseConfig(entries: { key: string; value: string | null }[]): PersonalityConfig {
  const map = new Map<string, string>()
  const prefix = `${SECTION}:`
  for (const e of entries) {
    const short = e.key.startsWith(prefix) ? e.key.slice(prefix.length) : e.key
    map.set(short, e.value ?? '')
  }
  return {
    usePersonalityResponses: map.get('UsePersonalityResponses') === 'true',
    instructions: map.get('Instructions') ?? '',
    modelConnectionName: map.get('ModelConnectionName') ?? '',
    supportVoiceTags: map.get('SupportVoiceTags') === 'true',
  }
}

function toPayload(cfg: PersonalityConfig): Record<string, string | null> {
  return {
    UsePersonalityResponses: cfg.usePersonalityResponses ? 'true' : 'false',
    Instructions: cfg.instructions || null,
    ModelConnectionName: cfg.modelConnectionName || null,
    SupportVoiceTags: cfg.supportVoiceTags ? 'true' : 'false',
  }
}

export default function PersonalityEnginePanel({
  onToast,
}: {
  onToast: (message: string, type: 'success' | 'error') => void
}) {
  const [config, setConfig] = useState<PersonalityConfig>({
    usePersonalityResponses: false,
    instructions: '',
    modelConnectionName: '',
    supportVoiceTags: false,
  })
  const [original, setOriginal] = useState<PersonalityConfig>(config)
  const [providers, setProviders] = useState<ModelProvider[]>([])
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    let cancelled = false
    Promise.all([
      fetchConfigSection(SECTION).catch(() => []),
      fetchModelProviders('Chat').catch(() => []),
    ]).then(([entries, models]) => {
      if (cancelled) return
      const parsed = parseConfig(entries)
      setConfig(parsed)
      setOriginal(parsed)
      setProviders(models.filter((m) => m.enabled))
      setLoading(false)
    })
    return () => { cancelled = true }
  }, [])

  const isDirty = useCallback(() => {
    return (
      config.usePersonalityResponses !== original.usePersonalityResponses ||
      config.instructions !== original.instructions ||
      config.modelConnectionName !== original.modelConnectionName ||
      config.supportVoiceTags !== original.supportVoiceTags
    )
  }, [config, original])

  const handleSave = useCallback(async () => {
    setSaving(true)
    try {
      const payload = toPayload(config)
      await updateConfigSection(SECTION, payload)
      setOriginal(config)
      onToast('Personality settings saved', 'success')
    } catch (err) {
      onToast(
        `Save failed: ${err instanceof Error ? err.message : 'Unknown error'}`,
        'error',
      )
    } finally {
      setSaving(false)
    }
  }, [config, onToast])

  if (loading) {
    return (
      <div className="rounded-xl border border-stone bg-charcoal p-6">
        <div className="flex items-center gap-3 text-sm text-dust">
          <svg className="h-4 w-4 animate-spin text-amber" viewBox="0 0 24 24" fill="none">
            <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
            <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z" />
          </svg>
          Loading personality settings…
        </div>
      </div>
    )
  }

  return (
    <div className="rounded-xl border border-stone bg-charcoal overflow-hidden">
      {/* Section Header */}
      <div className="flex items-center gap-3 border-b border-stone bg-basalt/60 px-5 py-4">
        <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-amber-glow/20 text-amber">
          <Sparkles className="h-4 w-4" />
        </div>
        <div className="flex-1">
          <h2 className="text-sm font-semibold text-light">Personality Response Engine</h2>
          <p className="text-xs text-dust">
            When enabled, fast-path command responses are passed through your personality prompt via
            LLM instead of using canned templates. This adds latency but gives more natural,
            personality-consistent responses.
          </p>
        </div>
      </div>

      <div className="space-y-5 p-5">
        {/* Use Personality Responses toggle */}
        <div className="flex items-center justify-between gap-4">
          <div>
            <label className="block text-sm font-medium text-light">
              Use Personality Responses
            </label>
            <p className="mt-0.5 text-xs text-dust">
              Replace canned template responses with LLM-generated personality responses
            </p>
          </div>
          <ToggleSwitch
            checked={config.usePersonalityResponses}
            onChange={(val) => setConfig((c) => ({ ...c, usePersonalityResponses: val }))}
            label="Use Personality Responses"
          />
        </div>

        {/* Personality Prompt textarea */}
        <div>
          <label className="block text-sm font-medium text-light mb-1.5">
            Personality Prompt
          </label>
          <p className="text-xs text-dust mb-2">
            System prompt that shapes the assistant&apos;s personality and communication style
          </p>
          <textarea
            value={config.instructions}
            onChange={(e) => setConfig((c) => ({ ...c, instructions: e.target.value }))}
            placeholder="e.g. You are a friendly, witty smart home assistant named Lucia. Respond naturally and keep it brief."
            rows={5}
            disabled={!config.usePersonalityResponses}
            className={`w-full rounded-xl border border-stone bg-basalt px-3 py-2 text-sm text-light placeholder-dust/60 input-focus focus:ring-1 focus:ring-amber resize-y ${
              !config.usePersonalityResponses ? 'opacity-50 cursor-not-allowed' : ''
            }`}
          />
        </div>

        {/* Model Connection dropdown */}
        <div>
          <label className="block text-sm font-medium text-light mb-1.5">
            Model Connection
          </label>
          <p className="text-xs text-dust mb-2">
            Which LLM model to use for personality generation
          </p>
          <CustomSelect
            options={[
              { value: '', label: '— Use orchestrator default —' },
              ...providers.map((p) => ({
                value: p.id,
                label: `${p.name} (${p.providerType} · ${p.modelName})`,
              })),
            ]}
            value={config.modelConnectionName}
            onChange={(val) => setConfig((c) => ({ ...c, modelConnectionName: val }))}
            className="w-full"
          />
        </div>

        {/* Support Voice Tags toggle */}
        <div className="flex items-center justify-between gap-4">
          <div>
            <label className="block text-sm font-medium text-light">
              Support Voice Tags
            </label>
            <p className="mt-0.5 text-xs text-dust">
              Enable SSML / voice tagging in personality response output for speech synthesis
            </p>
          </div>
          <ToggleSwitch
            checked={config.supportVoiceTags}
            onChange={(val) => setConfig((c) => ({ ...c, supportVoiceTags: val }))}
            label="Support Voice Tags"
            disabled={!config.usePersonalityResponses}
          />
        </div>

        {/* Save button */}
        <div className="flex items-center justify-between pt-2 border-t border-stone">
          {isDirty() ? (
            <span className="flex items-center gap-2 text-sm text-amber">
              <span className="inline-block h-2 w-2 rounded-full bg-yellow-400" />
              Unsaved changes
            </span>
          ) : (
            <span />
          )}
          <button
            onClick={handleSave}
            disabled={saving || !isDirty()}
            className={`flex items-center gap-2 rounded-xl px-4 py-2 text-sm font-medium transition-colors ${
              saving || !isDirty()
                ? 'bg-stone text-dust cursor-not-allowed'
                : 'bg-amber text-void hover:bg-amber-glow'
            }`}
          >
            <Save className="h-4 w-4" />
            {saving ? 'Saving…' : 'Save Settings'}
          </button>
        </div>
      </div>
    </div>
  )
}
