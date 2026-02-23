import { useState, useEffect, useCallback } from 'react'
import {
  fetchActiveTasks,
  fetchArchivedTasks,
  fetchTaskStats,
  cancelTask,
} from '../api'
import type {
  ActiveTaskSummary,
  ArchivedTask,
  CombinedTaskStats,
  PagedResult,
} from '../types'
import { ListTodo, Play, CheckCircle2, XCircle, Ban, Archive, Search, ChevronLeft, ChevronRight, ChevronDown, ChevronUp, Loader2 } from 'lucide-react'

function formatDate(iso: string | null) {
  if (!iso) return '—'
  return new Date(iso).toLocaleString()
}

function truncate(text: string | null | undefined, max: number) {
  if (!text) return '—'
  return text.length > max ? text.slice(0, max) + '…' : text
}

function statusBadge(status: string) {
  const styles: Record<string, string> = {
    Completed: 'bg-sage/15 text-sage',
    Working: 'bg-amber/15 text-amber',
    Submitted: 'bg-amber/25 text-amber-glow',
    InputRequired: 'bg-rose/15 text-rose',
    Failed: 'bg-ember/15 text-rose',
    Canceled: 'bg-stone/50 text-dust',
  }
  const cls = styles[status] ?? 'bg-stone/50 text-dust'
  return (
    <span className={`inline-block rounded-md px-2 py-0.5 text-xs font-medium ${cls}`}>
      {status}
    </span>
  )
}

type Tab = 'active' | 'history'

