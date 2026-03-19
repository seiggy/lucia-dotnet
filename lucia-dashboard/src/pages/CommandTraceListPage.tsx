import { useState, useEffect } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useNavigate } from 'react-router-dom'
import { fetchCommandTraces, fetchCommandTraceStats } from '../api'
import { useCommandTraceStream } from '../hooks/useCommandTraceStream'
import CustomSelect from '../components/CustomSelect'
import type { CommandTrace, CommandTraceOutcome } from '../types'
import { Search, ChevronLeft, ChevronRight, Activity, Zap, Brain, AlertTriangle, Timer, Loader2, Wifi, WifiOff } from 'lucide-react'

function outcomeBadge(outcome: CommandTraceOutcome) {
  switch (outcome) {
    case 'commandHandled':
      return <span className="rounded-full bg-sage/15 px-2 py-0.5 text-xs font-medium text-sage">⚡ Command</span>
    case 'llmFallback':
    case 'llmCompleted':
      return <span className="rounded-full bg-violet-500/15 px-2 py-0.5 text-xs font-medium text-violet-400">🤖 LLM</span>
    case 'error':
      return <span className="rounded-full bg-ember/15 px-2 py-0.5 text-xs font-medium text-rose">Error</span>
    default:
      return <span className="rounded-full bg-stone/50 px-2 py-0.5 text-xs font-medium text-dust">{outcome}</span>
  }
}

function formatDate(iso: string) {
  return new Date(iso).toLocaleString()
}

function truncate(text: string, max: number) {
  return text.length > max ? text.slice(0, max) + '…' : text
}

const inputStyle = 'rounded-xl border border-stone bg-basalt px-3 py-2 text-sm text-light placeholder-dust/60 input-focus transition-colors'

