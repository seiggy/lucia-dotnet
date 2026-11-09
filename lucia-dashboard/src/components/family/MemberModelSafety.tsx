import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Loader2, Save } from 'lucide-react'
import Field from '../Field'
import ToggleSwitch from '../ToggleSwitch'
import Notice from './Notice'
import { fetchModelProviders } from '../../api'
import { saveLlmControls } from '../../family-api'
import type { FamilyMember, MemberLlmControls } from '../../family-api'
import { errorText, input, primaryButton, secondaryButton, section, sectionHint, sectionTitle } from './familyStyles'

interface MemberModelSafetyProps {
  member: FamilyMember
  onSaved: (controls: MemberLlmControls) => void
}

interface Draft {
  allowedProviderIds: string[]
  preferredProviderId: string
  dailyBudgetUsd: string
  monthlyBudgetUsd: string
  systemPrompt: string
  blockedTopics: string
  quietEnabled: boolean
  quietStart: string
  quietEnd: string
  quietTimeZone: string
  blockPromptInjection: boolean
  redactPersonalInfo: boolean
}

const browserTimeZone = Intl.DateTimeFormat().resolvedOptions().timeZone

function toDraft(controls: MemberLlmControls): Draft {
  return {
    allowedProviderIds: controls.allowedProviderIds,
    preferredProviderId: controls.preferredProviderId ?? '',
    dailyBudgetUsd: controls.dailyBudgetUsd?.toString() ?? '',
    monthlyBudgetUsd: controls.monthlyBudgetUsd?.toString() ?? '',
    systemPrompt: controls.systemPrompt ?? '',
    blockedTopics: controls.blockedTopics.join('\n'),
    quietEnabled: Boolean(controls.quietHours),
    quietStart: controls.quietHours?.start.slice(0, 5) ?? '21:00',
    quietEnd: controls.quietHours?.end.slice(0, 5) ?? '07:00',
    quietTimeZone: controls.quietHours?.timeZoneId ?? browserTimeZone,
    blockPromptInjection: controls.guardrails?.blockPromptInjection ?? false,
    redactPersonalInfo: controls.guardrails?.redactPersonalInfo ?? false,
  }
}

function toControls(draft: Draft): MemberLlmControls {
  const money = (value: string) => (value.trim() === '' ? null : Number(value))
  return {
    allowedProviderIds: draft.allowedProviderIds,
    preferredProviderId: draft.preferredProviderId || null,
    dailyBudgetUsd: money(draft.dailyBudgetUsd),
    monthlyBudgetUsd: money(draft.monthlyBudgetUsd),
    systemPrompt: draft.systemPrompt.trim() || null,
    blockedTopics: draft.blockedTopics.split('\n').map(topic => topic.trim()).filter(Boolean),
    quietHours: draft.quietEnabled
      ? { start: `${draft.quietStart}:00`, end: `${draft.quietEnd}:00`, timeZoneId: draft.quietTimeZone.trim() || null }
      : null,
    guardrails: { blockPromptInjection: draft.blockPromptInjection, redactPersonalInfo: draft.redactPersonalInfo },
  }
}