export default function TasksPage() {
  const [tab, setTab] = useState<Tab>('active')
  const [activeTasks, setActiveTasks] = useState<ActiveTaskSummary[]>([])
  const [archivedResult, setArchivedResult] = useState<PagedResult<ArchivedTask> | null>(null)
  const [stats, setStats] = useState<CombinedTaskStats | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [expandedId, setExpandedId] = useState<string | null>(null)

  const [statusFilter, setStatusFilter] = useState('')
  const [searchFilter, setSearchFilter] = useState('')
  const [page, setPage] = useState(1)

  const loadActive = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      const [tasks, taskStats] = await Promise.all([
        fetchActiveTasks(),
        fetchTaskStats(),
      ])
      setActiveTasks(tasks)
      setStats(taskStats)
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : 'Failed to load active tasks')
    } finally {
      setLoading(false)
    }
  }, [])

  const loadArchived = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      const params: Record<string, string> = {
        page: String(page),
        pageSize: '25',
      }
      if (statusFilter) params.status = statusFilter
      if (searchFilter) params.search = searchFilter

      const [result, taskStats] = await Promise.all([
        fetchArchivedTasks(params),
        fetchTaskStats(),
      ])
      setArchivedResult(result)
      setStats(taskStats)
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : 'Failed to load archived tasks')
    } finally {
      setLoading(false)
    }
  }, [page, statusFilter, searchFilter])

  useEffect(() => {
    if (tab === 'active') loadActive()
    else loadArchived()
  }, [tab, loadActive, loadArchived])

  const handleCancel = async (id: string) => {
    try {
      await cancelTask(id)
      await loadActive()
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : 'Failed to cancel task')
    }
  }

  return (
    <div>
      <h1 className="mb-6 font-display text-2xl font-bold text-light">Tasks</h1>

      {/* Stats bar */}
      {stats && (
        <div className="mb-6 grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-5">
          <div className="glass-panel rounded-xl p-4">
            <div className="mb-1 flex items-center gap-1.5">
              <Play className="h-3.5 w-3.5 text-amber" />
              <span className="text-xs font-medium uppercase tracking-wider text-dust">Active</span>
            </div>
            <div className="font-display text-2xl font-bold text-amber">{stats.activeCount}</div>
          </div>
          <div className="glass-panel rounded-xl p-4">
            <div className="mb-1 flex items-center gap-1.5">
              <CheckCircle2 className="h-3.5 w-3.5 text-sage" />
              <span className="text-xs font-medium uppercase tracking-wider text-dust">Completed</span>
            </div>
            <div className="font-display text-2xl font-bold text-sage">{stats.archived.completedCount}</div>
          </div>
          <div className="glass-panel rounded-xl p-4">
            <div className="mb-1 flex items-center gap-1.5">
              <XCircle className="h-3.5 w-3.5 text-rose" />
              <span className="text-xs font-medium uppercase tracking-wider text-dust">Failed</span>
            </div>
            <div className="font-display text-2xl font-bold text-rose">{stats.archived.failedCount}</div>
          </div>
          <div className="glass-panel rounded-xl p-4">
            <div className="mb-1 flex items-center gap-1.5">
              <Ban className="h-3.5 w-3.5 text-dust" />
              <span className="text-xs font-medium uppercase tracking-wider text-dust">Canceled</span>
            </div>
            <div className="font-display text-2xl font-bold text-fog">{stats.archived.canceledCount}</div>
          </div>
          <div className="glass-panel rounded-xl p-4">
            <div className="mb-1 flex items-center gap-1.5">
              <Archive className="h-3.5 w-3.5 text-fog" />
              <span className="text-xs font-medium uppercase tracking-wider text-dust">Total Archived</span>
            </div>
            <div className="font-display text-2xl font-bold text-light">{stats.archived.totalTasks}</div>
          </div>
        </div>
      )}

      {/* Tab selector */}
      <div className="mb-6 flex gap-1 rounded-xl border border-stone bg-void/60 p-1">
        <button
          className={`flex items-center gap-2 rounded-lg px-4 py-2 text-sm font-medium transition-colors ${tab === 'active' ? 'bg-amber text-void' : 'text-dust hover:text-light'}`}
          onClick={() => setTab('active')}
        >
          <ListTodo className="h-4 w-4" /> Active Tasks
        </button>
        <button
          className={`flex items-center gap-2 rounded-lg px-4 py-2 text-sm font-medium transition-colors ${tab === 'history' ? 'bg-amber text-void' : 'text-dust hover:text-light'}`}
          onClick={() => { setTab('history'); setPage(1) }}
        >
          <Archive className="h-4 w-4" /> Task History
        </button>
      </div>

      {error && (
        <div className="mb-4 rounded-xl border border-ember/30 bg-ember/10 px-4 py-3 text-sm text-rose">{error}</div>
      )}

      {loading ? (
        <div className="flex items-center justify-center gap-2 py-16 text-dust">
          <Loader2 className="h-5 w-5 animate-spin" /> Loading…
        </div>
      ) : tab === 'active' ? (
        <ActiveTasksTable
          tasks={activeTasks}
          expandedId={expandedId}
          onToggle={(id) => setExpandedId(expandedId === id ? null : id)}
          onCancel={handleCancel}
        />
      ) : (
        <ArchivedTasksTable
          result={archivedResult}
          expandedId={expandedId}
          onToggle={(id) => setExpandedId(expandedId === id ? null : id)}
          statusFilter={statusFilter}
          searchFilter={searchFilter}
          onStatusChange={(v) => { setStatusFilter(v); setPage(1) }}
          onSearchChange={(v) => { setSearchFilter(v); setPage(1) }}
          page={page}
          onPageChange={setPage}
        />
      )}
    </div>
  )
}

