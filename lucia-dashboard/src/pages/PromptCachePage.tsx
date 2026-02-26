import { useState, useEffect, useCallback } from 'react'
import {
  fetchPromptCacheEntries,
  fetchPromptCacheStats,
  evictPromptCacheEntry,
  evictAllPromptCache,
  fetchChatCacheEntries,
  fetchChatCacheStats,
  evictChatCacheEntry,
  evictAllChatCache,
} from '../api'
import { Database, Target, Crosshair, X, Loader2, Trash2, Route, Bot } from 'lucide-react'

interface PromptCacheEntry {
  cacheKey: string
  normalizedPrompt: string
  agentId: string
  confidence: number
  reasoning: string | null
  hitCount: number
  createdAt: string
  lastHitAt: string | null
}

interface ChatCacheEntry {
  cacheKey: string
  normalizedPrompt: string
  responseText: string | null
  functionCalls: { callId: string; name: string; argumentsJson: string | null }[] | null
  modelId: string | null
  hitCount: number
  createdAt: string
  lastHitAt: string
}

interface PromptCacheStats {
  totalEntries: number
  hitRate: number
  totalHits: number
  totalMisses: number
}

type CacheTab = 'routing' | 'agent'

function formatDate(iso: string | null) {
  if (!iso) return '—'
  return new Date(iso).toLocaleString()
}

function truncate(text: string, max: number) {
  return text.length > max ? text.slice(0, max) + '…' : text
}

function StatsBar({ stats, label }: { stats: PromptCacheStats | null; label: string }) {
  if (!stats) return null
  return (
    <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
      <div className="glass-panel rounded-xl p-4">
        <div className="mb-1 flex items-center gap-1.5">
          <Database className="h-3.5 w-3.5 text-fog" />
          <span className="text-xs font-medium uppercase tracking-wider text-dust">{label} Entries</span>
        </div>
        <p className="font-display text-2xl font-bold text-light">{stats.totalEntries}</p>
      </div>
      <div className="glass-panel rounded-xl p-4">
        <div className="mb-1 flex items-center gap-1.5">
          <Target className="h-3.5 w-3.5 text-amber" />
          <span className="text-xs font-medium uppercase tracking-wider text-dust">Hit Rate</span>
        </div>
        <p className="font-display text-2xl font-bold text-amber">
          {(stats.hitRate * 100).toFixed(1)}%
        </p>
      </div>
      <div className="glass-panel rounded-xl p-4">
        <div className="mb-1 flex items-center gap-1.5">
          <Crosshair className="h-3.5 w-3.5 text-sage" />
          <span className="text-xs font-medium uppercase tracking-wider text-dust">Total Hits</span>
        </div>
        <p className="font-display text-2xl font-bold text-sage">{stats.totalHits}</p>
      </div>
      <div className="glass-panel rounded-xl p-4">
        <div className="mb-1 flex items-center gap-1.5">
          <X className="h-3.5 w-3.5 text-rose" />
          <span className="text-xs font-medium uppercase tracking-wider text-dust">Total Misses</span>
        </div>
        <p className="font-display text-2xl font-bold text-rose">{stats.totalMisses}</p>
      </div>
    </div>
  )
}

