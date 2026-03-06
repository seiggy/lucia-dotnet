import { useState, useCallback, useMemo } from 'react'
import { searchMatcherDebug } from '../api'
import {
  Search, Loader2, Layers, Building2, Hash, MapPin,
  SlidersHorizontal, ChevronDown, ChevronUp, Zap, X, Route, Info,
} from 'lucide-react'

interface MatchedFloor {
  floorId: string; name: string; aliases: string[]; level: number | null
  hybridScore: number; embeddingSimilarity: number
}
interface MatchedArea {
  areaId: string; name: string; aliases: string[]; floorId: string | null
  hybridScore: number; embeddingSimilarity: number
}
interface MatchedEntity {
  entityId: string; friendlyName: string; domain: string; aliases: string[]
  areaId: string | null; areaName: string | null
  hybridScore: number; embeddingSimilarity: number
}
interface ResolvedEntity {
  entityId: string; friendlyName: string; domain: string
  areaId: string | null; areaName: string | null
}
interface SearchResult {
  query: string
  options: { threshold: number; embeddingWeight: number; scoreDropoffRatio: number; disagreementPenalty: number; embeddingResolutionMargin: number; domainFilter: string[] }
  resolution: {
    strategy: string
    reason: string
    bestEntityScore: number | null
    bestAreaScore: number | null
    bestFloorScore: number | null
  }
  floors: MatchedFloor[]
  areas: MatchedArea[]
  entities: MatchedEntity[]
  resolvedEntities: ResolvedEntity[]
  summary: {
    floorMatchCount: number; areaMatchCount: number
    entityMatchCount: number; resolvedEntityCount: number
  }
}

const COMMON_DOMAINS = [
  'light', 'switch', 'fan', 'climate', 'cover', 'media_player',
  'sensor', 'binary_sensor', 'lock', 'vacuum', 'camera', 'automation',
]

const STRATEGY_STYLES: Record<string, { bg: string; text: string; icon: typeof Layers }> = {
  Entity: { bg: 'bg-sky-400/15 border-sky-400/30', text: 'text-sky-400', icon: Hash },
  Area: { bg: 'bg-sage/15 border-sage/30', text: 'text-sage', icon: Building2 },
  Floor: { bg: 'bg-amber/15 border-amber/30', text: 'text-amber', icon: Layers },
  None: { bg: 'bg-dust/15 border-dust/30', text: 'text-dust', icon: Search },
}

function ScoreBar({ score, label, accent = false }: { score: number; label: string; accent?: boolean }) {
  const pct = Math.round(score * 100)
  return (
    <div className="flex items-center gap-2 text-xs">
      <span className="w-20 text-fog shrink-0">{label}</span>
      <div className="flex-1 h-1.5 bg-stone rounded-full overflow-hidden">
        <div
          className={`h-full rounded-full transition-all duration-500 ${accent ? 'bg-amber' : 'bg-sage'}`}
          style={{ width: `${pct}%` }}
        />
      </div>
      <span className={`w-12 text-right font-mono tabular-nums ${accent ? 'text-amber' : 'text-mist'}`}>
        {score.toFixed(4)}
      </span>
    </div>
  )
}

function SectionHeader({ icon: Icon, label, count, color }: {
  icon: typeof Layers; label: string; count: number; color: string
}) {
  return (
    <div className="flex items-center gap-2 mb-3">
      <div className={`w-7 h-7 rounded-md flex items-center justify-center ${color}`}>
        <Icon size={14} />
      </div>
      <h3 className="font-display text-sm font-semibold text-cloud tracking-wide">{label}</h3>
      <span className="ml-auto text-xs font-mono text-dust">{count} match{count !== 1 ? 'es' : ''}</span>
    </div>
  )
}