export default function CommandTraceListPage() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [page, setPage] = useState(1)
  const [search, setSearch] = useState('')
  const [outcomeFilter, setOutcomeFilter] = useState('')
  const [skillFilter, setSkillFilter] = useState('')
  const [fromDate, setFromDate] = useState('')
  const [toDate, setToDate] = useState('')

  // SSE live updates
  const { lastTrace, connectionState } = useCommandTraceStream()

  useEffect(() => {
    if (lastTrace) {
      queryClient.invalidateQueries({ queryKey: ['command-traces'] })
      queryClient.invalidateQueries({ queryKey: ['command-trace-stats'] })
    }
  }, [lastTrace, queryClient])

  const params: Record<string, string> = { page: String(page), pageSize: '20' }
  if (search) params.search = search
  if (outcomeFilter) params.outcome = outcomeFilter
  if (skillFilter) params.skill = skillFilter
  if (fromDate) params.fromDate = fromDate
  if (toDate) params.toDate = toDate

  const { data: traces, isLoading, isError } = useQuery({
    queryKey: ['command-traces', params],
    queryFn: () => fetchCommandTraces(params),
  })

  const { data: stats } = useQuery({
    queryKey: ['command-trace-stats'],
    queryFn: fetchCommandTraceStats,
  })

  const outcomeOptions = [
    { value: '', label: 'All' },
    { value: 'CommandHandled', label: 'Command Handled' },
    { value: 'LlmFallback', label: 'LLM Fallback' },
    { value: 'LlmCompleted', label: 'LLM Completed' },
    { value: 'Error', label: 'Error' },
  ]

  const skillOptions = [
    { value: '', label: 'All Skills' },
    ...(stats
      ? Object.keys(stats.bySkill).sort().map((skillId) => ({
          value: skillId,
          label: `${skillId} (${stats.bySkill[skillId]})`,
        }))
      : []),
  ]

  function skillActionLabel(trace: CommandTrace) {
    const skill = trace.match.skillId ?? trace.execution?.skillId
    const action = trace.match.action ?? trace.execution?.action
    if (!skill) return null
    return (
      <span className="rounded-md bg-amber/10 px-1.5 py-0.5 text-xs text-amber">
        {skill}{action ? ` / ${action}` : ''}
      </span>
    )
  }

  return (
    <div className="space-y-6">
      {/* Live indicator */}
      <div className="flex items-center gap-2 text-xs">
        {connectionState === 'connected' ? (
          <span className="flex items-center gap-1 text-sage"><Wifi className="h-3 w-3" /> Live</span>
        ) : connectionState === 'reconnecting' ? (
          <span className="flex items-center gap-1 text-amber"><Wifi className="h-3 w-3 animate-pulse" /> Reconnecting…</span>
        ) : (
          <span className="flex items-center gap-1 text-dust"><WifiOff className="h-3 w-3" /> Disconnected</span>
        )}
      </div>

      {/* Stats bar */}
      {stats && (
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-5">
          <StatCard icon={Activity} label="Total" value={stats.totalCount} color="text-light" />
          <StatCard icon={Zap} label="Command" value={stats.commandHandledCount} color="text-sage" />
          <StatCard icon={Brain} label="LLM Fallback" value={stats.llmFallbackCount} color="text-amber" />
          <StatCard icon={AlertTriangle} label="Errors" value={stats.errorCount} color="text-rose" />
          <StatCard icon={Timer} label="Avg Duration" value={`${Math.round(stats.avgDurationMs)}ms`} color="text-fog" />
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
          <label className="mb-1.5 block text-xs font-medium uppercase tracking-wider text-dust">Outcome</label>
          <CustomSelect
            options={outcomeOptions}
            value={outcomeFilter}
            onChange={(value) => { setOutcomeFilter(value); setPage(1) }}
          />
        </div>
        <div>
          <label className="mb-1.5 block text-xs font-medium uppercase tracking-wider text-dust">Skill</label>
          <CustomSelect
            options={skillOptions}
            value={skillFilter}
            onChange={(value) => { setSkillFilter(value); setPage(1) }}
          />
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
          <span className="text-sm">Loading command traces…</span>
        </div>
      )}
      {isError && <p className="text-sm text-rose">Failed to load command traces.</p>}

      {traces && (
        <>
          <div className="overflow-x-auto rounded-xl border border-stone">
            <table className="w-full text-left text-sm">
              <thead className="border-b border-stone bg-basalt/80">
                <tr>
                  <th className="px-4 py-3 text-xs font-medium uppercase tracking-wider text-dust">Timestamp</th>
                  <th className="px-4 py-3 text-xs font-medium uppercase tracking-wider text-dust">User Input</th>
                  <th className="px-4 py-3 text-xs font-medium uppercase tracking-wider text-dust">Outcome</th>
                  <th className="px-4 py-3 text-xs font-medium uppercase tracking-wider text-dust">Skill / Action</th>
                  <th className="px-4 py-3 text-xs font-medium uppercase tracking-wider text-dust">Confidence</th>
                  <th className="px-4 py-3 text-xs font-medium uppercase tracking-wider text-dust">Duration</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-stone/50">
                {traces.items.map((trace) => (
                  <tr
                    key={trace.id}
                    onClick={() => navigate(`/command-traces/${trace.id}`)}
                    className="cursor-pointer transition-colors hover:bg-basalt/60"
                  >
                    <td className="whitespace-nowrap px-4 py-3 text-dust">
                      {formatDate(trace.timestamp)}
                    </td>
                    <td className="px-4 py-3 text-light">
                      {truncate(trace.cleanText || trace.rawText, 80)}
                    </td>
                    <td className="px-4 py-3">{outcomeBadge(trace.outcome)}</td>
                    <td className="px-4 py-3">{skillActionLabel(trace)}</td>
                    <td className="whitespace-nowrap px-4 py-3 font-mono text-xs text-dust">
                      {(trace.match.confidence * 100).toFixed(1)}%
                    </td>
                    <td className="whitespace-nowrap px-4 py-3 font-mono text-xs text-dust">
                      {trace.totalDurationMs} ms
                    </td>
                  </tr>
                ))}
                {traces.items.length === 0 && (
                  <tr>
                    <td colSpan={6} className="px-4 py-12 text-center text-dust">
                      No command traces found.
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

function StatCard({ icon: Icon, label, value, color }: { icon: typeof Activity; label: string; value: number | string; color: string }) {
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
