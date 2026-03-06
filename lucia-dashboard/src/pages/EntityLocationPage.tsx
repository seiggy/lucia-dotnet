import { useState, useEffect, useCallback, useRef } from 'react'
import {
  fetchEntityLocationSummary,
  fetchEntityLocationEmbeddingProgress,
  fetchEntityLocationFloors,
  fetchEntityLocationAreas,
  fetchEntityLocationEntities,
  searchEntityLocation,
  connectEntityLocationEmbeddingProgressStream,
  invalidateEntityLocationCache,
  evictEntityLocationEmbedding,
  regenerateEntityLocationEmbedding,
  fetchEntityVisibility,
  updateVisibilitySettings,
  updateEntityAgents,
  clearAllAgentFilters,
  fetchAvailableAgents,
  type EntityLocationEmbeddingProgress,
} from '../api'
import {
  MapPin, Building2, Layers, Hash, Loader2, RefreshCw, Search, Clock,
  ChevronLeft, ChevronRight, Shield, ShieldOff, Users, X, Check, Trash2, Eye, EyeOff,
} from 'lucide-react'

interface FloorInfo {
  floorId: string
  name: string
  aliases: string[]
  level: number | null
  icon: string | null
}

interface AreaInfo {
  areaId: string
  name: string
  floorId: string | null
  aliases: string[]
  entityIds?: string[]
  entityCount?: number
  icon: string | null
  labels: string[]
}

interface EntityLocationInfo {
  entityId: string
  friendlyName: string
  domain: string
  aliases: string[]
  areaId: string | null
  platform: string | null
  embeddingGenerated?: boolean
  includeForAgent: string[] | null
}

interface LocationSummary {
  floorCount: number
  areaCount: number
  entityCount: number
  floorEmbeddingsGenerated?: number
  areaEmbeddingsGenerated?: number
  entityEmbeddingsGenerated?: number
  entityEmbeddingsMissing?: number
  embeddingGenerationInProgress?: boolean
  lastLoadedAt: string | null
}

type Tab = 'floors' | 'areas' | 'entities' | 'search'

function formatDate(iso: string | null) {
  if (!iso) return '—'
  return new Date(iso).toLocaleString()
}

function Badge({ children, color = 'amber' }: { children: React.ReactNode; color?: string }) {
  const colorMap: Record<string, string> = {
    amber: 'bg-amber/15 text-amber',
    sage: 'bg-sage/15 text-sage',
    sky: 'bg-sky-400/15 text-sky-400',
    fog: 'bg-fog/15 text-fog',
    rose: 'bg-rose/15 text-rose',
  }
  return (
    <span className={`rounded-md px-2 py-0.5 text-xs font-medium ${colorMap[color] ?? colorMap.fog}`}>
      {children}
    </span>
  )
}

