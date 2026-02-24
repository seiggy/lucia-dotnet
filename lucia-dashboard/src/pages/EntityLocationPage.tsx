import { useState, useEffect, useCallback } from 'react'
import {
  fetchEntityLocationSummary,
  fetchEntityLocationFloors,
  fetchEntityLocationAreas,
  fetchEntityLocationEntities,
  searchEntityLocation,
  invalidateEntityLocationCache,
} from '../api'
import {
  MapPin, Building2, Layers, Hash, Loader2, RefreshCw, Search, Clock, ChevronLeft, ChevronRight,
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
}

interface LocationSummary {
  floorCount: number
  areaCount: number
  entityCount: number
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

  const loadSummary = useCallback(async () => {
    try {
      const data = await fetchEntityLocationSummary()
      setSummary(data)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load summary')
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
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load data')
    } finally {
      setLoading(false)
    }
  }, [domainFilter])

  useEffect(() => {
    loadSummary()
    loadTab('floors')
  }, [loadSummary, loadTab])

  useEffect(() => {
    if (activeTab !== 'search') loadTab(activeTab)
  }, [activeTab, loadTab])

  async function handleInvalidate() {
    if (!confirm('Invalidate and reload all location data from Home Assistant?')) return
    setInvalidating(true)
    setError(null)
    try {
      await invalidateEntityLocationCache()
      await loadSummary()
      await loadTab(activeTab)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to invalidate cache')
    } finally {
      setInvalidating(false)
    }
  }

  async function handleSearch() {
    if (!searchTerm.trim()) return
    setSearching(true)
    setError(null)
    try {
      const data = await searchEntityLocation(searchTerm.trim(), searchDomain || undefined)
      setSearchResults(data.entities ?? data)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Search failed')
    } finally {
      setSearching(false)
    }
  }

  const entityPageCount = Math.max(1, Math.ceil(entities.length / entityPageSize))
  const pagedEntities = entities.slice(entityPage * entityPageSize, (entityPage + 1) * entityPageSize)

  const tabs: { id: Tab; label: string; icon: typeof Layers }[] = [
    { id: 'floors', label: 'Floors', icon: Layers },
    { id: 'areas', label: 'Areas', icon: Building2 },
    { id: 'entities', label: 'Entities', icon: Hash },
    { id: 'search', label: 'Search', icon: Search },
  ]

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="font-display text-2xl font-bold text-light">Entity Locations</h1>
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

      {/* Tabs */}
      <div className="flex gap-1 border-b border-stone/40">
        {tabs.map(({ id, label, icon: Icon }) => (
          <button
            key={id}
            onClick={() => setActiveTab(id)}
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
            <button
              onClick={() => loadTab('entities')}
              className="rounded-lg border border-stone bg-basalt px-3 py-2 text-sm font-medium text-fog hover:bg-stone/40"
            >
              Filter
            </button>
          </div>
          {loading ? (
            <p className="flex items-center gap-2 text-dust"><Loader2 className="h-4 w-4 animate-spin" /> Loading entities…</p>
          ) : (
            <>
              <div className="glass-panel overflow-x-auto rounded-xl">
                <table className="w-full text-left text-sm">
                  <thead className="border-b border-stone text-xs font-medium uppercase tracking-wider text-dust">
                    <tr>
                      <th className="px-4 py-3">Entity ID</th>
                      <th className="px-4 py-3">Friendly Name</th>
                      <th className="px-4 py-3">Domain</th>
                      <th className="px-4 py-3">Area</th>
                      <th className="px-4 py-3">Aliases</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-stone/50">
                    {pagedEntities.map((e) => (
                      <tr key={e.entityId} className="transition-colors hover:bg-basalt/60">
                        <td className="px-4 py-3 font-mono text-xs text-fog">{e.entityId}</td>
                        <td className="px-4 py-3 text-light">{e.friendlyName}</td>
                        <td className="px-4 py-3"><Badge>{e.domain}</Badge></td>
                        <td className="px-4 py-3 text-fog">{e.areaId ?? '—'}</td>
                        <td className="px-4 py-3">
                          <div className="flex flex-wrap gap-1">
                            {e.aliases.length > 0
                              ? e.aliases.map((a) => <Badge key={a} color="sage">{a}</Badge>)
                              : <span className="text-dust">—</span>}
                          </div>
                        </td>
                      </tr>
                    ))}
                    {entities.length === 0 && (
                      <tr><td colSpan={5} className="px-4 py-12 text-center text-dust">No entities found.</td></tr>
                    )}
                  </tbody>
                </table>
              </div>
              {entityPageCount > 1 && (
                <div className="flex items-center justify-between">
                  <span className="text-xs text-dust">
                    Showing {entityPage * entityPageSize + 1}–{Math.min((entityPage + 1) * entityPageSize, entities.length)} of {entities.length}
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
            <div className="glass-panel overflow-x-auto rounded-xl">
              <div className="border-b border-stone px-4 py-2">
                <span className="text-xs font-medium text-dust">
                  {searchResults.length} entit{searchResults.length === 1 ? 'y' : 'ies'} matched
                </span>
              </div>
              <table className="w-full text-left text-sm">
                <thead className="border-b border-stone text-xs font-medium uppercase tracking-wider text-dust">
                  <tr>
                    <th className="px-4 py-3">Entity ID</th>
                    <th className="px-4 py-3">Friendly Name</th>
                    <th className="px-4 py-3">Domain</th>
                    <th className="px-4 py-3">Area</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-stone/50">
                  {searchResults.map((e) => (
                    <tr key={e.entityId} className="transition-colors hover:bg-basalt/60">
                      <td className="px-4 py-3 font-mono text-xs text-fog">{e.entityId}</td>
                      <td className="px-4 py-3 text-light">{e.friendlyName}</td>
                      <td className="px-4 py-3"><Badge>{e.domain}</Badge></td>
                      <td className="px-4 py-3 text-fog">{e.areaId ?? '—'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
          {!searching && searchTerm && searchResults.length === 0 && (
            <p className="text-center text-dust">No entities matched &ldquo;{searchTerm}&rdquo;.</p>
          )}
        </>
      )}
    </div>
  )
}
