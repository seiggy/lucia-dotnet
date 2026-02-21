import { useState, useEffect, useCallback } from 'react'
import {
  fetchPromptCacheEntries,
  fetchPromptCacheStats,
  evictPromptCacheEntry,
  evictAllPromptCache,
} from '../api'

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
      {/* Stats bar */}
      {stats && (
        <div className="flex flex-wrap gap-3">
          <div className="rounded-lg bg-gray-800 px-4 py-2">
            <span className="text-xs text-gray-400">Total Entries</span>
            <p className="text-lg font-semibold">{stats.totalEntries}</p>
          </div>
          <div className="rounded-lg bg-gray-800 px-4 py-2">
            <span className="text-xs text-indigo-400">Hit Rate</span>
            <p className="text-lg font-semibold text-indigo-400">
              {(stats.hitRate * 100).toFixed(1)}%
            </p>
          </div>
          <div className="rounded-lg bg-gray-800 px-4 py-2">
            <span className="text-xs text-green-400">Total Hits</span>
            <p className="text-lg font-semibold text-green-400">{stats.totalHits}</p>
          </div>
          <div className="rounded-lg bg-gray-800 px-4 py-2">
            <span className="text-xs text-red-400">Total Misses</span>
            <p className="text-lg font-semibold text-red-400">{stats.totalMisses}</p>
          </div>
        </div>
      )}

      {/* Actions */}
      <div className="flex items-center justify-between">
        <h2 className="text-lg font-semibold">Cached Entries</h2>
        <button
          onClick={handleClearAll}
          disabled={entries.length === 0}
          className="rounded bg-red-600 px-3 py-1.5 text-sm font-medium hover:bg-red-700 disabled:opacity-40"
        >
          Clear All
        </button>
      </div>

      {/* Loading / Error */}
      {loading && <p className="text-gray-400">Loading cache entries…</p>}
      {error && <p className="text-red-400">{error}</p>}

      {/* Table */}
      {!loading && (
        <div className="overflow-x-auto rounded-lg border border-gray-700">
          <table className="w-full text-left text-sm">
            <thead className="bg-gray-800 text-xs uppercase text-gray-400">
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
            <tbody className="divide-y divide-gray-700">
              {entries.map((entry) => (
                <tr key={entry.cacheKey} className="bg-gray-900 hover:bg-gray-800">
                  <td className="px-4 py-3" title={entry.normalizedPrompt}>
                    {truncate(entry.normalizedPrompt, 80)}
                  </td>
                  <td className="whitespace-nowrap px-4 py-3">
                    <span className="rounded bg-indigo-900/50 px-2 py-0.5 text-xs font-medium text-indigo-300">
                      {entry.agentId}
                    </span>
                  </td>
                  <td className="whitespace-nowrap px-4 py-3 text-gray-300">
                    {(entry.confidence * 100).toFixed(0)}%
                  </td>
                  <td className="whitespace-nowrap px-4 py-3 text-gray-300">
                    {entry.hitCount}
                  </td>
                  <td className="whitespace-nowrap px-4 py-3 text-gray-300">
                    {formatDate(entry.createdAt)}
                  </td>
                  <td className="whitespace-nowrap px-4 py-3 text-gray-300">
                    {formatDate(entry.lastHitAt)}
                  </td>
                  <td className="px-4 py-3">
                    <button
                      onClick={() => handleEvict(entry.cacheKey)}
                      className="rounded bg-gray-700 px-2.5 py-1 text-xs font-medium text-red-400 hover:bg-gray-600"
                    >
                      Evict
                    </button>
                  </td>
                </tr>
              ))}
              {entries.length === 0 && (
                <tr>
                  <td colSpan={7} className="px-4 py-8 text-center text-gray-500">
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