function ActiveTasksTable({
  tasks,
  expandedId,
  onToggle,
  onCancel,
}: {
  tasks: ActiveTaskSummary[]
  expandedId: string | null
  onToggle: (id: string) => void
  onCancel: (id: string) => void
}) {
  if (tasks.length === 0) {
    return <div className="py-12 text-center text-dust">No active tasks.</div>
  }

  return (
    <div className="glass-panel overflow-hidden rounded-xl">
      <table className="w-full text-left text-sm">
        <thead className="border-b border-stone text-xs font-medium uppercase tracking-wider text-dust">
          <tr>
            <th className="px-4 py-3">Task ID</th>
            <th className="px-4 py-3">Status</th>
            <th className="px-4 py-3">Messages</th>
            <th className="px-4 py-3">User Input</th>
            <th className="px-4 py-3">Last Updated</th>
            <th className="px-4 py-3">Actions</th>
          </tr>
        </thead>
        <tbody>
          {tasks.map((t) => (
            <>
              <tr
                key={t.id}
                className="cursor-pointer border-b border-stone/50 transition-colors hover:bg-basalt/60"
                onClick={() => onToggle(t.id)}
              >
                <td className="px-4 py-3 font-mono text-xs text-fog">{t.id.slice(0, 12)}…</td>
                <td className="px-4 py-3">{statusBadge(t.status)}</td>
                <td className="px-4 py-3 text-fog">{t.messageCount}</td>
                <td className="px-4 py-3 text-fog">{truncate(t.userInput, 80)}</td>
                <td className="px-4 py-3 text-dust">{formatDate(t.lastUpdated)}</td>
                <td className="px-4 py-3">
                  {t.status !== 'Completed' && t.status !== 'Failed' && t.status !== 'Canceled' && (
                    <button
                      className="rounded-lg bg-ember/15 px-2.5 py-1 text-xs font-medium text-rose transition-colors hover:bg-ember/25"
                      onClick={(e) => { e.stopPropagation(); onCancel(t.id) }}
                    >
                      Cancel
                    </button>
                  )}
                  <span className="ml-2 text-dust">
                    {expandedId === t.id ? <ChevronUp className="inline h-4 w-4" /> : <ChevronDown className="inline h-4 w-4" />}
                  </span>
                </td>
              </tr>
              {expandedId === t.id && (
                <tr key={`${t.id}-detail`}>
                  <td colSpan={6} className="border-b border-stone/50 bg-void/30 px-5 py-4">
                    <div className="text-xs text-fog">
                      <strong className="text-dust">Full ID:</strong> <span className="font-mono">{t.id}</span><br />
                      <strong className="text-dust">Context ID:</strong> <span className="font-mono">{t.contextId ?? '—'}</span><br />
                      <strong className="text-dust">User Input:</strong> {t.userInput ?? '—'}
                    </div>
                  </td>
                </tr>
              )}
            </>
          ))}
        </tbody>
      </table>
    </div>
  )
}

