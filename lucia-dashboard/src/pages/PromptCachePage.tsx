import { useState, useEffect, useCallback } from 'react'
import {
  fetchPromptCacheEntries,
  fetchPromptCacheStats,
  evictPromptCacheEntry,
  evictAllPromptCache,
} from '../api'
import { Database, Target, Crosshair, X, Loader2, Trash2 } from 'lucide-react'

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

interface PromptCacheStats {
  totalEntries: number
  hitRate: number
  totalHits: number
  totalMisses: number
}

function formatDate(iso: string | null) {
  if (!iso) return '—'
  return new Date(iso).toLocaleString()
}

function truncate(text: string, max: number) {
  return text.length > max ? text.slice(0, max) + '…' : text
}

export default function PromptCachePage() {
  const [entries, setEntries] = useState<PromptCacheEntry[]>([])
  const [stats, setStats] = useState<PromptCacheStats | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const loadData = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      const [entriesData, statsData] = await Promise.all([
        fetchPromptCacheEntries(),
        fetchPromptCacheStats(),
      ])
      setEntries(entriesData)
      setStats(statsData)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load cache data')
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    loadData()
  }, [loadData])

  async function handleEvict(key: string) {
    try {
      await evictPromptCacheEntry(key)
      await loadData()
    } catch {
      setError('Failed to evict entry')
    }
  }

  async function handleClearAll() {
    if (!confirm('Are you sure you want to clear all cached entries?')) return
    try {
      await evictAllPromptCache()
      await loadData()
    } catch {
      setError('Failed to clear cache')
    }
  }

  return (
    <div className="space-y-6">
      <h1 className="font-display text-2xl font-bold text-light">Prompt Cache</h1>

      {/* Stats bar */}
      {stats && (
        <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
          <div className="glass-panel rounded-xl p-4">
            <div className="mb-1 flex items-center gap-1.5">
              <Database className="h-3.5 w-3.5 text-fog" />
              <span className="text-xs font-medium uppercase tracking-wider text-dust">Total Entries</span>
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
      )}

      {/* Actions */}
      <div className="flex items-center justify-between">
        <h2 className="font-display text-lg font-semibold text-light">Cached Entries</h2>
        <button
          onClick={handleClearAll}
          disabled={entries.length === 0}
          className="flex items-center gap-1.5 rounded-xl bg-ember/15 px-4 py-2 text-sm font-medium text-rose transition-colors hover:bg-ember/25 disabled:opacity-40"
        >
          <Trash2 className="h-4 w-4" /> Clear All
        </button>
      </div>

      {/* Loading / Error */}
      {loading && <p className="flex items-center gap-2 text-dust"><Loader2 className="h-4 w-4 animate-spin" /> Loading cache entries…</p>}
      {error && <p className="text-rose">{error}</p>}

      {/* Table */}
      {!loading && (
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
              {entries.map((entry) => (
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
                      onClick={() => handleEvict(entry.cacheKey)}
                      className="rounded-lg border border-stone bg-basalt px-2.5 py-1 text-xs font-medium text-rose transition-colors hover:bg-ember/15"
                    >
                      Evict
                    </button>
                  </td>
                </tr>
              ))}
              {entries.length === 0 && (
                <tr>
                  <td colSpan={7} className="px-4 py-12 text-center text-dust">
                    No cached entries found.
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}
