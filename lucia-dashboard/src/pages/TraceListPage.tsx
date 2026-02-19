import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useNavigate } from 'react-router-dom'
import { fetchTraces, fetchStats } from '../api'
import { LabelStatus } from '../types'
import type { ConversationTrace } from '../types'

function labelBadge(status: number) {
  switch (status) {
    case LabelStatus.Positive:
      return <span className="rounded-full bg-green-500/20 px-2 py-0.5 text-xs font-medium text-green-400">Positive</span>
    case LabelStatus.Negative:
      return <span className="rounded-full bg-red-500/20 px-2 py-0.5 text-xs font-medium text-red-400">Negative</span>
    default:
      return <span className="rounded-full bg-gray-500/20 px-2 py-0.5 text-xs font-medium text-gray-400">Unlabeled</span>
  }
}

function formatDate(iso: string) {
  return new Date(iso).toLocaleString()
}

function truncate(text: string, max: number) {
  return text.length > max ? text.slice(0, max) + '…' : text
}

export default function TraceListPage() {
  const navigate = useNavigate()
  const [page, setPage] = useState(1)
  const [search, setSearch] = useState('')
  const [labelFilter, setLabelFilter] = useState('')
  const [fromDate, setFromDate] = useState('')
  const [toDate, setToDate] = useState('')

  const params: Record<string, string> = { page: String(page), pageSize: '20' }
  if (search) params.search = search
  if (labelFilter) params.labelFilter = labelFilter
  if (fromDate) params.fromDate = fromDate
  if (toDate) params.toDate = toDate

  const { data: traces, isLoading, isError } = useQuery({
    queryKey: ['traces', params],
    queryFn: () => fetchTraces(params),
  })

  const { data: stats } = useQuery({
    queryKey: ['stats'],
    queryFn: fetchStats,
  })

  function agentBadges(trace: ConversationTrace) {
    return trace.agentExecutions.map((exec) => (
      <span
        key={exec.agentId}
        className="mr-1 rounded bg-indigo-500/20 px-1.5 py-0.5 text-xs text-indigo-300"
      >
        {exec.agentId}
      </span>
    ))
  }

  return (
    <div className="space-y-6">
      {/* Stats bar */}
      {stats && (
        <div className="flex flex-wrap gap-3">
          <div className="rounded-lg bg-gray-800 px-4 py-2">
            <span className="text-xs text-gray-400">Total</span>
            <p className="text-lg font-semibold">{stats.totalTraces}</p>
          </div>
          <div className="rounded-lg bg-gray-800 px-4 py-2">
            <span className="text-xs text-green-400">Positive</span>
            <p className="text-lg font-semibold text-green-400">{stats.positiveCount}</p>
          </div>
          <div className="rounded-lg bg-gray-800 px-4 py-2">
            <span className="text-xs text-red-400">Negative</span>
            <p className="text-lg font-semibold text-red-400">{stats.negativeCount}</p>
          </div>
          <div className="rounded-lg bg-gray-800 px-4 py-2">
            <span className="text-xs text-gray-400">Unlabeled</span>
            <p className="text-lg font-semibold text-gray-400">{stats.unlabeledCount}</p>
          </div>
          <div className="rounded-lg bg-gray-800 px-4 py-2">
            <span className="text-xs text-yellow-400">Errored</span>
            <p className="text-lg font-semibold text-yellow-400">{stats.erroredCount}</p>
          </div>
        </div>
      )}

      {/* Filters */}
      <div className="flex flex-wrap items-end gap-3">
        <div>
          <label className="mb-1 block text-xs text-gray-400">Search</label>
          <input
            type="text"
            value={search}
            onChange={(e) => { setSearch(e.target.value); setPage(1) }}
            placeholder="Search user input…"
            className="rounded border border-gray-600 bg-gray-800 px-3 py-1.5 text-sm text-white placeholder-gray-500 focus:border-indigo-500 focus:outline-none"
          />
        </div>
        <div>
          <label className="mb-1 block text-xs text-gray-400">Label</label>
          <select
            value={labelFilter}
            onChange={(e) => { setLabelFilter(e.target.value); setPage(1) }}
            className="rounded border border-gray-600 bg-gray-800 px-3 py-1.5 text-sm text-white focus:border-indigo-500 focus:outline-none"
          >
            <option value="">All</option>
            <option value="0">Unlabeled</option>
            <option value="1">Positive</option>
            <option value="2">Negative</option>
          </select>
        </div>
        <div>
          <label className="mb-1 block text-xs text-gray-400">From</label>
          <input
            type="date"
            value={fromDate}
            onChange={(e) => { setFromDate(e.target.value); setPage(1) }}
            className="rounded border border-gray-600 bg-gray-800 px-3 py-1.5 text-sm text-white focus:border-indigo-500 focus:outline-none"
          />
        </div>
        <div>
          <label className="mb-1 block text-xs text-gray-400">To</label>
          <input
            type="date"
            value={toDate}
            onChange={(e) => { setToDate(e.target.value); setPage(1) }}
            className="rounded border border-gray-600 bg-gray-800 px-3 py-1.5 text-sm text-white focus:border-indigo-500 focus:outline-none"
          />
        </div>
      </div>

      {/* Table */}
      {isLoading && <p className="text-gray-400">Loading traces…</p>}
      {isError && <p className="text-red-400">Failed to load traces.</p>}

      {traces && (
        <>
          <div className="overflow-x-auto rounded-lg border border-gray-700">
            <table className="w-full text-left text-sm">
              <thead className="bg-gray-800 text-xs uppercase text-gray-400">
                <tr>
                  <th className="px-4 py-3">Timestamp</th>
                  <th className="px-4 py-3">User Input</th>
                  <th className="px-4 py-3">Agents</th>
                  <th className="px-4 py-3">Duration</th>
                  <th className="px-4 py-3">Label</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-700">
                {traces.items.map((trace) => (
                  <tr
                    key={trace.id}
                    onClick={() => navigate(`/traces/${trace.id}`)}
                    className="cursor-pointer bg-gray-900 hover:bg-gray-800"
                  >
                    <td className="whitespace-nowrap px-4 py-3 text-gray-300">
                      {formatDate(trace.timestamp)}
                    </td>
                    <td className="px-4 py-3">{truncate(trace.userInput, 80)}</td>
                    <td className="px-4 py-3">{agentBadges(trace)}</td>
                    <td className="whitespace-nowrap px-4 py-3 text-gray-300">
                      {trace.totalDurationMs} ms
                    </td>
                    <td className="px-4 py-3">{labelBadge(trace.label.status)}</td>
                  </tr>
                ))}
                {traces.items.length === 0 && (
                  <tr>
                    <td colSpan={5} className="px-4 py-8 text-center text-gray-500">
                      No traces found.
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>

          {/* Pagination */}
          <div className="flex items-center justify-between text-sm text-gray-400">
            <span>
              Page {traces.page} of {traces.totalPages} ({traces.totalCount} total)
            </span>
            <div className="flex gap-2">
              <button
                disabled={page <= 1}
                onClick={() => setPage((p) => Math.max(1, p - 1))}
                className="rounded bg-gray-800 px-3 py-1 hover:bg-gray-700 disabled:opacity-40"
              >
                Previous
              </button>
              <button
                disabled={page >= traces.totalPages}
                onClick={() => setPage((p) => p + 1)}
                className="rounded bg-gray-800 px-3 py-1 hover:bg-gray-700 disabled:opacity-40"
              >
                Next
              </button>
            </div>
          </div>
        </>
      )}
    </div>
  )
}
