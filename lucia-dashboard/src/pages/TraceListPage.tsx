import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useNavigate } from 'react-router-dom'
import { fetchTraces, fetchStats } from '../api'
import { LabelStatus } from '../types'
import type { ConversationTrace } from '../types'
import { Search, ChevronLeft, ChevronRight, Activity, ThumbsUp, ThumbsDown, Tag, AlertTriangle, Loader2 } from 'lucide-react'

function labelBadge(status: number) {
  switch (status) {
    case LabelStatus.Positive:
      return <span className="rounded-full bg-sage/15 px-2 py-0.5 text-xs font-medium text-sage">Positive</span>
    case LabelStatus.Negative:
      return <span className="rounded-full bg-ember/15 px-2 py-0.5 text-xs font-medium text-rose">Negative</span>
    default:
      return <span className="rounded-full bg-stone/50 px-2 py-0.5 text-xs font-medium text-dust">Unlabeled</span>
  }
}

function formatDate(iso: string) {
  return new Date(iso).toLocaleString()
}

function truncate(text: string, max: number) {
  return text.length > max ? text.slice(0, max) + '…' : text
}

const inputStyle = 'rounded-xl border border-stone bg-basalt px-3 py-2 text-sm text-light placeholder-dust/60 input-focus transition-colors'
const selectStyle = 'rounded-xl border border-stone bg-basalt px-3 py-2 text-sm text-light input-focus transition-colors appearance-none'