export default function EntityLocationPage() {
  const [summary, setSummary] = useState<LocationSummary | null>(null)
  const [floors, setFloors] = useState<FloorInfo[]>([])
  const [areas, setAreas] = useState<AreaInfo[]>([])
  const [entities, setEntities] = useState<EntityLocationInfo[]>([])
  const [searchResults, setSearchResults] = useState<EntityLocationInfo[]>([])
  const [activeTab, setActiveTab] = useState<Tab>('floors')
  const [loading, setLoading] = useState(true)
  const [invalidating, setInvalidating] = useState(false)
  const [searching, setSearching] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [searchTerm, setSearchTerm] = useState('')
  const [searchDomain, setSearchDomain] = useState('')
  const [domainFilter, setDomainFilter] = useState('')
  const [entityPage, setEntityPage] = useState(0)
  const entityPageSize = 50
  const [embeddingActionKey, setEmbeddingActionKey] = useState<string | null>(null)
  const [embeddingProgress, setEmbeddingProgress] = useState<EntityLocationEmbeddingProgress | null>(null)

  // Visibility state
  const [useExposedOnly, setUseExposedOnly] = useState(false)
  const [availableAgents, setAvailableAgents] = useState<{ name: string; domains: string[] }[]>([])
  const [entityAgentMap, setEntityAgentMap] = useState<Record<string, string[]>>({})
  const [togglingExposed, setTogglingExposed] = useState(false)

  // Selection state for bulk operations
  const [selectedEntityIds, setSelectedEntityIds] = useState<Set<string>>(new Set())
  const [bulkAgentDropdownOpen, setBulkAgentDropdownOpen] = useState(false)

  // Agent impersonation — filters entities and search to a specific agent's view
  const [impersonateAgent, setImpersonateAgent] = useState('')

  // Per-row agent dropdown
  const [agentDropdownEntityId, setAgentDropdownEntityId] = useState<string | null>(null)
  const dropdownRef = useRef<HTMLDivElement>(null)
  const embeddingStreamRef = useRef<EventSource | null>(null)
  const embeddingGenerationWasRunningRef = useRef(false)

  // Close dropdown on outside click
  useEffect(() => {
    function handleClick(e: MouseEvent) {
      if (dropdownRef.current && !dropdownRef.current.contains(e.target as Node)) {
        setAgentDropdownEntityId(null)
        setBulkAgentDropdownOpen(false)
      }
    }
    document.addEventListener('mousedown', handleClick)
    return () => document.removeEventListener('mousedown', handleClick)
  }, [])

  const loadSummary = useCallback(async () => {
    try {
      const data = await fetchEntityLocationSummary()
      setSummary(data)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load summary')
    }
  }, [])

  const applyEmbeddingProgress = useCallback((progress: EntityLocationEmbeddingProgress) => {
    setEmbeddingProgress(progress)
    setSummary(prev => {
      if (!prev) {
        return prev
      }

      return {
        ...prev,
        floorEmbeddingsGenerated: progress.floorGeneratedCount,
        areaEmbeddingsGenerated: progress.areaGeneratedCount,
        entityEmbeddingsGenerated: progress.entityGeneratedCount,
        entityEmbeddingsMissing: progress.entityMissingCount,
        embeddingGenerationInProgress: progress.isGenerationRunning,
      }
    })
  }, [])

  const loadEmbeddingProgress = useCallback(async () => {
    try {
      const progress = await fetchEntityLocationEmbeddingProgress()
      applyEmbeddingProgress(progress)
    } catch {
      // Non-fatal; SSE stream may still reconnect and provide updates.
    }
  }, [applyEmbeddingProgress])

  const loadVisibility = useCallback(async () => {
    try {
      const [vis, agents] = await Promise.all([fetchEntityVisibility(), fetchAvailableAgents()])
      setUseExposedOnly(vis.useExposedEntitiesOnly)
      setEntityAgentMap(vis.entityAgentMap)
      setAvailableAgents(agents)
    } catch {
      // Visibility config may not exist yet — ignore
    }
  }, [])

  const loadTab = useCallback(async (tab: Tab) => {
    if (tab === 'search') return
    setLoading(true)
    setError(null)
    try {
      if (tab === 'floors') setFloors(await fetchEntityLocationFloors())
      else if (tab === 'areas') setAreas(await fetchEntityLocationAreas())
      else if (tab === 'entities') {
        const result = await fetchEntityLocationEntities(domainFilter || undefined)
        setEntities(result)
        setEntityPage(0)
        setSelectedEntityIds(new Set())
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load data')
    } finally {
      setLoading(false)
    }
  }, [domainFilter])

  useEffect(() => {
    loadSummary()
    loadEmbeddingProgress()
    loadVisibility()
    loadTab('floors')
  }, [loadSummary, loadEmbeddingProgress, loadVisibility, loadTab])

  useEffect(() => {
    if (activeTab !== 'search') loadTab(activeTab)
  }, [activeTab, loadTab])

  useEffect(() => {
    const source = connectEntityLocationEmbeddingProgressStream(
      (progress) => {
        applyEmbeddingProgress(progress)
      },
      () => {
        // EventSource auto-reconnects; avoid noisy UI errors for transient disconnects.
      },
    )

    embeddingStreamRef.current = source

    return () => {
      source.close()
      if (embeddingStreamRef.current === source) {
        embeddingStreamRef.current = null
      }
    }
  }, [applyEmbeddingProgress])

  useEffect(() => {
    if (!embeddingProgress) {
      return
    }

    const wasRunning = embeddingGenerationWasRunningRef.current
    embeddingGenerationWasRunningRef.current = embeddingProgress.isGenerationRunning

    if (wasRunning && !embeddingProgress.isGenerationRunning && activeTab === 'entities') {
      void loadTab('entities')
    }
  }, [embeddingProgress, activeTab, loadTab])

  async function handleInvalidate() {
    if (!confirm('Invalidate and reload all location data from Home Assistant?')) return
    setInvalidating(true)
    setError(null)
    try {
      await invalidateEntityLocationCache()
      await Promise.all([loadSummary(), loadEmbeddingProgress(), loadVisibility()])
      await loadTab(activeTab)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to invalidate cache')
    } finally {
      setInvalidating(false)
    }
  }

  async function handleToggleExposed() {
    setTogglingExposed(true)
    setError(null)
    try {
      await updateVisibilitySettings(!useExposedOnly)
      setUseExposedOnly(!useExposedOnly)
      await Promise.all([loadSummary(), loadTab(activeTab)])
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to toggle exposed filter')
    } finally {
      setTogglingExposed(false)
    }
  }

  async function handleSearch() {
    if (!searchTerm.trim()) return
    setSearching(true)
    setError(null)
    setSelectedEntityIds(new Set())
    try {
      const data = await searchEntityLocation(searchTerm.trim(), searchDomain || undefined, impersonateAgent || undefined)
      setSearchResults(data.entities ?? data)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Search failed')
    } finally {
      setSearching(false)
    }
  }

  async function handleEvictEmbedding(entityId: string) {
    if (!confirm(`Evict cached embedding for "${entityId}"?`)) return
    setEmbeddingActionKey(`${entityId}:evict`)
    setError(null)
    try {
      await evictEntityLocationEmbedding('entity', entityId)
      setEntities(prev => prev.map(e => e.entityId === entityId ? { ...e, embeddingGenerated: false } : e))
      setSearchResults(prev => prev.map(e => e.entityId === entityId ? { ...e, embeddingGenerated: false } : e))
      await loadSummary()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to evict embedding')
    } finally {
      setEmbeddingActionKey(null)
    }
  }

  async function handleRegenerateEmbedding(entityId: string) {
    setEmbeddingActionKey(`${entityId}:regenerate`)
    setError(null)
    try {
      await regenerateEntityLocationEmbedding('entity', entityId)
      setEntities(prev => prev.map(e => e.entityId === entityId ? { ...e, embeddingGenerated: true } : e))
      setSearchResults(prev => prev.map(e => e.entityId === entityId ? { ...e, embeddingGenerated: true } : e))
      await loadSummary()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to regenerate embedding')
    } finally {
      setEmbeddingActionKey(null)
    }
  }

  // ── Agent visibility helpers ──────────────────────────────────

  function getEntityAgents(entityId: string): string[] | null {
    if (entityId in entityAgentMap) return entityAgentMap[entityId]
    return null // visible to all
  }

  async function setAgentsForEntity(entityId: string, agents: string[] | null) {
    try {
      await updateEntityAgents({ [entityId]: agents })
      setEntityAgentMap(prev => {
        const next = { ...prev }
        if (agents === null) {
          delete next[entityId]
        } else {
          next[entityId] = agents
        }
        return next
      })
      // Update in-memory entity list
      setEntities(prev => prev.map(e =>
        e.entityId === entityId ? { ...e, includeForAgent: agents } : e
      ))
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to update agent visibility')
    }
  }

  async function handleBulkAssign(agents: string[] | null) {
    const ids = Array.from(selectedEntityIds)
    if (ids.length === 0) return
    try {
      const updates: Record<string, string[] | null> = {}
      ids.forEach(id => { updates[id] = agents })
      await updateEntityAgents(updates)
      setEntityAgentMap(prev => {
        const next = { ...prev }
        ids.forEach(id => {
          if (agents === null) {
            delete next[id]
          } else {
            next[id] = agents
          }
        })
        return next
      })
      setEntities(prev => prev.map(e =>
        selectedEntityIds.has(e.entityId) ? { ...e, includeForAgent: agents } : e
      ))
      setSelectedEntityIds(new Set())
      setBulkAgentDropdownOpen(false)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to bulk-assign agents')
    }
  }

  async function handleClearAllFilters() {
    if (!confirm('Clear all per-entity agent filters? Every entity will become visible to all agents.')) return
    try {
      await clearAllAgentFilters()
      setEntityAgentMap({})
      setEntities(prev => prev.map(e => ({ ...e, includeForAgent: null })))
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to clear agent filters')
    }
  }

  function toggleSelect(entityId: string) {
    setSelectedEntityIds(prev => {
      const next = new Set(prev)
      if (next.has(entityId)) next.delete(entityId)
      else next.add(entityId)
      return next
    })
  }

  function toggleSelectAll(entityList: EntityLocationInfo[]) {
    const allSelected = entityList.every(e => selectedEntityIds.has(e.entityId))
    if (allSelected) {
      setSelectedEntityIds(new Set())
    } else {
      setSelectedEntityIds(new Set(entityList.map(e => e.entityId)))
    }
  }

  // ── Derived state ──────────────────────────────────────────────

  // When impersonating an agent, filter entities to only those visible to that agent
  const visibleEntities = impersonateAgent
    ? entities.filter(e =>
        e.includeForAgent === null ||
        e.includeForAgent.includes(impersonateAgent))
    : entities

  const entityPageCount = Math.max(1, Math.ceil(visibleEntities.length / entityPageSize))
  const pagedEntities = visibleEntities.slice(entityPage * entityPageSize, (entityPage + 1) * entityPageSize)
  const hasFilters = Object.keys(entityAgentMap).length > 0

  const tabs: { id: Tab; label: string; icon: typeof Layers }[] = [
    { id: 'floors', label: 'Floors', icon: Layers },
    { id: 'areas', label: 'Areas', icon: Building2 },
    { id: 'entities', label: 'Entities', icon: Hash },
    { id: 'search', label: 'Search', icon: Search },
  ]

  // ── Agent badge renderer ──────────────────────────────────────

  function AgentBadges({ entityId }: { entityId: string }) {
    const agents = getEntityAgents(entityId)
    if (agents === null) return <Badge color="amber">All</Badge>
    if (agents.length === 0) return <Badge color="rose">None</Badge>
    return (
      <div className="flex flex-wrap gap-1">
        {agents.map(a => <Badge key={a} color="sky">{a}</Badge>)}
      </div>
    )
  }

  // ── Per-entity agent dropdown ─────────────────────────────────

  function AgentDropdown({ entityId }: { entityId: string }) {
    const isOpen = agentDropdownEntityId === entityId
    const currentAgents = getEntityAgents(entityId)

    function toggleAgent(agent: string) {
      const current = currentAgents ?? [...availableAgents.map(a => a.name)] // null = all → start with all
      const updated = current.includes(agent)
        ? current.filter(a => a !== agent)
        : [...current, agent]
      setAgentsForEntity(entityId, updated.length === 0 ? [] : updated)
    }

    return (
      <div className="relative">
        <button
          onClick={() => setAgentDropdownEntityId(isOpen ? null : entityId)}
          className="flex items-center gap-1 rounded-md px-1.5 py-0.5 text-xs text-dust transition-colors hover:bg-stone/40 hover:text-fog"
          title="Edit agent visibility"
        >
          <AgentBadges entityId={entityId} />
          <Users className="ml-1 h-3 w-3 flex-shrink-0 opacity-50" />
        </button>

        {isOpen && (
          <div
            ref={dropdownRef}
            className="absolute right-0 top-full z-50 mt-1 w-56 rounded-lg border border-stone bg-basalt shadow-xl shadow-black/40"
          >
            <div className="border-b border-stone/60 px-3 py-2 text-xs font-medium uppercase tracking-wider text-dust">
              Agent Visibility
            </div>
            <div className="max-h-48 overflow-y-auto p-1">
              {/* Reset to All */}
              <button
                onClick={() => { setAgentsForEntity(entityId, null); setAgentDropdownEntityId(null) }}
                className={`flex w-full items-center gap-2 rounded-md px-3 py-1.5 text-left text-sm transition-colors ${
                  currentAgents === null ? 'bg-amber/15 text-amber' : 'text-fog hover:bg-stone/40'
                }`}
              >
                <Eye className="h-3.5 w-3.5" />
                All Agents
                {currentAgents === null && <Check className="ml-auto h-3.5 w-3.5" />}
              </button>

              {/* Exclude from all */}
              <button
                onClick={() => { setAgentsForEntity(entityId, []); setAgentDropdownEntityId(null) }}
                className={`flex w-full items-center gap-2 rounded-md px-3 py-1.5 text-left text-sm transition-colors ${
                  currentAgents?.length === 0 ? 'bg-rose/15 text-rose' : 'text-fog hover:bg-stone/40'
                }`}
              >
                <EyeOff className="h-3.5 w-3.5" />
                No Agents
                {currentAgents?.length === 0 && <Check className="ml-auto h-3.5 w-3.5" />}
              </button>

              <div className="my-1 border-t border-stone/40" />

              {/* Individual agents */}
              {availableAgents.map(({ name: agent }) => {
                const isSelected = currentAgents === null || currentAgents.includes(agent)
                return (
                  <button
                    key={agent}
                    onClick={() => toggleAgent(agent)}
                    className={`flex w-full items-center gap-2 rounded-md px-3 py-1.5 text-left text-sm transition-colors ${
                      isSelected ? 'text-sky-400' : 'text-dust hover:text-fog'
                    } hover:bg-stone/40`}
                  >
                    <div className={`flex h-4 w-4 items-center justify-center rounded border ${
                      isSelected ? 'border-sky-400 bg-sky-400/20' : 'border-stone'
                    }`}>
                      {isSelected && <Check className="h-3 w-3" />}
                    </div>
                    {agent}
                  </button>
                )
              })}
            </div>
          </div>
        )}
      </div>
    )
  }

  // ── Bulk action bar ───────────────────────────────────────────

  function BulkActionBar() {
    const count = selectedEntityIds.size
    if (count === 0) return null

    return (
      <div className="flex items-center gap-3 rounded-xl border border-amber/30 bg-amber/5 px-4 py-2.5">
        <span className="text-sm font-medium text-amber">{count} selected</span>
        <div className="h-4 w-px bg-stone/60" />

        {/* Assign to agents dropdown */}
        <div className="relative" ref={bulkAgentDropdownOpen ? dropdownRef : undefined}>
          <button
            onClick={() => setBulkAgentDropdownOpen(!bulkAgentDropdownOpen)}
            className="flex items-center gap-1.5 rounded-lg bg-sky-400/15 px-3 py-1.5 text-xs font-medium text-sky-400 transition-colors hover:bg-sky-400/25"
          >
            <Users className="h-3.5 w-3.5" />
            Assign to Agent
          </button>
          {bulkAgentDropdownOpen && (
            <div className="absolute left-0 top-full z-50 mt-1 w-52 rounded-lg border border-stone bg-basalt shadow-xl shadow-black/40">
              <div className="max-h-48 overflow-y-auto p-1">
                {availableAgents.map(({ name: agent }) => (
                  <button
                    key={agent}
                    onClick={() => {
                      // Add this agent to each selected entity's agent list
                      const updates: Record<string, string[] | null> = {}
                      selectedEntityIds.forEach(id => {
                        const current = getEntityAgents(id) ?? []
                        if (!current.includes(agent)) {
                          updates[id] = [...current, agent]
                        }
                      })
                      if (Object.keys(updates).length > 0) {
                        handleBulkAssign(null) // won't run, we handle inline
                        updateEntityAgents(updates).then(() => {
                          setEntityAgentMap(prev => ({ ...prev, ...updates as Record<string, string[]> }))
                          setEntities(prev => prev.map(e => {
                            if (e.entityId in updates) return { ...e, includeForAgent: updates[e.entityId] }
                            return e
                          }))
                        })
                      }
                      setBulkAgentDropdownOpen(false)
                    }}
                    className="flex w-full items-center gap-2 rounded-md px-3 py-1.5 text-left text-sm text-fog transition-colors hover:bg-stone/40"
                  >
                    <Shield className="h-3.5 w-3.5 text-sky-400" />
                    {agent}
                  </button>
                ))}
              </div>
            </div>
          )}
        </div>

        <button
          onClick={() => handleBulkAssign(null)}
          className="flex items-center gap-1.5 rounded-lg bg-amber/15 px-3 py-1.5 text-xs font-medium text-amber transition-colors hover:bg-amber/25"
          title="Reset selected to visible by all agents"
        >
          <Eye className="h-3.5 w-3.5" />
          All Agents
        </button>

        <button
          onClick={() => handleBulkAssign([])}
          className="flex items-center gap-1.5 rounded-lg bg-rose/15 px-3 py-1.5 text-xs font-medium text-rose transition-colors hover:bg-rose/25"
          title="Exclude selected from all agents"
        >
          <EyeOff className="h-3.5 w-3.5" />
          No Agents
        </button>

        <button
          onClick={() => { setSelectedEntityIds(new Set()) }}
          className="ml-auto rounded-lg p-1.5 text-dust transition-colors hover:bg-stone/40 hover:text-fog"
          title="Clear selection"
        >
          <X className="h-4 w-4" />
        </button>
      </div>
    )
  }

  // ── Entity table renderer (shared between tabs) ───────────────

  function EntityTable({ entityList, showSelect }: { entityList: EntityLocationInfo[]; showSelect: boolean }) {
    const allSelected = entityList.length > 0 && entityList.every(e => selectedEntityIds.has(e.entityId))

    return (
      <div className="glass-panel overflow-x-auto rounded-xl">
        <table className="w-full text-left text-sm">
          <thead className="border-b border-stone text-xs font-medium uppercase tracking-wider text-dust">
            <tr>
              {showSelect && (
                <th className="w-10 px-3 py-3">
                  <button
                    onClick={() => toggleSelectAll(entityList)}
                    className={`flex h-4 w-4 items-center justify-center rounded border transition-colors ${
                      allSelected ? 'border-amber bg-amber/20 text-amber' : 'border-stone hover:border-fog'
                    }`}
                  >
                    {allSelected && <Check className="h-3 w-3" />}
                  </button>
                </th>
              )}
              <th className="px-4 py-3">Entity ID</th>
              <th className="px-4 py-3">Friendly Name</th>
              <th className="px-4 py-3">Domain</th>
              <th className="px-4 py-3">Area</th>
              <th className="px-4 py-3">Embedding</th>
              <th className="px-4 py-3">Agents</th>
              <th className="px-4 py-3">Actions</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-stone/50">
            {entityList.map((e) => (
              <tr key={e.entityId} className="transition-colors hover:bg-basalt/60">
                {showSelect && (
                  <td className="px-3 py-3">
                    <button
                      onClick={() => toggleSelect(e.entityId)}
                      className={`flex h-4 w-4 items-center justify-center rounded border transition-colors ${
                        selectedEntityIds.has(e.entityId) ? 'border-amber bg-amber/20 text-amber' : 'border-stone hover:border-fog'
                      }`}
                    >
                      {selectedEntityIds.has(e.entityId) && <Check className="h-3 w-3" />}
                    </button>
                  </td>
                )}
                <td className="px-4 py-3 font-mono text-xs text-fog">{e.entityId}</td>
                <td className="px-4 py-3 text-light">{e.friendlyName}</td>
                <td className="px-4 py-3"><Badge>{e.domain}</Badge></td>
                <td className="px-4 py-3 text-fog">{e.areaId ?? '—'}</td>
                <td className="px-4 py-3">
                  {e.embeddingGenerated
                    ? <Badge color="sage">Generated</Badge>
                    : <Badge color="rose">Missing</Badge>}
                </td>
                <td className="px-4 py-3">
                  <AgentDropdown entityId={e.entityId} />
                </td>
                <td className="px-4 py-3">
                  <div className="flex items-center gap-1">
                    <button
                      onClick={() => handleRegenerateEmbedding(e.entityId)}
                      disabled={embeddingActionKey !== null}
                      className="rounded-md p-1 text-sky-400 transition-colors hover:bg-sky-400/15 disabled:opacity-40"
                      title="Regenerate embedding"
                    >
                      {embeddingActionKey === `${e.entityId}:regenerate`
                        ? <Loader2 className="h-3.5 w-3.5 animate-spin" />
                        : <RefreshCw className="h-3.5 w-3.5" />}
                    </button>
                    <button
                      onClick={() => handleEvictEmbedding(e.entityId)}
                      disabled={embeddingActionKey !== null}
                      className="rounded-md p-1 text-rose transition-colors hover:bg-rose/15 disabled:opacity-40"
                      title="Evict cached embedding"
                    >
                      {embeddingActionKey === `${e.entityId}:evict`
                        ? <Loader2 className="h-3.5 w-3.5 animate-spin" />
                        : <Trash2 className="h-3.5 w-3.5" />}
                    </button>
                  </div>
                </td>
              </tr>
            ))}
            {entityList.length === 0 && (
              <tr>
                <td colSpan={showSelect ? 8 : 7} className="px-4 py-12 text-center text-dust">
                  No entities found.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    )
  }

  const entityTotalForProgress = embeddingProgress?.entityTotalCount ?? summary?.entityCount ?? 0
  const entityGeneratedForProgress = embeddingProgress?.entityGeneratedCount ?? summary?.entityEmbeddingsGenerated ?? 0
  const entityMissingForProgress = embeddingProgress?.entityMissingCount
    ?? Math.max(entityTotalForProgress - entityGeneratedForProgress, 0)
  const entityProgressPercent = entityTotalForProgress > 0
    ? Math.round((entityGeneratedForProgress / entityTotalForProgress) * 100)
    : 0
  const embeddingGenerationInProgress = embeddingProgress?.isGenerationRunning
    ?? summary?.embeddingGenerationInProgress
    ?? false

  return (
    <div className="space-y-6">
      {/* Header with actions */}
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="font-display text-2xl font-bold text-light">Entity Locations</h1>
        <div className="flex items-center gap-2">
          {/* Exposed-only toggle */}
          <button
            onClick={handleToggleExposed}
            disabled={togglingExposed}
            className={`flex items-center gap-1.5 rounded-xl px-4 py-2 text-sm font-medium transition-colors ${
              useExposedOnly
                ? 'bg-sage/15 text-sage hover:bg-sage/25'
                : 'bg-stone/30 text-dust hover:bg-stone/40 hover:text-fog'
            } disabled:opacity-40`}
            title={useExposedOnly ? 'Loading only HA-exposed entities' : 'Loading all entities'}
          >
            {togglingExposed
              ? <Loader2 className="h-4 w-4 animate-spin" />
              : useExposedOnly ? <Shield className="h-4 w-4" /> : <ShieldOff className="h-4 w-4" />}
            {useExposedOnly ? 'Exposed Only' : 'All Entities'}
          </button>

          {/* Clear all filters */}
          {hasFilters && (
            <button
              onClick={handleClearAllFilters}
              className="flex items-center gap-1.5 rounded-xl bg-rose/15 px-4 py-2 text-sm font-medium text-rose transition-colors hover:bg-rose/25"
              title="Clear all per-entity agent filters"
            >
              <Trash2 className="h-4 w-4" />
              Clear All Filters
            </button>
          )}

          {/* Reload from HA */}
          <button
            onClick={handleInvalidate}
            disabled={invalidating}
            className="flex items-center gap-1.5 rounded-xl bg-amber/15 px-4 py-2 text-sm font-medium text-amber transition-colors hover:bg-amber/25 disabled:opacity-40"
          >
            {invalidating
              ? <Loader2 className="h-4 w-4 animate-spin" />
              : <RefreshCw className="h-4 w-4" />}
            Reload from HA
          </button>
        </div>
      </div>

      {/* Summary cards */}
      {summary && (
        <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
          <div className="glass-panel rounded-xl p-4">
            <div className="mb-1 flex items-center gap-1.5">
              <Layers className="h-3.5 w-3.5 text-amber" />
              <span className="text-xs font-medium uppercase tracking-wider text-dust">Floors</span>
            </div>
            <p className="font-display text-2xl font-bold text-light">{summary.floorCount}</p>
          </div>
          <div className="glass-panel rounded-xl p-4">
            <div className="mb-1 flex items-center gap-1.5">
              <Building2 className="h-3.5 w-3.5 text-sage" />
              <span className="text-xs font-medium uppercase tracking-wider text-dust">Areas</span>
            </div>
            <p className="font-display text-2xl font-bold text-sage">{summary.areaCount}</p>
          </div>
          <div className="glass-panel rounded-xl p-4">
            <div className="mb-1 flex items-center gap-1.5">
              <Hash className="h-3.5 w-3.5 text-sky-400" />
              <span className="text-xs font-medium uppercase tracking-wider text-dust">Entities</span>
            </div>
            <p className="font-display text-2xl font-bold text-sky-400">{summary.entityCount}</p>
            <p className="mt-1 text-xs text-dust">
              Embeddings: {summary.entityEmbeddingsGenerated ?? 0}/{summary.entityCount}
            </p>
          </div>
          <div className="glass-panel rounded-xl p-4">
            <div className="mb-1 flex items-center gap-1.5">
              <Clock className="h-3.5 w-3.5 text-fog" />
              <span className="text-xs font-medium uppercase tracking-wider text-dust">Last Loaded</span>
            </div>
            <p className="text-sm font-medium text-fog">{formatDate(summary.lastLoadedAt)}</p>
          </div>
        </div>
      )}

      {summary && (
        <div className="glass-panel rounded-xl p-4">
          <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
            <div className="flex items-center gap-2">
              <span className="text-sm font-medium text-light">Entity Embedding Generation</span>
              {embeddingGenerationInProgress
                ? <Badge color="amber">In Progress</Badge>
                : <Badge color="sage">Idle</Badge>}
            </div>
            <span className="text-xs text-dust">
              Missing embeddings: {entityMissingForProgress} / {entityTotalForProgress}
            </span>
          </div>

          <div className="h-2 w-full overflow-hidden rounded-full bg-stone/50">
            <div
              className="h-full rounded-full bg-sky-400 transition-all duration-300"
              style={{ width: `${Math.min(entityProgressPercent, 100)}%` }}
            />
          </div>

          <p className="mt-2 text-xs text-dust">
            Generated {entityGeneratedForProgress} of {entityTotalForProgress} ({entityProgressPercent}%)
          </p>
        </div>
      )}

      {/* Tabs */}
      <div className="flex gap-1 border-b border-stone/40">
        {tabs.map(({ id, label, icon: Icon }) => (
          <button
            key={id}
            onClick={() => { setActiveTab(id); setSelectedEntityIds(new Set()) }}
            className={`flex items-center gap-1.5 border-b-2 px-4 py-2.5 text-sm font-medium transition-colors ${
              activeTab === id
                ? 'border-amber text-amber'
                : 'border-transparent text-dust hover:text-fog'
            }`}
          >
            <Icon className="h-4 w-4" />
            {label}
          </button>
        ))}
      </div>

      {error && <p className="text-rose">{error}</p>}

      {/* Floors tab */}
      {activeTab === 'floors' && (
        loading ? (
          <p className="flex items-center gap-2 text-dust"><Loader2 className="h-4 w-4 animate-spin" /> Loading floors…</p>
        ) : (
          <div className="glass-panel overflow-x-auto rounded-xl">
            <table className="w-full text-left text-sm">
              <thead className="border-b border-stone text-xs font-medium uppercase tracking-wider text-dust">
                <tr>
                  <th className="px-4 py-3">Name</th>
                  <th className="px-4 py-3">Floor ID</th>
                  <th className="px-4 py-3">Level</th>
                  <th className="px-4 py-3">Aliases</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-stone/50">
                {floors.map((f) => (
                  <tr key={f.floorId} className="transition-colors hover:bg-basalt/60">
                    <td className="px-4 py-3 font-medium text-light">{f.name}</td>
                    <td className="px-4 py-3 text-dust">{f.floorId}</td>
                    <td className="px-4 py-3 text-fog">{f.level ?? '—'}</td>
                    <td className="px-4 py-3">
                      <div className="flex flex-wrap gap-1">
                        {f.aliases.length > 0
                          ? f.aliases.map((a) => <Badge key={a} color="sage">{a}</Badge>)
                          : <span className="text-dust">—</span>}
                      </div>
                    </td>
                  </tr>
                ))}
                {floors.length === 0 && (
                  <tr><td colSpan={4} className="px-4 py-12 text-center text-dust">No floors cached.</td></tr>
                )}
              </tbody>
            </table>
          </div>
        )
      )}

      {/* Areas tab */}
      {activeTab === 'areas' && (
        loading ? (
          <p className="flex items-center gap-2 text-dust"><Loader2 className="h-4 w-4 animate-spin" /> Loading areas…</p>
        ) : (
          <div className="glass-panel overflow-x-auto rounded-xl">
            <table className="w-full text-left text-sm">
              <thead className="border-b border-stone text-xs font-medium uppercase tracking-wider text-dust">
                <tr>
                  <th className="px-4 py-3">Name</th>
                  <th className="px-4 py-3">Area ID</th>
                  <th className="px-4 py-3">Floor</th>
                  <th className="px-4 py-3">Entities</th>
                  <th className="px-4 py-3">Aliases</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-stone/50">
                {areas.map((a) => (
                  <tr key={a.areaId} className="transition-colors hover:bg-basalt/60">
                    <td className="px-4 py-3 font-medium text-light">{a.name}</td>
                    <td className="px-4 py-3 text-dust">{a.areaId}</td>
                    <td className="px-4 py-3 text-fog">{a.floorId ?? '—'}</td>
                    <td className="px-4 py-3"><Badge color="sky">{a.entityCount ?? a.entityIds?.length ?? 0}</Badge></td>
                    <td className="px-4 py-3">
                      <div className="flex flex-wrap gap-1">
                        {a.aliases.length > 0
                          ? a.aliases.map((al) => <Badge key={al} color="sage">{al}</Badge>)
                          : <span className="text-dust">—</span>}
                      </div>
                    </td>
                  </tr>
                ))}
                {areas.length === 0 && (
                  <tr><td colSpan={5} className="px-4 py-12 text-center text-dust">No areas cached.</td></tr>
                )}
              </tbody>
            </table>
          </div>
        )
      )}

      {/* Entities tab */}
      {activeTab === 'entities' && (
        <>
          <div className="flex items-center gap-3">
            <input
              type="text"
              placeholder="Filter by domain (e.g., light, climate)"
              value={domainFilter}
              onChange={(e) => setDomainFilter(e.target.value)}
              onKeyDown={(e) => { if (e.key === 'Enter') loadTab('entities') }}
              className="rounded-lg border border-stone bg-basalt px-3 py-2 text-sm text-fog placeholder:text-dust/50 focus:border-amber focus:outline-none"
            />
            <select
              value={impersonateAgent}
              onChange={(e) => {
                const agentName = e.target.value
                setImpersonateAgent(agentName)
                setEntityPage(0)
                // Auto-apply domain filter from agent's skill domains
                const agent = availableAgents.find(a => a.name === agentName)
                if (agent && agent.domains.length > 0) {
                  setDomainFilter(agent.domains.join(','))
                  // Reload with the new domain filter
                  setTimeout(() => loadTab('entities'), 0)
                } else if (!agentName) {
                  setDomainFilter('')
                }
              }}
              className="rounded-lg border border-stone bg-basalt px-3 py-2 text-sm text-fog focus:border-amber focus:outline-none"
            >
              <option value="">All Agents</option>
              {availableAgents.map(a => (
                <option key={a.name} value={a.name}>
                  {a.name}{a.domains.length > 0 ? ` (${a.domains.join(', ')})` : ''}
                </option>
              ))}
            </select>
            {impersonateAgent && (
              <span className="rounded-md bg-sky-400/15 px-2 py-1 text-xs font-medium text-sky-400">
                👁 {impersonateAgent} view — {visibleEntities.length} entities
              </span>
            )}
            <button
              onClick={() => loadTab('entities')}
              className="rounded-lg border border-stone bg-basalt px-3 py-2 text-sm font-medium text-fog hover:bg-stone/40"
            >
              Filter
            </button>
          </div>

          <BulkActionBar />

          {loading ? (
            <p className="flex items-center gap-2 text-dust"><Loader2 className="h-4 w-4 animate-spin" /> Loading entities…</p>
          ) : (
            <>
              <EntityTable entityList={pagedEntities} showSelect />
              {entityPageCount > 1 && (
                <div className="flex items-center justify-between">
                  <span className="text-xs text-dust">
                    Showing {entityPage * entityPageSize + 1}–{Math.min((entityPage + 1) * entityPageSize, visibleEntities.length)} of {visibleEntities.length}
                  </span>
                  <div className="flex items-center gap-1">
                    <button
                      onClick={() => setEntityPage(p => Math.max(0, p - 1))}
                      disabled={entityPage === 0}
                      className="rounded-lg border border-stone bg-basalt p-1.5 text-fog hover:bg-stone/40 disabled:opacity-30"
                    >
                      <ChevronLeft className="h-4 w-4" />
                    </button>
                    {Array.from({ length: entityPageCount }, (_, i) => (
                      <button
                        key={i}
                        onClick={() => setEntityPage(i)}
                        className={`min-w-[2rem] rounded-lg border px-2 py-1 text-xs font-medium ${
                          i === entityPage
                            ? 'border-amber bg-amber/15 text-amber'
                            : 'border-stone bg-basalt text-dust hover:bg-stone/40'
                        }`}
                      >
                        {i + 1}
                      </button>
                    )).slice(
                      Math.max(0, entityPage - 2),
                      Math.min(entityPageCount, entityPage + 3)
                    )}
                    {entityPage + 3 < entityPageCount && <span className="px-1 text-xs text-dust">…</span>}
                    <button
                      onClick={() => setEntityPage(p => Math.min(entityPageCount - 1, p + 1))}
                      disabled={entityPage >= entityPageCount - 1}
                      className="rounded-lg border border-stone bg-basalt p-1.5 text-fog hover:bg-stone/40 disabled:opacity-30"
                    >
                      <ChevronRight className="h-4 w-4" />
                    </button>
                  </div>
                </div>
              )}
            </>
          )}
        </>
      )}

      {/* Search tab */}
      {activeTab === 'search' && (
        <>
          <div className="flex items-center gap-3">
            <div className="relative flex-1">
              <MapPin className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-dust" />
              <input
                type="text"
                placeholder="Search by location (e.g., kitchen, upstairs, downstairs)"
                value={searchTerm}
                onChange={(e) => setSearchTerm(e.target.value)}
                onKeyDown={(e) => { if (e.key === 'Enter') handleSearch() }}
                className="w-full rounded-lg border border-stone bg-basalt py-2 pl-10 pr-3 text-sm text-fog placeholder:text-dust/50 focus:border-amber focus:outline-none"
              />
            </div>
            <input
              type="text"
              placeholder="Domain filter"
              value={searchDomain}
              onChange={(e) => setSearchDomain(e.target.value)}
              onKeyDown={(e) => { if (e.key === 'Enter') handleSearch() }}
              className="w-40 rounded-lg border border-stone bg-basalt px-3 py-2 text-sm text-fog placeholder:text-dust/50 focus:border-amber focus:outline-none"
            />
            <select
              value={impersonateAgent}
              onChange={(e) => {
                const agentName = e.target.value
                setImpersonateAgent(agentName)
                const agent = availableAgents.find(a => a.name === agentName)
                if (agent && agent.domains.length > 0) {
                  setSearchDomain(agent.domains.join(','))
                } else if (!agentName) {
                  setSearchDomain('')
                }
              }}
              className="rounded-lg border border-stone bg-basalt px-3 py-2 text-sm text-fog focus:border-amber focus:outline-none"
            >
              <option value="">All Agents</option>
              {availableAgents.map(a => (
                <option key={a.name} value={a.name}>
                  {a.name}{a.domains.length > 0 ? ` (${a.domains.join(', ')})` : ''}
                </option>
              ))}
            </select>
            <button
              onClick={handleSearch}
              disabled={searching || !searchTerm.trim()}
              className="flex items-center gap-1.5 rounded-lg bg-amber/15 px-4 py-2 text-sm font-medium text-amber hover:bg-amber/25 disabled:opacity-40"
            >
              {searching ? <Loader2 className="h-4 w-4 animate-spin" /> : <Search className="h-4 w-4" />}
              Search
            </button>
          </div>

          {searchResults.length > 0 && (
            <>
              <div className="flex items-center justify-between">
                <span className="text-xs font-medium text-dust">
                  {searchResults.length} entit{searchResults.length === 1 ? 'y' : 'ies'} matched
                </span>
              </div>
              <BulkActionBar />
              <EntityTable entityList={searchResults} showSelect />
            </>
          )}
          {!searching && searchTerm && searchResults.length === 0 && (
            <p className="text-center text-dust">No entities matched &ldquo;{searchTerm}&rdquo;.</p>
          )}
        </>
      )}
    </div>
  )
}
