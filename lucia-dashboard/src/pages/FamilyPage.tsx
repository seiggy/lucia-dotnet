import { useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Activity, Cpu, ShieldCheck, Smartphone, UserPlus, UserRound } from 'lucide-react'
import Tabs from '../components/Tabs'
import { tabPanelProps } from '../components/tabPanelProps'
import MemberProfileForm from '../components/family/MemberProfileForm'
import MemberPermissions from '../components/family/MemberPermissions'
import MemberModelSafety from '../components/family/MemberModelSafety'
import MemberDevices from '../components/family/MemberDevices'
import MemberUsagePanel from '../components/family/MemberUsagePanel'
import OidcSettingsPanel from '../components/family/OidcSettingsPanel'
import { secondaryButton } from '../components/family/familyStyles'
import { listMembers, ROLE_LABELS } from '../family-api'
import type { FamilyMember } from '../family-api'

type View = 'members' | 'sign-in'
type MemberTab = 'profile' | 'permissions' | 'model' | 'devices' | 'usage'

const MEMBER_TABS = [
  { id: 'profile' as const, label: 'Profile', icon: <UserRound className="h-4 w-4" aria-hidden="true" /> },
  { id: 'permissions' as const, label: 'Permissions', icon: <ShieldCheck className="h-4 w-4" aria-hidden="true" /> },
  { id: 'model' as const, label: 'Model & safety', icon: <Cpu className="h-4 w-4" aria-hidden="true" /> },
  { id: 'devices' as const, label: 'Devices & sign-in', icon: <Smartphone className="h-4 w-4" aria-hidden="true" /> },
  { id: 'usage' as const, label: 'Usage', icon: <Activity className="h-4 w-4" aria-hidden="true" /> },
]

const NEW = 'new'