export default function TraceListPage() {
  const navigate = useNavigate()
  const [page, setPage] = useState(1)
  const [search, setSearch] = useState('')
  const [labelFilter, setLabelFilter] = useState('')
  const [agentFilter, setAgentFilter] = useState('')
  const [fromDate, setFromDate] = useState('')
  const [toDate, setToDate] = useState('')

  const params: Record<string, string> = { page: String(page), pageSize: '20' }
  if (search) params.search = search
  if (labelFilter) params.label = labelFilter
  if (agentFilter) params.agent = agentFilter
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
        className="mr-1 rounded-md bg-amber/10 px-1.5 py-0.5 text-xs text-amber"
      >
        {exec.agentId}
      </span>
    ))
  }

  return (
    <div className="space-y-6">
      {/* Stats bar */}
      {stats && (
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-5">
          <StatCard icon={Activity} label="Total" value={stats.totalTraces} color="text-light" />
          <StatCard icon={ThumbsUp} label="Positive" value={stats.positiveCount} color="text-sage" />
          <StatCard icon={ThumbsDown} label="Negative" value={stats.negativeCount} color="text-rose" />
          <StatCard icon={Tag} label="Unlabeled" value={stats.unlabeledCount} color="text-dust" />
          <StatCard icon={AlertTriangle} label="Errored" value={stats.erroredCount} color="text-amber" />
        </div>
      )}

      {/* Filters */}
      <div className="flex flex-wrap items-end gap-3">
        <div className="relative">
          <label className="mb-1.5 block text-xs font-medium uppercase tracking-wider text-dust">Search</label>
          <div className="relative">
            <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-dust" />
            <input
              type="text"
              value={search}
              onChange={(e) => { setSearch(e.target.value); setPage(1) }}
              placeholder="Search user input…"
              className={inputStyle + ' pl-9'}
            />
          </div>
        </div>
        <div>
          <label className="mb-1.5 block text-xs font-medium uppercase tracking-wider text-dust">Label</label>
          <select
            value={labelFilter}
            onChange={(e) => { setLabelFilter(e.target.value); setPage(1) }}
            className={selectStyle}
          >
            <option value="">All</option>
            <option value="0">Unlabeled</option>
            <option value="1">Positive</option>
            <option value="2">Negative</option>
          </select>
        </div>
        <div>
          <label className="mb-1.5 block text-xs font-medium uppercase tracking-wider text-dust">Agent</label>
          <select
            value={agentFilter}
            onChange={(e) => { setAgentFilter(e.target.value); setPage(1) }}
            className={selectStyle}
          >
            <option value="">All Agents</option>
            {stats && Object.keys(stats.byAgent).sort().map((agentId) => (
              <option key={agentId} value={agentId}>
                {agentId} ({stats.byAgent[agentId]})
              </option>
            ))}
          </select>
        </div>
        <div>
          <label className="mb-1.5 block text-xs font-medium uppercase tracking-wider text-dust">From</label>
          <input
            type="date"
            value={fromDate}
            onChange={(e) => { setFromDate(e.target.value); setPage(1) }}
            className={inputStyle}
          />
        </div>
        <div>
          <label className="mb-1.5 block text-xs font-medium uppercase tracking-wider text-dust">To</label>
          <input
            type="date"
            value={toDate}
            onChange={(e) => { setToDate(e.target.value); setPage(1) }}
            className={inputStyle}
          />
        </div>
      </div>

      {/* Table */}
      {isLoading && (
        <div className="flex items-center gap-2 text-fog">
          <Loader2 className="h-4 w-4 animate-spin" />
          <span className="text-sm">Loading traces…</span>
        </div>
      )}
      {isError && <p className="text-sm text-rose">Failed to load traces.</p>}

      {traces && (
        <>
          <div className="overflow-x-auto rounded-xl border border-stone">
            <table className="w-full text-left text-sm">
              <thead className="border-b border-stone bg-basalt/80">
                <tr>
                  <th className="px-4 py-3 text-xs font-medium uppercase tracking-wider text-dust">Timestamp</th>
                  <th className="px-4 py-3 text-xs font-medium uppercase tracking-wider text-dust">User Input</th>
                  <th className="px-4 py-3 text-xs font-medium uppercase tracking-wider text-dust">Agents</th>
                  <th className="px-4 py-3 text-xs font-medium uppercase tracking-wider text-dust">Duration</th>
                  <th className="px-4 py-3 text-xs font-medium uppercase tracking-wider text-dust">Label</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-stone/50">
                {traces.items.map((trace) => (
                  <tr
                    key={trace.id}
                    onClick={() => navigate(`/traces/${trace.id}`)}
                    className="cursor-pointer transition-colors hover:bg-basalt/60"
                  >
                    <td className="whitespace-nowrap px-4 py-3 text-dust">
                      {formatDate(trace.timestamp)}
                    </td>
                    <td className="px-4 py-3 text-light">
                      {trace.conversationHistory && trace.conversationHistory.length > 0 && (
                        <span
                          className="mr-1.5 inline-block rounded-md bg-amber/10 px-1.5 py-0.5 text-xs text-amber"
                          title={`${trace.conversationHistory.length} prior turns`}
                        >
                          {trace.conversationHistory.length}↩
                        </span>
                      )}
                      {truncate(trace.userInput, 80)}
                    </td>
                    <td className="px-4 py-3">{agentBadges(trace)}</td>
                    <td className="whitespace-nowrap px-4 py-3 font-mono text-xs text-dust">
                      {trace.totalDurationMs} ms
                    </td>
                    <td className="px-4 py-3">{labelBadge(trace.label.status)}</td>
                  </tr>
                ))}
                {traces.items.length === 0 && (
                  <tr>
                    <td colSpan={5} className="px-4 py-12 text-center text-dust">
                      No traces found.
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>

          {/* Pagination */}
          <div className="flex items-center justify-between text-sm text-dust">
            <span>
              Page {traces.page} of {traces.totalPages} ({traces.totalCount} total)
            </span>
            <div className="flex gap-2">
              <button
                disabled={page <= 1}
                onClick={() => setPage((p) => Math.max(1, p - 1))}
                className="flex items-center gap-1 rounded-lg border border-stone bg-basalt px-3 py-1.5 text-fog transition-colors hover:border-amber/30 hover:text-light disabled:opacity-40"
              >
                <ChevronLeft className="h-4 w-4" /> Previous
              </button>
              <button
                disabled={page >= traces.totalPages}
                onClick={() => setPage((p) => p + 1)}
                className="flex items-center gap-1 rounded-lg border border-stone bg-basalt px-3 py-1.5 text-fog transition-colors hover:border-amber/30 hover:text-light disabled:opacity-40"
              >
                Next <ChevronRight className="h-4 w-4" />
              </button>
            </div>
          </div>
        </>
      )}
    </div>
  )
}

/* ── Stat Card ──────────────────────────────────────── */

function StatCard({ icon: Icon, label, value, color }: { icon: typeof Activity; label: string; value: number; color: string }) {
  return (
    <div className="glass-panel rounded-xl px-4 py-3">
      <div className="flex items-center gap-2">
        <Icon className={`h-4 w-4 ${color}`} />
        <span className="text-xs font-medium uppercase tracking-wider text-dust">{label}</span>
      </div>
      <p className={`mt-1 font-display text-xl font-semibold ${color}`}>{value}</p>
    </div>
  )
}
