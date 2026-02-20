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

function formatDate(iso: string | null) {
  if (!iso) return '—'
  return new Date(iso).toLocaleString()
}

function truncate(text: string | null | undefined, max: number) {
  if (!text) return '—'
  return text.length > max ? text.slice(0, max) + '…' : text
}

function statusBadge(status: string) {
  const colors: Record<string, string> = {
    Completed: 'bg-green-600',
    Working: 'bg-blue-600',
    Submitted: 'bg-yellow-600',
    InputRequired: 'bg-purple-600',
    Failed: 'bg-red-600',
    Canceled: 'bg-gray-600',
  }
  const bg = colors[status] ?? 'bg-gray-500'
  return (
    <span className={`inline-block rounded px-2 py-0.5 text-xs font-medium ${bg}`}>
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

  // Archive filters
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
      <h1 className="mb-4 text-2xl font-bold">Tasks</h1>

      {/* Stats bar */}
      {stats && (
        <div className="mb-4 flex gap-4">
          <div className="rounded bg-gray-800 px-4 py-2 text-center">
            <div className="text-2xl font-bold text-blue-400">{stats.activeCount}</div>
            <div className="text-xs text-gray-400">Active</div>
          </div>
          <div className="rounded bg-gray-800 px-4 py-2 text-center">
            <div className="text-2xl font-bold text-green-400">{stats.archived.completedCount}</div>
            <div className="text-xs text-gray-400">Completed</div>
          </div>
          <div className="rounded bg-gray-800 px-4 py-2 text-center">
            <div className="text-2xl font-bold text-red-400">{stats.archived.failedCount}</div>
            <div className="text-xs text-gray-400">Failed</div>
          </div>
          <div className="rounded bg-gray-800 px-4 py-2 text-center">
            <div className="text-2xl font-bold text-gray-400">{stats.archived.canceledCount}</div>
            <div className="text-xs text-gray-400">Canceled</div>
          </div>
          <div className="rounded bg-gray-800 px-4 py-2 text-center">
            <div className="text-2xl font-bold text-white">{stats.archived.totalTasks}</div>
            <div className="text-xs text-gray-400">Total Archived</div>
          </div>
        </div>
      )}

      {/* Tab selector */}
      <div className="mb-4 flex gap-1 rounded bg-gray-800 p-1">
        <button
          className={`rounded px-4 py-2 text-sm font-medium ${tab === 'active' ? 'bg-indigo-600 text-white' : 'text-gray-400 hover:text-gray-200'}`}
          onClick={() => setTab('active')}
        >
          Active Tasks
        </button>
        <button
          className={`rounded px-4 py-2 text-sm font-medium ${tab === 'history' ? 'bg-indigo-600 text-white' : 'text-gray-400 hover:text-gray-200'}`}
          onClick={() => { setTab('history'); setPage(1) }}
        >
          Task History
        </button>
      </div>

      {error && (
        <div className="mb-4 rounded bg-red-900/50 px-4 py-2 text-red-200">{error}</div>
      )}

      {loading ? (
        <div className="py-12 text-center text-gray-400">Loading…</div>
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
    return <div className="py-8 text-center text-gray-500">No active tasks.</div>
  }

  return (
    <table className="w-full text-left text-sm">
      <thead className="border-b border-gray-700 text-gray-400">
        <tr>
          <th className="px-3 py-2">Task ID</th>
          <th className="px-3 py-2">Status</th>
          <th className="px-3 py-2">Messages</th>
          <th className="px-3 py-2">User Input</th>
          <th className="px-3 py-2">Last Updated</th>
          <th className="px-3 py-2">Actions</th>
        </tr>
      </thead>
      <tbody>
        {tasks.map((t) => (
          <>
            <tr
              key={t.id}
              className="cursor-pointer border-b border-gray-800 hover:bg-gray-800/50"
              onClick={() => onToggle(t.id)}
            >
              <td className="px-3 py-2 font-mono text-xs">{t.id.slice(0, 12)}…</td>
              <td className="px-3 py-2">{statusBadge(t.status)}</td>
              <td className="px-3 py-2">{t.messageCount}</td>
              <td className="px-3 py-2">{truncate(t.userInput, 80)}</td>
              <td className="px-3 py-2 text-gray-400">{formatDate(t.lastUpdated)}</td>
              <td className="px-3 py-2">
                {t.status !== 'Completed' && t.status !== 'Failed' && t.status !== 'Canceled' && (
                  <button
                    className="rounded bg-red-700 px-2 py-1 text-xs hover:bg-red-600"
                    onClick={(e) => { e.stopPropagation(); onCancel(t.id) }}
                  >
                    Cancel
                  </button>
                )}
              </td>
            </tr>
            {expandedId === t.id && (
              <tr key={`${t.id}-detail`}>
                <td colSpan={6} className="bg-gray-800/30 px-4 py-3">
                  <div className="text-xs text-gray-300">
                    <strong>Full ID:</strong> {t.id}<br />
                    <strong>Context ID:</strong> {t.contextId ?? '—'}<br />
                    <strong>User Input:</strong> {t.userInput ?? '—'}
                  </div>
                </td>
              </tr>
            )}
          </>
        ))}
      </tbody>
    </table>
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
      <div className="mb-3 flex gap-3">
        <select
          className="rounded bg-gray-800 px-3 py-1.5 text-sm text-gray-200"
          value={statusFilter}
          onChange={(e) => onStatusChange(e.target.value)}
        >
          <option value="">All Statuses</option>
          <option value="Completed">Completed</option>
          <option value="Failed">Failed</option>
          <option value="Canceled">Canceled</option>
        </select>
        <input
          type="text"
          placeholder="Search user input…"
          className="rounded bg-gray-800 px-3 py-1.5 text-sm text-gray-200 placeholder-gray-500"
          value={searchFilter}
          onChange={(e) => onSearchChange(e.target.value)}
        />
      </div>

      {!result || result.items.length === 0 ? (
        <div className="py-8 text-center text-gray-500">No archived tasks found.</div>
      ) : (
        <>
          <table className="w-full text-left text-sm">
            <thead className="border-b border-gray-700 text-gray-400">
              <tr>
                <th className="px-3 py-2">Task ID</th>
                <th className="px-3 py-2">Status</th>
                <th className="px-3 py-2">Agents</th>
                <th className="px-3 py-2">User Input</th>
                <th className="px-3 py-2">Messages</th>
                <th className="px-3 py-2">Archived</th>
              </tr>
            </thead>
            <tbody>
              {result.items.map((t) => (
                <>
                  <tr
                    key={t.id}
                    className="cursor-pointer border-b border-gray-800 hover:bg-gray-800/50"
                    onClick={() => onToggle(t.id)}
                  >
                    <td className="px-3 py-2 font-mono text-xs">{t.id.slice(0, 12)}…</td>
                    <td className="px-3 py-2">{statusBadge(t.status)}</td>
                    <td className="px-3 py-2 text-xs">{t.agentIds.join(', ') || '—'}</td>
                    <td className="px-3 py-2">{truncate(t.userInput, 60)}</td>
                    <td className="px-3 py-2">{t.messageCount}</td>
                    <td className="px-3 py-2 text-gray-400">{formatDate(t.archivedAt)}</td>
                  </tr>
                  {expandedId === t.id && (
                    <tr key={`${t.id}-detail`}>
                      <td colSpan={6} className="bg-gray-800/30 px-4 py-3">
                        <div className="mb-2 text-xs text-gray-300">
                          <strong>Full ID:</strong> {t.id}<br />
                          <strong>Context ID:</strong> {t.contextId ?? '—'}<br />
                          <strong>Created:</strong> {formatDate(t.createdAt)}<br />
                          <strong>Final Response:</strong> {truncate(t.finalResponse, 300)}
                        </div>
                        {t.history.length > 0 && (
                          <div className="mt-2">
                            <div className="mb-1 text-xs font-medium text-gray-400">Conversation History</div>
                            <div className="max-h-60 space-y-1 overflow-y-auto">
                              {t.history.map((m, i) => (
                                <div key={i} className={`rounded px-2 py-1 text-xs ${m.role === 'User' ? 'bg-blue-900/30' : 'bg-gray-700/50'}`}>
                                  <span className="font-medium text-gray-400">{m.role}:</span>{' '}
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

          {/* Pagination */}
          {result.totalPages > 1 && (
            <div className="mt-3 flex items-center justify-between text-sm text-gray-400">
              <span>
                Page {result.page} of {result.totalPages} ({result.totalCount} total)
              </span>
              <div className="flex gap-2">
                <button
                  className="rounded bg-gray-700 px-3 py-1 disabled:opacity-50"
                  disabled={page <= 1}
                  onClick={() => onPageChange(page - 1)}
                >
                  Previous
                </button>
                <button
                  className="rounded bg-gray-700 px-3 py-1 disabled:opacity-50"
                  disabled={page >= result.totalPages}
                  onClick={() => onPageChange(page + 1)}
                >
                  Next
                </button>
              </div>
            </div>
          )}
        </>
      )}
    </div>
  )
}