function ArchivedTasksTable({
  result,
  expandedId,
  onToggle,
  statusFilter,
  searchFilter,
  onStatusChange,
  onSearchChange,
  page,
  onPageChange,
}: {
  result: PagedResult<ArchivedTask> | null
  expandedId: string | null
  onToggle: (id: string) => void
  statusFilter: string
  searchFilter: string
  onStatusChange: (v: string) => void
  onSearchChange: (v: string) => void
  page: number
  onPageChange: (p: number) => void
}) {
  return (
    <div>
      {/* Filters */}
      <div className="mb-4 flex flex-wrap gap-3">
        <select
          className="rounded-xl border border-stone bg-basalt px-4 py-2 text-sm text-light input-focus appearance-none"
          value={statusFilter}
          onChange={(e) => onStatusChange(e.target.value)}
        >
          <option value="">All Statuses</option>
          <option value="Completed">Completed</option>
          <option value="Failed">Failed</option>
          <option value="Canceled">Canceled</option>
        </select>
        <div className="relative">
          <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-dust" />
          <input
            type="text"
            placeholder="Search user input…"
            className="rounded-xl border border-stone bg-basalt py-2 pl-9 pr-4 text-sm text-light placeholder-dust/60 input-focus"
            value={searchFilter}
            onChange={(e) => onSearchChange(e.target.value)}
          />
        </div>
      </div>

      {!result || result.items.length === 0 ? (
        <div className="py-12 text-center text-dust">No archived tasks found.</div>
      ) : (
        <>
          <div className="glass-panel overflow-hidden rounded-xl">
            <table className="w-full text-left text-sm">
              <thead className="border-b border-stone text-xs font-medium uppercase tracking-wider text-dust">
                <tr>
                  <th className="px-4 py-3">Task ID</th>
                  <th className="px-4 py-3">Status</th>
                  <th className="px-4 py-3">Agents</th>
                  <th className="px-4 py-3">User Input</th>
                  <th className="px-4 py-3">Messages</th>
                  <th className="px-4 py-3">Archived</th>
                </tr>
              </thead>
              <tbody>
                {result.items.map((t) => (
                  <>
                    <tr
                      key={t.id}
                      className="cursor-pointer border-b border-stone/50 transition-colors hover:bg-basalt/60"
                      onClick={() => onToggle(t.id)}
                    >
                      <td className="px-4 py-3 font-mono text-xs text-fog">{t.id.slice(0, 12)}…</td>
                      <td className="px-4 py-3">{statusBadge(t.status)}</td>
                      <td className="px-4 py-3 text-xs text-amber">{t.agentIds.join(', ') || '—'}</td>
                      <td className="px-4 py-3 text-fog">{truncate(t.userInput, 60)}</td>
                      <td className="px-4 py-3 text-fog">{t.messageCount}</td>
                      <td className="px-4 py-3 text-dust">{formatDate(t.archivedAt)}</td>
                    </tr>
                    {expandedId === t.id && (
                      <tr key={`${t.id}-detail`}>
                        <td colSpan={6} className="border-b border-stone/50 bg-void/30 px-5 py-4">
                          <div className="mb-2 text-xs text-fog">
                            <strong className="text-dust">Full ID:</strong> <span className="font-mono">{t.id}</span><br />
                            <strong className="text-dust">Context ID:</strong> <span className="font-mono">{t.contextId ?? '—'}</span><br />
                            <strong className="text-dust">Created:</strong> {formatDate(t.createdAt)}<br />
                            <strong className="text-dust">Final Response:</strong> {truncate(t.finalResponse, 300)}
                          </div>
                          {t.history.length > 0 && (
                            <div className="mt-3">
                              <div className="mb-2 text-xs font-medium uppercase tracking-wider text-dust">Conversation History</div>
                              <div className="max-h-60 space-y-1 overflow-y-auto rounded-lg border border-stone bg-void/50 p-2">
                                {t.history.map((m, i) => (
                                  <div key={i} className={`rounded-md px-2.5 py-1.5 text-xs ${m.role === 'User' ? 'bg-amber/10 text-amber' : 'bg-stone/30 text-fog'}`}>
                                    <span className="font-medium text-dust">{m.role}:</span>{' '}
                                    {truncate(m.text, 500)}
                                  </div>
                                ))}
                              </div>
                            </div>
                          )}
                        </td>
                      </tr>
                    )}
                  </>
                ))}
              </tbody>
            </table>
          </div>

          {/* Pagination */}
          {result.totalPages > 1 && (
            <div className="mt-4 flex items-center justify-between text-sm">
              <span className="text-dust">
                Page {result.page} of {result.totalPages} ({result.totalCount} total)
              </span>
              <div className="flex gap-2">
                <button
                  className="flex items-center gap-1 rounded-xl border border-stone bg-basalt px-3 py-1.5 text-sm text-fog transition-colors hover:text-light disabled:opacity-40"
                  disabled={page <= 1}
                  onClick={() => onPageChange(page - 1)}
                >
                  <ChevronLeft className="h-4 w-4" /> Previous
                </button>
                <button
                  className="flex items-center gap-1 rounded-xl border border-stone bg-basalt px-3 py-1.5 text-sm text-fog transition-colors hover:text-light disabled:opacity-40"
                  disabled={page >= result.totalPages}
                  onClick={() => onPageChange(page + 1)}
                >
                  Next <ChevronRight className="h-4 w-4" />
                </button>
              </div>
            </div>
          )}
        </>
      )}
    </div>
  )
}
