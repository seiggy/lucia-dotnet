import { useId, useState } from 'react'
import type { FormEvent } from 'react'
import { Loader2, Save, Trash2 } from 'lucide-react'
import Field from '../Field'
import ToggleSwitch from '../ToggleSwitch'
import Notice from './Notice'
import { useConfirm } from '../../hooks/useConfirm'
import { createMember, deleteMember, ROLE_DEFAULTS, ROLE_LABELS, toRequest, updateMember } from '../../family-api'
import type { FamilyMember, FamilyMemberRequest, FamilyRole } from '../../family-api'
import { dangerButton, errorText, input, primaryButton, section, sectionHint, sectionTitle } from './familyStyles'

const ROLES: FamilyRole[] = ['Administrator', 'Adult', 'Child', 'Guest']

interface MemberProfileFormProps {
  /** The member being edited, or undefined to add a new one. */
  member?: FamilyMember
  members: FamilyMember[]
  onSaved: (member: FamilyMember) => void
  onDeleted?: () => void
}

const EMPTY: FamilyMemberRequest = { displayName: '', role: 'Adult', guardianIds: [], isActive: true, useAssistantHarness: false }

/** Profile, role, guardians, and access for a family member. */
export default function MemberProfileForm({ member, members, onSaved, onDeleted }: MemberProfileFormProps) {
  const confirmAction = useConfirm()
  const roleGroup = useId()
  const [draft, setDraft] = useState<FamilyMemberRequest>(member ? toRequest(member) : EMPTY)
  const [busy, setBusy] = useState(false)
  const [notice, setNotice] = useState<{ error: boolean; text: string } | null>(null)

  const guardianOptions = members.filter(candidate =>
    candidate.id !== member?.id
    && candidate.isActive
    && (candidate.role === 'Adult' || candidate.role === 'Administrator'))
  const needsGuardian = draft.role === 'Child' && draft.guardianIds.length === 0
  const nameMissing = !draft.displayName.trim()

  function update(patch: Partial<FamilyMemberRequest>) {
    setDraft(current => ({ ...current, ...patch }))
    setNotice(null)
  }

  function toggleGuardian(id: string, checked: boolean) {
    update({ guardianIds: checked ? [...draft.guardianIds, id] : draft.guardianIds.filter(existing => existing !== id) })
  }

  async function save(event: FormEvent) {
    event.preventDefault()
    if (nameMissing || needsGuardian) return
    setBusy(true)
    setNotice(null)
    try {
      const body = { ...draft, displayName: draft.displayName.trim() }
      const saved = member ? await updateMember(member.id, body) : await createMember(body)
      setNotice({ error: false, text: member ? `Saved ${saved.displayName}.` : `Added ${saved.displayName}.` })
      onSaved(saved)
    } catch (error) {
      setNotice({ error: true, text: errorText(error, 'Could not save this member.') })
    } finally {
      setBusy(false)
    }
  }

  async function remove() {
    if (!member) return
    if (!(await confirmAction({
      title: `Remove ${member.displayName}?`,
      message: 'Their paired devices stop working immediately and their permissions and model settings are deleted. Usage history is kept.',
      confirmLabel: 'Remove member',
    }))) return
    setBusy(true)
    setNotice(null)
    try {
      await deleteMember(member.id)
      onDeleted?.()
    } catch (error) {
      setNotice({ error: true, text: errorText(error, 'Could not remove this member.') })
      setBusy(false)
    }
  }

  return (
    <form onSubmit={event => void save(event)} className="space-y-6" noValidate>
      <div className={section}>
        <div className="grid gap-4 sm:grid-cols-[minmax(0,2fr)_minmax(8rem,1fr)]">
          <Field label="Name" error={nameMissing && draft.displayName.length > 0 ? 'Enter a name.' : undefined}>
            {control => (
              <input {...control} className={input} maxLength={64} autoComplete="off" value={draft.displayName} disabled={busy}
                onChange={event => update({ displayName: event.target.value })} placeholder="Alex" />
            )}
          </Field>
          <Field label="Birth year" optional hint="Helps you pick a role. Lucia does not infer age.">
            {control => (
              <input {...control} className={input} type="number" inputMode="numeric" min={1900} max={new Date().getFullYear()}
                value={draft.birthYear ?? ''} disabled={busy}
                onChange={event => update({ birthYear: event.target.value ? Number(event.target.value) : null })} />
            )}
          </Field>
        </div>
      </div>

      <div className={section}>
        <h3 id={`${roleGroup}-role`} className={sectionTitle}>Role</h3>
        <p className={sectionHint}>The role sets default permissions. You can override any agent or tool on the Permissions tab.</p>
        <div role="radiogroup" aria-labelledby={`${roleGroup}-role`} className="grid gap-2 sm:grid-cols-2">
          {ROLES.map(role => (
            <label key={role}
              className={`flex cursor-pointer gap-3 rounded-xl border p-3 transition-colors has-[:focus-visible]:ring-2 has-[:focus-visible]:ring-amber/60 ${
                draft.role === role ? 'border-amber/60 bg-amber/8' : 'border-stone hover:border-ash'
              }`}>
              <input type="radio" name={roleGroup} value={role} checked={draft.role === role} disabled={busy}
                onChange={() => update({ role })} className="mt-1 h-4 w-4 accent-amber" />
              <span>
                <span className="block text-sm font-medium text-light">{ROLE_LABELS[role]}</span>
                <span className="mt-0.5 block text-xs leading-5 text-fog">{ROLE_DEFAULTS[role]}</span>
              </span>
            </label>
          ))}
        </div>
      </div>

      <div className={section}>
        <h3 id={`${roleGroup}-guardians`} className={sectionTitle}>Guardians</h3>
        <p className={sectionHint}>
          Guardians approve this member's requests that need permission. {draft.role === 'Child' ? 'A child needs at least one.' : 'Optional for this role.'}
        </p>
        {guardianOptions.length === 0 ? (
          <p className="text-sm text-dust">Add an active adult or administrator first. Only they can be guardians.</p>
        ) : (
          <div role="group" aria-labelledby={`${roleGroup}-guardians`} className="flex flex-wrap gap-2">
            {guardianOptions.map(guardian => {
              const checked = draft.guardianIds.includes(guardian.id)
              return (
                <label key={guardian.id}
                  className={`inline-flex min-h-11 cursor-pointer items-center gap-2 rounded-xl border px-3 text-sm transition-colors has-[:focus-visible]:ring-2 has-[:focus-visible]:ring-amber/60 ${
                    checked ? 'border-amber/60 bg-amber/8 text-light' : 'border-stone text-fog hover:border-ash'
                  }`}>
                  <input type="checkbox" className="h-4 w-4 accent-amber" checked={checked} disabled={busy}
                    onChange={event => toggleGuardian(guardian.id, event.target.checked)} />
                  {guardian.displayName}
                </label>
              )
            })}
          </div>
        )}
        {needsGuardian && guardianOptions.length > 0 && <p className="text-sm text-amber">Choose at least one guardian to save a child.</p>}
      </div>

      <div className={section}>
        <h3 className={sectionTitle}>Access</h3>
        <div className="flex items-center justify-between gap-4">
          <div>
            <p className="text-sm font-medium text-light">Active</p>
            <p className="text-xs leading-5 text-fog">Paused members can't sign in, and their requests are not attributed to them.</p>
          </div>
          <ToggleSwitch label="Active" checked={draft.isActive ?? true} disabled={busy} onChange={isActive => update({ isActive })} />
        </div>
        <Field label="Access ends" optional hint="Use for guests. Their devices stop working at this time.">
          {control => (
            <input {...control} type="datetime-local" className={`${input} sm:max-w-xs`} disabled={busy}
              value={toLocalInput(draft.accessExpiresAt)}
              onChange={event => update({ accessExpiresAt: event.target.value ? new Date(event.target.value).toISOString() : null })} />
          )}
        </Field>
        <div className="flex items-center justify-between gap-4">
          <div>
            <p className="text-sm font-medium text-light">Use the new assistant (preview)</p>
            <p className="text-xs leading-5 text-fog">
              Routes this member's open-ended requests to the V2 assistant when the AssistantHarness feature flag is on. Everyone else stays on the current orchestrator.
            </p>
          </div>
          <ToggleSwitch label="Use the new assistant" checked={draft.useAssistantHarness ?? false} disabled={busy}
            onChange={useAssistantHarness => update({ useAssistantHarness })} />
        </div>
      </div>

      <Notice notice={notice} />

      <div className="flex flex-wrap items-center justify-between gap-3 border-t border-stone pt-6">
        <button type="submit" className={primaryButton} disabled={busy || nameMissing || needsGuardian}>
          {busy ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" /> : <Save className="h-4 w-4" aria-hidden="true" />}
          {member ? 'Save changes' : 'Add member'}
        </button>
        {member && (
          <button type="button" className={dangerButton} disabled={busy} onClick={() => void remove()}>
            <Trash2 className="h-4 w-4" aria-hidden="true" /> Remove member
          </button>
        )}
      </div>
    </form>
  )
}

function toLocalInput(value?: string | null) {
  if (!value) return ''
  const date = new Date(value)
  const offset = date.getTimezoneOffset() * 60_000
  return new Date(date.getTime() - offset).toISOString().slice(0, 16)
}
