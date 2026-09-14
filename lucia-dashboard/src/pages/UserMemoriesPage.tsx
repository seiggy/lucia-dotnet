import { useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Check, Loader2, Pencil, RefreshCw, Search, Trash2 } from 'lucide-react'
import { deleteUserMemory, fetchUserMemories, listSpeakerProfiles, updateUserMemory } from '../api'
import type { UserMemoryEntry } from '../api'

const inputStyle = 'min-h-11 w-full rounded-xl border border-stone bg-basalt px-3 py-2 text-base text-light placeholder:text-dust input-focus'
const secondaryButton = 'inline-flex min-h-11 items-center justify-center gap-2 rounded-xl border border-stone bg-basalt px-3 py-2 text-sm font-medium text-fog transition-colors hover:border-amber/40 hover:text-light focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-amber/60 disabled:cursor-not-allowed disabled:opacity-40'
const memoryLabels: Record<string, string> = {
  preferred_name: 'Preferred name',
  preferred_room: 'Preferred room',
  preferences: 'Preferences',
}

interface MemoryEdit extends UserMemoryEntry {
  profileId: string
}

/** Inspect and manage personal memories through stable enrolled-profile identities. */
export default function UserMemoriesPage() {
  const [parameters, setParameters] = useSearchParams()
  const queryClient = useQueryClient()
  const [search, setSearch] = useState('')
  const [editing, setEditing] = useState<MemoryEdit | null>(null)
  const [draft, setDraft] = useState('')
  const [busy, setBusy] = useState(false)
  const [notice, setNotice] = useState<{ error: boolean; text: string } | null>(null)

  const profilesQuery = useQuery({
    queryKey: ['memory-speaker-profiles'],
    queryFn: listSpeakerProfiles,
  })
  const profiles = (profilesQuery.data ?? [])
    .filter(profile => !profile.isProvisional)
    .sort((left, right) => left.name.localeCompare(right.name))
  const requestedProfileId = parameters.get('profile')
  const profile = requestedProfileId
    ? profiles.find(candidate => candidate.id === requestedProfileId)
    : profiles[0]
  const memoriesQuery = useQuery({
    queryKey: ['user-memories', profile?.id, search],
    queryFn: ({ signal }) => {
      if (!profile) throw new Error('Choose an enrolled user.')
      return fetchUserMemories(profile.id, search, signal)
    },
    enabled: Boolean(profile),
    gcTime: 0,
  })
  const memories = memoriesQuery.data ?? []
  const activeEdit = editing?.profileId === profile?.id ? editing : null
  const controlsLocked = busy || activeEdit !== null

  function selectProfile(profileId: string) {
    setParameters({ profile: profileId })
    setSearch('')
    setEditing(null)
    setNotice(null)
  }

  function editMemory(entry: UserMemoryEntry) {
    if (!profile) return
    setEditing({ ...entry, profileId: profile.id })
    setDraft(entry.value)
    setNotice(null)
  }

  async function saveMemory() {
    if (!profile || !activeEdit) return
    if (!draft.trim()) {
      setNotice({ error: true, text: 'Enter a memory value, or cancel and delete the entry.' })
      return
    }
    setBusy(true)
    setNotice(null)
    try {
      await updateUserMemory(activeEdit.profileId, activeEdit, draft)
      await queryClient.invalidateQueries({ queryKey: ['user-memories', profile.id] })
      setEditing(null)
      setNotice({ error: false, text: `Memory saved for ${profile.name}.` })
    } catch (error: unknown) {
      setNotice({ error: true, text: error instanceof Error ? error.message : 'Failed to save this memory.' })
    } finally {
      setBusy(false)
    }
  }

  async function removeMemory(entry: UserMemoryEntry) {
    if (!profile || controlsLocked) return
    const label = memoryLabels[entry.key] ?? entry.key
    if (!window.confirm(`Delete "${label}" for ${profile.name}? This removes only this memory, not the voice profile.`)) return
    setBusy(true)
    setNotice(null)
    try {
      await deleteUserMemory(profile.id, entry.key)
      await queryClient.invalidateQueries({ queryKey: ['user-memories', profile.id] })
      setNotice({ error: false, text: `Memory deleted for ${profile.name}.` })
    } catch (error: unknown) {
      setNotice({ error: true, text: error instanceof Error ? error.message : 'Failed to delete this memory.' })
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="mx-auto max-w-6xl space-y-6">
      <header className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h1 className="font-display text-2xl font-semibold text-light">User memories</h1>
          <p className="mt-2 max-w-[70ch] text-sm leading-6 text-fog">
            Inspect the preferences and facts Lucia uses when it recognizes an enrolled speaker.
            Changes here affect that person's stored memories, not their voice recordings.
          </p>
        </div>
        <Link to="/voice-platform" className={secondaryButton}>Voice profiles</Link>
      </header>

      <div className="grid items-end gap-4 rounded-xl border border-stone bg-basalt/50 p-4 lg:grid-cols-[minmax(12rem,1fr)_minmax(12rem,2fr)_auto]">
        <div>
          <label htmlFor="memory-profile" className="mb-2 block text-sm font-medium text-light">Enrolled user</label>
          <select id="memory-profile" className={inputStyle} value={profile?.id ?? ''} disabled={controlsLocked || profilesQuery.isLoading || profiles.length === 0} onChange={event => selectProfile(event.target.value)}>
            <option value="" disabled>Choose an enrolled user</option>
            {profiles.map(candidate => (
              <option key={candidate.id} value={candidate.id}>{candidate.name} ({candidate.id.slice(0, 8)})</option>
            ))}
          </select>
        </div>
        <div>
          <label htmlFor="memory-search" className="mb-2 block text-sm font-medium text-light">Search memories</label>
          <div className="relative">
            <Search className="pointer-events-none absolute left-3 top-3 h-5 w-5 text-dust" aria-hidden="true" />
            <input id="memory-search" type="search" maxLength={200} className={`${inputStyle} pl-10`} value={search} disabled={controlsLocked || !profile} onChange={event => setSearch(event.target.value)} placeholder="Search keys or values" />
          </div>
        </div>
        <button type="button" className={secondaryButton} disabled={controlsLocked || profilesQuery.isFetching || memoriesQuery.isFetching} onClick={() => {
          void profilesQuery.refetch()
          if (profile) void memoriesQuery.refetch()
        }}>
          <RefreshCw className="h-4 w-4" aria-hidden="true" /> Refresh
        </button>
      </div>

      {profilesQuery.isError && <p role="alert" className="text-sm text-rose">{profilesQuery.error.message}</p>}
      {notice && (
        <p role={notice.error ? 'alert' : 'status'} className={`rounded-xl border px-4 py-3 text-sm ${notice.error ? 'border-rose/30 bg-rose/8 text-rose' : 'border-sage/30 bg-sage/8 text-sage'}`}>
          {notice.text}
        </p>
      )}

      {profilesQuery.isLoading ? (
        <div role="status" aria-label="Loading enrolled users" className="h-40 animate-pulse rounded-xl bg-basalt/70" />
      ) : !profilesQuery.isError && !profile ? (
        <div className="rounded-xl border border-stone p-6">
          <h2 className="font-display text-lg text-light">{profiles.length ? 'That profile is no longer available' : 'No enrolled users yet'}</h2>
          <p className="mt-2 text-sm text-fog">{profiles.length ? 'Choose an enrolled user from the list.' : 'Complete voice enrollment first. The confirmed details and later memories will appear here.'}</p>
          {!profiles.length && <Link to="/voice-platform" className={`mt-4 ${secondaryButton}`}>Open voice enrollment</Link>}
        </div>
      ) : profile && (
        <section aria-label={`Memories for ${profile.name}`} className="space-y-4">
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div className="min-w-0">
              <h2 className="break-words font-display text-xl font-semibold text-light [overflow-wrap:anywhere]">{profile.name}</h2>
              <p className="mt-1 break-all font-mono text-xs text-dust">Profile ID: {profile.id}</p>
              {!profile.isAuthorized && <p className="mt-2 text-sm text-amber">Voice recognition is not authorized for this profile. Its stored memories are still available to manage.</p>}
            </div>
            {!memoriesQuery.isLoading && !memoriesQuery.isError && <p className="text-sm text-dust">{memories.length} {memories.length === 1 ? 'memory' : 'memories'} displayed</p>}
          </div>

          {memoriesQuery.isLoading ? (
            <div role="status" aria-label="Loading memories" className="h-48 animate-pulse rounded-xl bg-basalt/70" />
          ) : memoriesQuery.isError ? (
            <div role="alert" className="rounded-xl border border-rose/30 bg-rose/8 p-4 text-sm text-rose">
              <p>{memoriesQuery.error.message}</p>
              <button type="button" className={`mt-3 ${secondaryButton}`} onClick={() => void memoriesQuery.refetch()}>Try again</button>
            </div>
          ) : memories.length === 0 ? (
            <p className="rounded-xl border border-stone p-6 text-sm text-fog">
              {search.trim() ? 'No memories match this search.' : `No personal memories are saved for ${profile.name}. They can ask Lucia to remember a preference or fact.`}
            </p>
          ) : (
            <ul className="divide-y divide-stone overflow-hidden rounded-xl border border-stone bg-basalt/35">
              {memories.map(entry => {
                const label = memoryLabels[entry.key] ?? entry.key
                const isEditing = activeEdit?.key === entry.key
                return (
                  <li key={entry.key} aria-label={label} className="grid gap-4 p-4 sm:p-5 lg:grid-cols-[minmax(10rem,1fr)_minmax(0,2fr)_auto]">
                    <div className="min-w-0">
                      <h3 className="break-words font-medium text-light">{label}</h3>
                      <p className="mt-1 break-all font-mono text-xs text-dust">{entry.key}</p>
                      <p className="mt-3 text-xs leading-5 text-dust">Saved {formatDate(entry.createdAt)}</p>
                      <p className="text-xs leading-5 text-dust">{entry.expiresAt ? `Expires ${formatDate(entry.expiresAt)}` : 'No expiration'}</p>
                    </div>
                    {isEditing ? (
                      <div className="min-w-0 space-y-3 lg:col-span-2">
                        <label htmlFor="memory-value" className="block text-sm font-medium text-light">Edit {label}</label>
                        <textarea id="memory-value" className={`${inputStyle} min-h-32`} rows={4} value={draft} disabled={busy} onChange={event => setDraft(event.target.value)} autoFocus />
                        <div className="flex flex-wrap gap-2">
                          <button type="button" disabled={busy || !draft.trim()} onClick={() => void saveMemory()} className="inline-flex min-h-11 items-center justify-center gap-2 rounded-xl bg-amber px-4 py-2 text-sm font-semibold text-on-accent hover:bg-amber-glow focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-amber/60 disabled:cursor-not-allowed disabled:opacity-40">
                            {busy ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" /> : <Check className="h-4 w-4" aria-hidden="true" />} Save changes
                          </button>
                          <button type="button" className={secondaryButton} disabled={busy} onClick={() => { setEditing(null); setNotice(null) }}>Cancel</button>
                        </div>
                      </div>
                    ) : (
                      <>
                        <p className="min-w-0 whitespace-pre-wrap break-words text-sm leading-6 text-cloud [overflow-wrap:anywhere]">{entry.value}</p>
                        <div className="flex items-start gap-2">
                          <button type="button" aria-label={`Edit ${label}`} className={secondaryButton} disabled={controlsLocked} onClick={() => editMemory(entry)}><Pencil className="h-4 w-4" aria-hidden="true" /> Edit</button>
                          <button type="button" aria-label={`Delete ${label}`} className={`${secondaryButton} text-rose`} disabled={controlsLocked} onClick={() => void removeMemory(entry)}><Trash2 className="h-4 w-4" aria-hidden="true" /> Delete</button>
                        </div>
                      </>
                    )}
                  </li>
                )
              })}
            </ul>
          )}
          {memories.length >= 200 && <p className="text-sm text-amber">Showing the first 200 matches. Refine the search to find older entries.</p>}
          <p className="max-w-[75ch] text-xs leading-5 text-dust">
            This view excludes internal chat history. The store records values, save times, and expiration, but not whether an entry came from onboarding or an AI tool.
          </p>
        </section>
      )}
    </div>
  )
}

function formatDate(value: string) {
  return new Date(value).toLocaleString()
}