export default function PromptCachePage() {
  const [tab, setTab] = useState<CacheTab>('routing')
  const [routingEntries, setRoutingEntries] = useState<PromptCacheEntry[]>([])
  const [chatEntries, setChatEntries] = useState<ChatCacheEntry[]>([])
  const [routingStats, setRoutingStats] = useState<PromptCacheStats | null>(null)
  const [chatStats, setChatStats] = useState<PromptCacheStats | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const loadData = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      const [rEntries, rStats, cEntries, cStats] = await Promise.all([
        fetchPromptCacheEntries(),
        fetchPromptCacheStats(),
        fetchChatCacheEntries(),
        fetchChatCacheStats(),
      ])
      setRoutingEntries(
        [...rEntries].sort((a: PromptCacheEntry, b: PromptCacheEntry) =>
          new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime()
        )
      )
      setChatEntries(
        [...cEntries].sort((a: ChatCacheEntry, b: ChatCacheEntry) =>
          new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime()
        )
      )
      setRoutingStats(rStats)
      setChatStats(cStats)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load cache data')
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    loadData()
  }, [loadData])

  async function handleEvictRouting(key: string) {
    try {
      await evictPromptCacheEntry(key)
      await loadData()
    } catch {
      setError('Failed to evict entry')
    }
  }

  async function handleClearAllRouting() {
    if (!confirm('Clear all routing cache entries?')) return
    try {
      await evictAllPromptCache()
      await loadData()
    } catch {
      setError('Failed to clear routing cache')
    }
  }

  async function handleEvictChat(key: string) {
    try {
      await evictChatCacheEntry(key)
      await loadData()
    } catch {
      setError('Failed to evict entry')
    }
  }

  async function handleClearAllChat() {
    if (!confirm('Clear all agent cache entries?')) return
    try {
      await evictAllChatCache()
      await loadData()
    } catch {
      setError('Failed to clear agent cache')
    }
  }

  const stats = tab === 'routing' ? routingStats : chatStats

  return (
    <div className="space-y-6">
      <h1 className="font-display text-2xl font-bold text-light">Prompt Cache</h1>

      {/* Tab Switcher */}
      <div className="flex gap-2">
        <button
          onClick={() => setTab('routing')}
          className={`flex items-center gap-1.5 rounded-xl px-4 py-2 text-sm font-medium transition-colors ${
            tab === 'routing'
              ? 'bg-amber/15 text-amber'
              : 'bg-basalt text-dust hover:text-fog'
          }`}
        >
          <Route className="h-4 w-4" /> Router Cache
          {routingStats && <span className="ml-1 text-xs opacity-70">({routingStats.totalEntries})</span>}
        </button>
        <button
          onClick={() => setTab('agent')}
          className={`flex items-center gap-1.5 rounded-xl px-4 py-2 text-sm font-medium transition-colors ${
            tab === 'agent'
              ? 'bg-amber/15 text-amber'
              : 'bg-basalt text-dust hover:text-fog'
          }`}
        >
          <Bot className="h-4 w-4" /> Agent Cache
          {chatStats && <span className="ml-1 text-xs opacity-70">({chatStats.totalEntries})</span>}
        </button>
      </div>

      {/* Stats bar */}
      <StatsBar stats={stats} label={tab === 'routing' ? 'Router' : 'Agent'} />

      {/* Loading / Error */}
      {loading && <p className="flex items-center gap-2 text-dust"><Loader2 className="h-4 w-4 animate-spin" /> Loading cache entries…</p>}
      {error && <p className="text-rose">{error}</p>}

      {/* Routing table */}
      {!loading && tab === 'routing' && (
        <>
          <div className="flex items-center justify-between">
            <h2 className="font-display text-lg font-semibold text-light">Routing Decisions</h2>
            <button
              onClick={handleClearAllRouting}
              disabled={routingEntries.length === 0}
              className="flex items-center gap-1.5 rounded-xl bg-ember/15 px-4 py-2 text-sm font-medium text-rose transition-colors hover:bg-ember/25 disabled:opacity-40"
            >
              <Trash2 className="h-4 w-4" /> Clear All
            </button>
          </div>
          <div className="glass-panel overflow-x-auto rounded-xl">
            <table className="w-full text-left text-sm">
              <thead className="border-b border-stone text-xs font-medium uppercase tracking-wider text-dust">
                <tr>
                  <th className="px-4 py-3">Prompt</th>
                  <th className="px-4 py-3">Routed Agent</th>
                  <th className="px-4 py-3">Confidence</th>
                  <th className="px-4 py-3">Hit Count</th>
                  <th className="px-4 py-3">Created At</th>
                  <th className="px-4 py-3">Last Hit At</th>
                  <th className="px-4 py-3">Actions</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-stone/50">
                {routingEntries.map((entry) => (
                  <tr key={entry.cacheKey} className="transition-colors hover:bg-basalt/60">
                    <td className="px-4 py-3 text-fog" title={entry.normalizedPrompt}>
                      {truncate(entry.normalizedPrompt, 80)}
                    </td>
                    <td className="whitespace-nowrap px-4 py-3">
                      <span className="rounded-md bg-amber/15 px-2 py-0.5 text-xs font-medium text-amber">
                        {entry.agentId}
                      </span>
                    </td>
                    <td className="whitespace-nowrap px-4 py-3 text-fog">
                      {(entry.confidence * 100).toFixed(0)}%
                    </td>
                    <td className="whitespace-nowrap px-4 py-3 text-fog">
                      {entry.hitCount}
                    </td>
                    <td className="whitespace-nowrap px-4 py-3 text-dust">
                      {formatDate(entry.createdAt)}
                    </td>
                    <td className="whitespace-nowrap px-4 py-3 text-dust">
                      {formatDate(entry.lastHitAt)}
                    </td>
                    <td className="px-4 py-3">
                      <button
                        onClick={() => handleEvictRouting(entry.cacheKey)}
                        className="rounded-lg border border-stone bg-basalt px-2.5 py-1 text-xs font-medium text-rose transition-colors hover:bg-ember/15"
                      >
                        Evict
                      </button>
                    </td>
                  </tr>
                ))}
                {routingEntries.length === 0 && (
                  <tr>
                    <td colSpan={7} className="px-4 py-12 text-center text-dust">
                      No routing cache entries found.
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        </>
      )}

      {/* Agent chat cache table */}
      {!loading && tab === 'agent' && (
        <>
          <div className="flex items-center justify-between">
            <h2 className="font-display text-lg font-semibold text-light">Agent Responses</h2>
            <button
              onClick={handleClearAllChat}
              disabled={chatEntries.length === 0}
              className="flex items-center gap-1.5 rounded-xl bg-ember/15 px-4 py-2 text-sm font-medium text-rose transition-colors hover:bg-ember/25 disabled:opacity-40"
            >
              <Trash2 className="h-4 w-4" /> Clear All
            </button>
          </div>
          <div className="glass-panel overflow-x-auto rounded-xl">
            <table className="w-full text-left text-sm">
              <thead className="border-b border-stone text-xs font-medium uppercase tracking-wider text-dust">
                <tr>
                  <th className="px-4 py-3">Prompt</th>
                  <th className="px-4 py-3">Response</th>
                  <th className="px-4 py-3">Model</th>
                  <th className="px-4 py-3">Tool Calls</th>
                  <th className="px-4 py-3">Hit Count</th>
                  <th className="px-4 py-3">Created At</th>
                  <th className="px-4 py-3">Last Hit At</th>
                  <th className="px-4 py-3">Actions</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-stone/50">
                {chatEntries.map((entry) => (
                  <tr key={entry.cacheKey} className="transition-colors hover:bg-basalt/60">
                    <td className="px-4 py-3 text-fog" title={entry.normalizedPrompt}>
                      {truncate(entry.normalizedPrompt, 60)}
                    </td>
                    <td className="px-4 py-3 text-fog" title={entry.responseText ?? ''}>
                      {entry.responseText ? truncate(entry.responseText, 60) : '—'}
                    </td>
                    <td className="whitespace-nowrap px-4 py-3">
                      {entry.modelId ? (
                        <span className="rounded-md bg-sage/15 px-2 py-0.5 text-xs font-medium text-sage">
                          {entry.modelId}
                        </span>
                      ) : '—'}
                    </td>
                    <td className="whitespace-nowrap px-4 py-3 text-fog">
                      {entry.functionCalls?.length ?? 0}
                    </td>
                    <td className="whitespace-nowrap px-4 py-3 text-fog">
                      {entry.hitCount}
                    </td>
                    <td className="whitespace-nowrap px-4 py-3 text-dust">
                      {formatDate(entry.createdAt)}
                    </td>
                    <td className="whitespace-nowrap px-4 py-3 text-dust">
                      {formatDate(entry.lastHitAt)}
                    </td>
                    <td className="px-4 py-3">
                      <button
                        onClick={() => handleEvictChat(entry.cacheKey)}
                        className="rounded-lg border border-stone bg-basalt px-2.5 py-1 text-xs font-medium text-rose transition-colors hover:bg-ember/15"
                      >
                        Evict
                      </button>
                    </td>
                  </tr>
                ))}
                {chatEntries.length === 0 && (
                  <tr>
                    <td colSpan={8} className="px-4 py-12 text-center text-dust">
                      No agent cache entries found.
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        </>
      )}
    </div>
  )
}