/** Family members: who can use the Lucia apps, what each may do, and which models answer them. */
export default function FamilyPage() {
  const queryClient = useQueryClient()
  const [parameters, setParameters] = useSearchParams()
  const [view, setView] = useState<View>('members')
  const [tab, setTab] = useState<MemberTab>('profile')
  const membersQuery = useQuery({ queryKey: ['family-members'], queryFn: ({ signal }) => listMembers(signal) })

  const members = [...(membersQuery.data ?? [])].sort((left, right) => left.displayName.localeCompare(right.displayName))
  const requested = parameters.get('member')
  const selected = requested === NEW ? undefined : members.find(member => member.id === requested) ?? members[0]
  const adding = requested === NEW || (!membersQuery.isLoading && members.length === 0)

  function select(id: string) {
    setParameters({ member: id })
    setTab('profile')
  }

  function replaceMember(updated: FamilyMember) {
    queryClient.setQueryData<FamilyMember[]>(['family-members'], current =>
      current?.some(member => member.id === updated.id)
        ? current.map(member => (member.id === updated.id ? updated : member))
        : [...(current ?? []), updated])
  }

  return (
    <div className="mx-auto max-w-6xl space-y-6">
      <header>
        <h1 className="font-display text-2xl font-semibold text-light">Family</h1>
        <p className="mt-2 max-w-[70ch] text-sm leading-6 text-fog">
          Decide who can use the Lucia apps, what each person may ask Lucia to do, and which models answer them.
          Family members use their own apps; this portal stays for administration.
        </p>
      </header>

      <Tabs label="Family settings" idPrefix="family" value={view} onChange={setView}
        tabs={[{ id: 'members', label: 'Members' }, { id: 'sign-in', label: 'App sign-in' }]} />

      {view === 'sign-in' ? (
        <section {...tabPanelProps('family', 'sign-in')} className="focus-visible:outline-none">
          <OidcSettingsPanel />
        </section>
      ) : (
        <section {...tabPanelProps('family', 'members')} className="grid gap-6 focus-visible:outline-none lg:grid-cols-[16rem_minmax(0,1fr)]">
          <nav aria-label="Family members" className="space-y-3">
            <button type="button" className={`${secondaryButton} w-full`} onClick={() => setParameters({ member: NEW })}>
              <UserPlus className="h-4 w-4" aria-hidden="true" /> Add member
            </button>
            {membersQuery.isLoading ? (
              <div role="status" aria-label="Loading family members" className="h-40 animate-pulse rounded-xl bg-basalt/70" />
            ) : membersQuery.isError ? (
              <div role="alert" className="text-sm text-rose">
                <p>{membersQuery.error.message}</p>
                <button type="button" className={`mt-3 ${secondaryButton}`} onClick={() => void membersQuery.refetch()}>Try again</button>
              </div>
            ) : members.length > 0 && (
              <ul className="space-y-0.5">
                {members.map(member => {
                  const current = !adding && member.id === selected?.id
                  const ended = member.accessExpiresAt != null && new Date(member.accessExpiresAt) <= new Date()
                  return (
                    <li key={member.id}>
                      <button type="button" aria-current={current ? 'true' : undefined} onClick={() => select(member.id)}
                        className={`relative flex w-full flex-col rounded-lg px-3 py-2.5 text-left transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-amber/60 ${
                          current ? 'nav-active' : 'text-fog hover:bg-stone/40 hover:text-cloud'
                        }`}>
                        <span className="truncate text-sm font-medium">{member.displayName}</span>
                        <span className="text-xs text-dust">
                          {ROLE_LABELS[member.role]}
                          {!member.isActive ? ' · Paused' : ended ? ' · Access ended' : ''}
                        </span>
                      </button>
                    </li>
                  )
                })}
              </ul>
            )}
          </nav>

          <div className="min-w-0 rounded-xl border border-stone bg-charcoal p-5 sm:p-6">
            {membersQuery.isLoading ? (
              <div role="status" aria-label="Loading member" className="h-64 animate-pulse rounded-xl bg-basalt/70" />
            ) : adding ? (
              <div className="space-y-6">
                <div>
                  <h2 className="font-display text-xl font-semibold text-light">{members.length === 0 ? 'Add your first family member' : 'Add a family member'}</h2>
                  <p className="mt-1 max-w-[70ch] text-sm leading-6 text-fog">
                    {members.length === 0
                      ? 'Start with yourself as an administrator, then add adults before children so you can choose their guardians.'
                      : 'After saving, pair their phone or link their voice on the Devices & sign-in tab.'}
                  </p>
                </div>
                <MemberProfileForm key={NEW} members={members} onSaved={member => { replaceMember(member); select(member.id) }} />
              </div>
            ) : selected && (
              <div className="space-y-6">
                <div>
                  <h2 className="break-words font-display text-xl font-semibold text-light">{selected.displayName}</h2>
                  <p className="mt-1 text-sm text-fog">
                    {ROLE_LABELS[selected.role]}
                    {selected.guardianIds.length > 0 && ` · Guardians: ${selected.guardianIds
                      .map(id => members.find(member => member.id === id)?.displayName ?? 'Removed member')
                      .join(', ')}`}
                  </p>
                </div>
                <Tabs label={`${selected.displayName} settings`} idPrefix="member" value={tab} onChange={setTab} tabs={MEMBER_TABS} />
                <div {...tabPanelProps('member', tab)} className="focus-visible:outline-none">
                  {tab === 'profile' && (
                    <MemberProfileForm key={selected.id} member={selected} members={members} onSaved={replaceMember}
                      onDeleted={() => {
                        void queryClient.invalidateQueries({ queryKey: ['family-members'] })
                        setParameters({})
                      }} />
                  )}
                  {tab === 'permissions' && (
                    <MemberPermissions key={selected.id} member={selected}
                      onSaved={capabilityRules => replaceMember({ ...selected, capabilityRules })} />
                  )}
                  {tab === 'model' && (
                    <MemberModelSafety key={selected.id} member={selected}
                      onSaved={llmControls => replaceMember({ ...selected, llmControls })} />
                  )}
                  {tab === 'devices' && <MemberDevices key={selected.id} member={selected} />}
                  {tab === 'usage' && <MemberUsagePanel key={selected.id} member={selected} />}
                </div>
              </div>
            )}
          </div>
        </section>
      )}
    </div>
  )
}