export default function MatcherDebugPage() {
  const [query, setQuery] = useState('')
  const [loading, setLoading] = useState(false)
  const [result, setResult] = useState<SearchResult | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [showOptions, setShowOptions] = useState(false)
  const [threshold, setThreshold] = useState(0.55)
  const [embeddingWeight, setEmbeddingWeight] = useState(0.4)
  const [dropoff, setDropoff] = useState(0.80)
  const [disagreementPenalty, setDisagreementPenalty] = useState(0.4)
  const [embeddingResolutionMargin, setEmbeddingResolutionMargin] = useState(0.30)
  const [selectedDomains, setSelectedDomains] = useState<string[]>([])

  const doSearch = useCallback(async () => {
    if (!query.trim()) return
    setLoading(true)
    setError(null)
    try {
      const r = await searchMatcherDebug(query.trim(), {
        threshold,
        embeddingWeight,
        dropoff,
        disagreementPenalty,
        embeddingResolutionMargin,
        domains: selectedDomains.length > 0 ? selectedDomains : undefined,
      }) as SearchResult
      setResult(r)
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : 'Search failed')
      setResult(null)
    } finally {
      setLoading(false)
    }
  }, [query, threshold, embeddingWeight, dropoff, disagreementPenalty, embeddingResolutionMargin, selectedDomains])

  const toggleDomain = useCallback((domain: string) => {
    setSelectedDomains(prev =>
      prev.includes(domain) ? prev.filter(d => d !== domain) : [...prev, domain]
    )
  }, [])

  // Collect unique domains from entity matches and resolved entities
  const availableDomains = useMemo(() => {
    if (!result) return []
    const domains = new Set<string>()
    result.entities.forEach(e => domains.add(e.domain))
    result.resolvedEntities.forEach(e => domains.add(e.domain))
    return [...domains].sort()
  }, [result])

  const totalMatches = result
    ? result.summary.floorMatchCount + result.summary.areaMatchCount + result.summary.entityMatchCount
    : 0

  return (
    <div className="space-y-6">
      {/* Header */}
      <div>
        <h1 className="font-display text-2xl font-bold text-bright tracking-tight flex items-center gap-2">
          <Zap size={22} className="text-amber" />
          Matcher Debug
        </h1>
        <p className="text-sm text-fog mt-1">
          Hierarchical search — walks floors → areas → entities, showing hybrid scores at every level.
        </p>
      </div>

      {/* Search bar */}
      <div className="bg-basalt border border-stone rounded-xl p-4 space-y-3">
        <form
          onSubmit={e => { e.preventDefault(); doSearch() }}
          className="flex gap-2"
        >
          <div className="relative flex-1">
            <Search size={16} className="absolute left-3 top-1/2 -translate-y-1/2 text-dust" />
            <input
              type="text"
              value={query}
              onChange={e => setQuery(e.target.value)}
              placeholder="Search term (e.g. 'downstairs', 'kitchen lights', 'bedroom')"
              className="w-full bg-obsidian border border-stone rounded-lg pl-10 pr-4 py-2.5 text-sm text-cloud placeholder:text-dust focus:outline-none focus:border-amber/50 focus:ring-1 focus:ring-amber/20 transition"
            />
          </div>
          <button
            type="submit"
            disabled={loading || !query.trim()}
            className="px-5 py-2.5 bg-amber/15 text-amber font-medium text-sm rounded-lg hover:bg-amber/25 disabled:opacity-40 disabled:cursor-not-allowed transition flex items-center gap-2"
          >
            {loading ? <Loader2 size={14} className="animate-spin" /> : <Search size={14} />}
            Search
          </button>
        </form>

        {/* Tuning options toggle */}
        <button
          onClick={() => setShowOptions(o => !o)}
          className="flex items-center gap-1.5 text-xs text-dust hover:text-fog transition"
        >
          <SlidersHorizontal size={12} />
          Tuning options
          {showOptions ? <ChevronUp size={12} /> : <ChevronDown size={12} />}
        </button>

        {showOptions && (
          <div className="space-y-3 pt-1">
            <div className="grid grid-cols-5 gap-4">
              <label className="space-y-1">
                <span className="text-xs text-fog">Threshold</span>
                <input
                  type="number" step="0.05" min="0" max="1" value={threshold}
                  onChange={e => setThreshold(+e.target.value)}
                  className="w-full bg-obsidian border border-stone rounded-md px-3 py-1.5 text-sm text-cloud font-mono focus:outline-none focus:border-amber/50 transition"
                />
              </label>
              <label className="space-y-1">
                <span className="text-xs text-fog">Embedding weight</span>
                <input
                  type="number" step="0.05" min="0" max="1" value={embeddingWeight}
                  onChange={e => setEmbeddingWeight(+e.target.value)}
                  className="w-full bg-obsidian border border-stone rounded-md px-3 py-1.5 text-sm text-cloud font-mono focus:outline-none focus:border-amber/50 transition"
                />
              </label>
              <label className="space-y-1">
                <span className="text-xs text-fog">Score dropoff</span>
                <input
                  type="number" step="0.05" min="0" max="1" value={dropoff}
                  onChange={e => setDropoff(+e.target.value)}
                  className="w-full bg-obsidian border border-stone rounded-md px-3 py-1.5 text-sm text-cloud font-mono focus:outline-none focus:border-amber/50 transition"
                />
              </label>
              <label className="space-y-1">
                <span className="text-xs text-fog">Disagreement penalty</span>
                <input
                  type="number" step="0.05" min="0" max="1" value={disagreementPenalty}
                  onChange={e => setDisagreementPenalty(+e.target.value)}
                  className="w-full bg-obsidian border border-stone rounded-md px-3 py-1.5 text-sm text-cloud font-mono focus:outline-none focus:border-amber/50 transition"
                />
              </label>
              <label className="space-y-1">
                <span className="text-xs text-fog">Embed. resolution</span>
                <input
                  type="number" step="0.05" min="0" max="1" value={embeddingResolutionMargin}
                  onChange={e => setEmbeddingResolutionMargin(+e.target.value)}
                  className="w-full bg-obsidian border border-stone rounded-md px-3 py-1.5 text-sm text-cloud font-mono focus:outline-none focus:border-amber/50 transition"
                />
              </label>
            </div>

            {/* Domain filter */}
            <div className="space-y-1.5">
              <div className="flex items-center gap-2">
                <span className="text-xs text-fog">Domain filter</span>
                {selectedDomains.length > 0 && (
                  <button
                    type="button"
                    onClick={() => setSelectedDomains([])}
                    className="text-[10px] text-dust hover:text-fog transition"
                  >
                    clear all
                  </button>
                )}
              </div>
              <div className="flex flex-wrap gap-1.5">
                {COMMON_DOMAINS.map(d => {
                  const active = selectedDomains.includes(d)
                  return (
                    <button
                      key={d}
                      type="button"
                      onClick={() => toggleDomain(d)}
                      className={`inline-flex items-center gap-1 px-2 py-1 rounded-md text-xs font-mono transition ${
                        active
                          ? 'bg-amber/20 text-amber border border-amber/40'
                          : 'bg-obsidian text-dust border border-stone hover:border-fog/30 hover:text-fog'
                      }`}
                    >
                      {d}
                      {active && <X size={10} />}
                    </button>
                  )
                })}
              </div>
              {availableDomains.filter(d => !COMMON_DOMAINS.includes(d)).length > 0 && (
                <div className="flex flex-wrap gap-1.5 pt-0.5">
                  {availableDomains.filter(d => !COMMON_DOMAINS.includes(d)).map(d => {
                    const active = selectedDomains.includes(d)
                    return (
                      <button
                        key={d}
                        type="button"
                        onClick={() => toggleDomain(d)}
                        className={`inline-flex items-center gap-1 px-2 py-1 rounded-md text-xs font-mono transition ${
                          active
                            ? 'bg-sage/20 text-sage border border-sage/40'
                            : 'bg-obsidian text-dust border border-stone hover:border-fog/30 hover:text-fog'
                        }`}
                      >
                        {d}
                        {active && <X size={10} />}
                      </button>
                    )
                  })}
                </div>
              )}
            </div>
          </div>
        )}
      </div>

      {error && (
        <div className="bg-ember/10 border border-ember/30 rounded-lg px-4 py-3 text-sm text-rose">
          {error}
        </div>
      )}

      {/* Results */}
      {result && (
        <div className="space-y-4">
          {/* Summary strip */}
          <div className="flex items-center gap-3 text-xs text-fog flex-wrap">
            <span className="font-display font-semibold text-cloud text-sm">
              &ldquo;{result.query}&rdquo;
            </span>
            <span className="text-dust">·</span>
            <span>{totalMatches} direct match{totalMatches !== 1 ? 'es' : ''}</span>
            <span className="text-dust">·</span>
            <span>{result.summary.resolvedEntityCount} resolved entit{result.summary.resolvedEntityCount !== 1 ? 'ies' : 'y'}</span>
            {result.options.domainFilter.length > 0 && (
              <>
                <span className="text-dust">·</span>
                <span className="text-amber">
                  domains: {result.options.domainFilter.join(', ')}
                </span>
              </>
            )}
            <span className="ml-auto font-mono text-dust">
              T={result.options.threshold} W={result.options.embeddingWeight} D={result.options.scoreDropoffRatio} P={result.options.disagreementPenalty} M={result.options.embeddingResolutionMargin}
            </span>
          </div>

          {/* Resolution strategy panel */}
          <div className="bg-basalt border border-stone rounded-xl p-4 space-y-3">
            <div className="flex items-center gap-2 mb-1">
              <div className="w-7 h-7 rounded-md flex items-center justify-center bg-rose/15 text-rose">
                <Route size={14} />
              </div>
              <h3 className="font-display text-sm font-semibold text-cloud tracking-wide">Resolution Strategy</h3>
              {(() => {
                const s = STRATEGY_STYLES[result.resolution.strategy] ?? STRATEGY_STYLES.None
                const Icon = s.icon
                return (
                  <span className={`ml-2 inline-flex items-center gap-1.5 px-2.5 py-1 rounded-md text-xs font-semibold border ${s.bg} ${s.text}`}>
                    <Icon size={12} />
                    {result.resolution.strategy}
                  </span>
                )
              })()}
            </div>

            {/* Reason */}
            <div className="flex items-start gap-2 bg-obsidian rounded-lg px-3 py-2.5">
              <Info size={14} className="text-fog shrink-0 mt-0.5" />
              <p className="text-xs text-mist leading-relaxed font-mono">{result.resolution.reason}</p>
            </div>

            {/* Best embedding similarities comparison */}
            {(result.resolution.bestEntityScore !== null || result.resolution.bestAreaScore !== null || result.resolution.bestFloorScore !== null) && (
              <div className="grid grid-cols-3 gap-3">
                {[
                  { label: 'Best Floor Score', value: result.resolution.bestFloorScore, color: 'amber' },
                  { label: 'Best Area Score', value: result.resolution.bestAreaScore, color: 'sage' },
                  { label: 'Best Entity Score', value: result.resolution.bestEntityScore, color: 'sky-400' },
                ].map(({ label, value, color }) => (
                  <div key={label} className="bg-obsidian rounded-lg p-3 space-y-1.5">
                    <span className="text-[10px] text-dust uppercase tracking-wider">{label}</span>
                    {value !== null ? (
                      <div className="space-y-1">
                        <span className={`block font-mono text-lg tabular-nums text-${color}`}>
                          {value.toFixed(4)}
                        </span>
                        <div className="h-1 bg-stone rounded-full overflow-hidden">
                          <div
                            className={`h-full rounded-full bg-${color}`}
                            style={{ width: `${Math.round(value * 100)}%` }}
                          />
                        </div>
                      </div>
                    ) : (
                      <span className="block font-mono text-lg text-dust">—</span>
                    )}
                  </div>
                ))}
              </div>
            )}
          </div>

          {/* Floor matches */}
          {result.floors.length > 0 && (
            <div className="bg-basalt border border-stone rounded-xl p-4">
              <SectionHeader icon={Layers} label="Floor Matches" count={result.floors.length} color="bg-amber/15 text-amber" />
              <div className="space-y-3">
                {result.floors.map(f => (
                  <div key={f.floorId} className="bg-obsidian rounded-lg p-3 space-y-2">
                    <div className="flex items-center gap-2">
                      <span className="font-medium text-sm text-bright">{f.name}</span>
                      <span className="text-xs text-dust font-mono">{f.floorId}</span>
                      {f.level !== null && (
                        <span className="text-xs text-fog ml-auto">Level {f.level}</span>
                      )}
                    </div>
                    {f.aliases.length > 0 && (
                      <div className="flex gap-1 flex-wrap">
                        {f.aliases.map(a => (
                          <span key={a} className="bg-stone/60 text-dust text-[10px] px-1.5 py-0.5 rounded">{a}</span>
                        ))}
                      </div>
                    )}
                    <ScoreBar score={f.hybridScore} label="Hybrid" accent />
                    <ScoreBar score={f.embeddingSimilarity} label="Embedding" />
                  </div>
                ))}
              </div>
            </div>
          )}

          {/* Area matches */}
          {result.areas.length > 0 && (
            <div className="bg-basalt border border-stone rounded-xl p-4">
              <SectionHeader icon={Building2} label="Area Matches" count={result.areas.length} color="bg-sage/15 text-sage" />
              <div className="space-y-3">
                {result.areas.map(a => (
                  <div key={a.areaId} className="bg-obsidian rounded-lg p-3 space-y-2">
                    <div className="flex items-center gap-2">
                      <span className="font-medium text-sm text-bright">{a.name}</span>
                      <span className="text-xs text-dust font-mono">{a.areaId}</span>
                      {a.floorId && (
                        <span className="text-xs text-fog ml-auto">Floor: {a.floorId}</span>
                      )}
                    </div>
                    {a.aliases.length > 0 && (
                      <div className="flex gap-1 flex-wrap">
                        {a.aliases.map(al => (
                          <span key={al} className="bg-stone/60 text-dust text-[10px] px-1.5 py-0.5 rounded">{al}</span>
                        ))}
                      </div>
                    )}
                    <ScoreBar score={a.hybridScore} label="Hybrid" accent />
                    <ScoreBar score={a.embeddingSimilarity} label="Embedding" />
                  </div>
                ))}
              </div>
            </div>
          )}

          {/* Entity matches */}
          {result.entities.length > 0 && (
            <div className="bg-basalt border border-stone rounded-xl p-4">
              <SectionHeader icon={Hash} label="Entity Matches" count={result.entities.length} color="bg-sky-400/15 text-sky-400" />
              <div className="overflow-x-auto">
                <table className="w-full text-sm">
                  <thead>
                    <tr className="text-left text-xs text-dust border-b border-stone">
                      <th className="pb-2 pr-3 font-medium">Entity</th>
                      <th className="pb-2 pr-3 font-medium">Domain</th>
                      <th className="pb-2 pr-3 font-medium">Area</th>
                      <th className="pb-2 pr-3 font-medium text-right">Hybrid</th>
                      <th className="pb-2 font-medium text-right">Embedding</th>
                    </tr>
                  </thead>
                  <tbody>
                    {result.entities.map(e => (
                      <tr key={e.entityId} className="border-b border-stone/50 hover:bg-obsidian/60">
                        <td className="py-2 pr-3">
                          <div className="text-cloud">{e.friendlyName}</div>
                          <div className="text-[10px] text-dust font-mono">{e.entityId}</div>
                          {e.aliases.length > 0 && (
                            <div className="flex gap-1 flex-wrap mt-0.5">
                              {e.aliases.map(a => (
                                <span key={a} className="bg-stone/60 text-dust text-[10px] px-1 py-0 rounded">{a}</span>
                              ))}
                            </div>
                          )}
                        </td>
                        <td className="py-2 pr-3 text-xs text-fog">{e.domain}</td>
                        <td className="py-2 pr-3 text-xs text-fog">{e.areaName ?? '—'}</td>
                        <td className="py-2 pr-3 text-right font-mono tabular-nums text-amber">{e.hybridScore.toFixed(4)}</td>
                        <td className="py-2 text-right font-mono tabular-nums text-mist">{e.embeddingSimilarity.toFixed(4)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          )}

          {/* Resolved entities from hierarchy walk */}
          {result.resolvedEntities.length > 0 && (
            <div className="bg-basalt border border-stone rounded-xl p-4">
              <SectionHeader icon={MapPin} label="Resolved from Hierarchy" count={result.resolvedEntities.length} color="bg-dust/30 text-fog" />
              <p className="text-xs text-dust mb-3 -mt-1">
                Entities belonging to matched floors/areas — not scored directly.
              </p>
              <div className="overflow-x-auto">
                <table className="w-full text-sm">
                  <thead>
                    <tr className="text-left text-xs text-dust border-b border-stone">
                      <th className="pb-2 pr-3 font-medium">Entity</th>
                      <th className="pb-2 pr-3 font-medium">Domain</th>
                      <th className="pb-2 font-medium">Area</th>
                    </tr>
                  </thead>
                  <tbody>
                    {result.resolvedEntities.map(e => (
                      <tr key={e.entityId} className="border-b border-stone/50 hover:bg-obsidian/60">
                        <td className="py-1.5 pr-3">
                          <span className="text-cloud">{e.friendlyName}</span>
                          <span className="ml-2 text-[10px] text-dust font-mono">{e.entityId}</span>
                        </td>
                        <td className="py-1.5 pr-3 text-xs text-fog">{e.domain}</td>
                        <td className="py-1.5 text-xs text-fog">{e.areaName ?? '—'}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          )}

          {/* No results state */}
          {totalMatches === 0 && result.resolvedEntities.length === 0 && (
            <div className="bg-basalt border border-stone rounded-xl p-8 text-center">
              <Search size={32} className="text-dust mx-auto mb-3" />
              <p className="text-fog text-sm">No matches found for &ldquo;{result.query}&rdquo;</p>
              <p className="text-dust text-xs mt-1">Try lowering the threshold or adjusting the embedding weight.</p>
            </div>
          )}
        </div>
      )}
    </div>
  )
}