/** Which models answer a member, what they may spend, and the content rules applied to their conversations. */
export default function MemberModelSafety({ member, onSaved }: MemberModelSafetyProps) {
  const providersQuery = useQuery({ queryKey: ['model-providers', 'Chat'], queryFn: () => fetchModelProviders('Chat') })
  const initial = toDraft(member.llmControls)
  const [draft, setDraft] = useState<Draft>(initial)
  const [busy, setBusy] = useState(false)
  const [notice, setNotice] = useState<{ error: boolean; text: string } | null>(null)

  const providers = [...(providersQuery.data ?? [])].sort((left, right) => left.name.localeCompare(right.name))
  const preferredOptions = draft.allowedProviderIds.length > 0
    ? providers.filter(provider => draft.allowedProviderIds.includes(provider.id))
    : providers
  const badMoney = (value: string) => value.trim() !== '' && (!Number.isFinite(Number(value)) || Number(value) < 0)
  const quietSame = draft.quietEnabled && draft.quietStart === draft.quietEnd
  const invalid = badMoney(draft.dailyBudgetUsd) || badMoney(draft.monthlyBudgetUsd) || quietSame
  const dirty = JSON.stringify(draft) !== JSON.stringify(initial)

  function update(patch: Partial<Draft>) {
    setDraft(current => ({ ...current, ...patch }))
    setNotice(null)
  }

  function toggleProvider(id: string, checked: boolean) {
    const allowed = checked ? [...draft.allowedProviderIds, id] : draft.allowedProviderIds.filter(existing => existing !== id)
    const keepPreferred = !draft.preferredProviderId || allowed.length === 0 || allowed.includes(draft.preferredProviderId)
    update({ allowedProviderIds: allowed, preferredProviderId: keepPreferred ? draft.preferredProviderId : '' })
  }

  async function save() {
    if (invalid) return
    setBusy(true)
    setNotice(null)
    try {
      const saved = await saveLlmControls(member.id, toControls(draft))
      setDraft(toDraft(saved))
      onSaved(saved)
      setNotice({ error: false, text: `Model and safety settings saved for ${member.displayName}.` })
    } catch (error) {
      setNotice({ error: true, text: errorText(error, 'Could not save model and safety settings.') })
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="space-y-6">
      <div className={section}>
        <h3 id={`models-${member.id}`} className={sectionTitle}>Models</h3>
        <p className={sectionHint}>
          Limit {member.displayName} to specific providers, for example local models only. Calls never fall back to a provider outside this list; if none is reachable the request fails.
        </p>
        {providersQuery.isLoading ? (
          <div role="status" aria-label="Loading providers" className="h-24 animate-pulse rounded-xl bg-basalt/70" />
        ) : providersQuery.isError ? (
          <p role="alert" className="text-sm text-rose">{providersQuery.error.message}</p>
        ) : (
          <>
            <div role="group" aria-labelledby={`models-${member.id}`} className="flex flex-wrap gap-2">
              {providers.map(provider => {
                const checked = draft.allowedProviderIds.includes(provider.id)
                return (
                  <label key={provider.id}
                    className={`inline-flex min-h-11 cursor-pointer items-center gap-2 rounded-xl border px-3 text-sm transition-colors has-[:focus-visible]:ring-2 has-[:focus-visible]:ring-amber/60 ${
                      checked ? 'border-amber/60 bg-amber/8 text-light' : 'border-stone text-fog hover:border-ash'
                    }`}>
                    <input type="checkbox" className="h-4 w-4 accent-amber" checked={checked} disabled={busy}
                      onChange={event => toggleProvider(provider.id, event.target.checked)} />
                    {provider.name}
                    <span className="text-xs text-dust">{provider.providerType}</span>
                  </label>
                )
              })}
            </div>
            <p className="text-xs text-dust">
              {draft.allowedProviderIds.length === 0 ? 'No providers selected: every enabled provider is allowed.' : `${draft.allowedProviderIds.length} allowed.`}
            </p>
            <Field label="Preferred provider" optional hint="Answers this member's requests instead of each agent's own model.">
              {control => (
                <select {...control} className={`${input} sm:max-w-sm`} value={draft.preferredProviderId} disabled={busy}
                  onChange={event => update({ preferredProviderId: event.target.value })}>
                  <option value="">Each agent's own model</option>
                  {preferredOptions.map(provider => <option key={provider.id} value={provider.id}>{provider.name}</option>)}
                </select>
              )}
            </Field>
          </>
        )}
      </div>

      <div className={section}>
        <h3 className={sectionTitle}>Spending</h3>
        <p className={sectionHint}>
          Estimated from each provider's token prices, set on the Providers page. New requests are refused once a limit is reached; one request can run slightly over.
        </p>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label="Daily limit (USD)" optional error={badMoney(draft.dailyBudgetUsd) ? 'Enter zero or a positive amount.' : undefined}>
            {control => (
              <input {...control} className={input} type="number" min={0} step="0.01" inputMode="decimal" placeholder="No limit"
                value={draft.dailyBudgetUsd} disabled={busy} onChange={event => update({ dailyBudgetUsd: event.target.value })} />
            )}
          </Field>
          <Field label="Monthly limit (USD)" optional error={badMoney(draft.monthlyBudgetUsd) ? 'Enter zero or a positive amount.' : undefined}>
            {control => (
              <input {...control} className={input} type="number" min={0} step="0.01" inputMode="decimal" placeholder="No limit"
                value={draft.monthlyBudgetUsd} disabled={busy} onChange={event => update({ monthlyBudgetUsd: event.target.value })} />
            )}
          </Field>
        </div>
      </div>

      <div className={section}>
        <h3 className={sectionTitle}>Guidance and topics</h3>
        <Field label="Extra instructions" optional hint="Added to every agent's instructions for this member, for example age-appropriate language.">
          {control => (
            <textarea {...control} className={`${input} min-h-28`} rows={4} maxLength={4000} value={draft.systemPrompt} disabled={busy}
              placeholder="Use simple words and keep answers short." onChange={event => update({ systemPrompt: event.target.value })} />
          )}
        </Field>
        <Field label="Blocked topics" optional hint="One per line. Requests and replies that mention a topic are refused without repeating it.">
          {control => (
            <textarea {...control} className={`${input} min-h-28`} rows={4} value={draft.blockedTopics} disabled={busy}
              placeholder={'horror movies\ngambling'} onChange={event => update({ blockedTopics: event.target.value })} />
          )}
        </Field>
      </div>

      <div className={section}>
        <div className="flex items-center justify-between gap-4">
          <div>
            <h3 className={sectionTitle}>Quiet hours</h3>
            <p className="mt-1 text-sm text-fog">Lucia declines {member.displayName}'s requests during this window.</p>
          </div>
          <ToggleSwitch label="Quiet hours" checked={draft.quietEnabled} disabled={busy} onChange={quietEnabled => update({ quietEnabled })} />
        </div>
        {draft.quietEnabled && (
          <div className="grid gap-4 sm:grid-cols-3">
            <Field label="Starts">
              {control => <input {...control} type="time" className={input} value={draft.quietStart} disabled={busy} onChange={event => update({ quietStart: event.target.value })} />}
            </Field>
            <Field label="Ends" error={quietSame ? 'Pick a different end time.' : undefined}>
              {control => <input {...control} type="time" className={input} value={draft.quietEnd} disabled={busy} onChange={event => update({ quietEnd: event.target.value })} />}
            </Field>
            <Field label="Time zone" hint="IANA name, for example America/New_York.">
              {control => <input {...control} className={input} value={draft.quietTimeZone} disabled={busy} onChange={event => update({ quietTimeZone: event.target.value })} />}
            </Field>
          </div>
        )}
      </div>

      <div className={section}>
        <h3 className={sectionTitle}>Guardrails</h3>
        <p className={sectionHint}>Deterministic AgentEval checks on this member's conversations. They add no model calls.</p>
        <div className="flex items-center justify-between gap-4">
          <div>
            <p className="text-sm font-medium text-light">Refuse prompt injection</p>
            <p className="text-xs leading-5 text-fog">Declines requests that contain known instruction-override markers.</p>
          </div>
          <ToggleSwitch label="Refuse prompt injection" checked={draft.blockPromptInjection} disabled={busy}
            onChange={blockPromptInjection => update({ blockPromptInjection })} />
        </div>
        <div className="flex items-center justify-between gap-4">
          <div>
            <p className="text-sm font-medium text-light">Hide personal details in replies</p>
            <p className="text-xs leading-5 text-fog">Masks email addresses, phone numbers, and similar data in Lucia's answers.</p>
          </div>
          <ToggleSwitch label="Hide personal details in replies" checked={draft.redactPersonalInfo} disabled={busy}
            onChange={redactPersonalInfo => update({ redactPersonalInfo })} />
        </div>
      </div>

      <Notice notice={notice} />

      <div className="flex flex-wrap items-center gap-3 border-t border-stone pt-6">
        <button type="button" className={primaryButton} disabled={busy || !dirty || invalid} onClick={() => void save()}>
          {busy ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" /> : <Save className="h-4 w-4" aria-hidden="true" />}
          Save settings
        </button>
        {dirty && !busy && (
          <button type="button" className={secondaryButton} onClick={() => { setDraft(initial); setNotice(null) }}>Discard changes</button>
        )}
      </div>
    </div>
  )
}
