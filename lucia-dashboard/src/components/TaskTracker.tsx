import { useEffect, useMemo, useRef, useState } from 'react'
import { CheckCircle2, ChevronDown, Loader2, XCircle } from 'lucide-react'
import type { BackgroundTask } from '../api'
import { fetchBackgroundTasks } from '../api'

const COMPLETE_FADE_DELAY_MS = 4_000
const COMPLETE_HIDE_DELAY_MS = 4_800
const MAX_RECENT_TASKS = 5

function sortTasks(tasks: BackgroundTask[]): BackgroundTask[] {
  return [...tasks].sort((left, right) => {
    const leftTime = left.completedAt ?? left.createdAt
    const rightTime = right.completedAt ?? right.createdAt
    return new Date(rightTime).getTime() - new Date(leftTime).getTime()
  })
}

function upsertTask(tasks: BackgroundTask[], task: BackgroundTask): BackgroundTask[] {
  const next = [...tasks]
  const index = next.findIndex(existing => existing.id === task.id)

  if (index >= 0) next[index] = task
  else next.unshift(task)

  return sortTasks(next)
}

function statusBadgeClass(status: BackgroundTask['status']): string {
  switch (status) {
    case 'Running':
      return 'border-amber/30 bg-amber/10 text-amber'
    case 'Queued':
      return 'border-stone/50 bg-stone/30 text-fog'
    case 'Complete':
      return 'border-emerald-400/30 bg-emerald-400/10 text-emerald-300'
    case 'Failed':
      return 'border-rose-400/30 bg-rose-400/10 text-rose-300'
    case 'Cancelled':
      return 'border-stone/50 bg-stone/30 text-dust'
  }
}

export default function TaskTracker() {
  const [tasks, setTasks] = useState<BackgroundTask[]>([])
  const [expanded, setExpanded] = useState(false)
  const [fadingIds, setFadingIds] = useState<string[]>([])
  const [hiddenIds, setHiddenIds] = useState<string[]>([])
  const sourceRef = useRef<EventSource | null>(null)
  const reconnectTimeoutRef = useRef<number | null>(null)
  const retriesRef = useRef(0)
  const completeTimersRef = useRef<Record<string, { fade: number; hide: number }>>({})

  useEffect(() => {
    let disposed = false

    const clearReconnectTimer = () => {
      if (reconnectTimeoutRef.current !== null) {
        window.clearTimeout(reconnectTimeoutRef.current)
        reconnectTimeoutRef.current = null
      }
    }

    const connect = () => {
      clearReconnectTimer()
      sourceRef.current?.close()

      const source = new EventSource('/api/tasks/background/stream')
      sourceRef.current = source

      source.onopen = () => {
        retriesRef.current = 0
      }

      source.addEventListener('snapshot', event => {
        try {
          const next = JSON.parse(event.data) as BackgroundTask[]
          setTasks(sortTasks(next))
        } catch {
          /* ignore malformed payloads */
        }
      })

      source.addEventListener('update', event => {
        try {
          const next = JSON.parse(event.data) as BackgroundTask
          setTasks(current => upsertTask(current, next))
        } catch {
          /* ignore malformed payloads */
        }
      })

      source.onerror = () => {
        source.close()
        sourceRef.current = null

        if (disposed) return

        const delay = Math.min(1_000 * 2 ** retriesRef.current, 30_000)
        retriesRef.current += 1
        reconnectTimeoutRef.current = window.setTimeout(() => {
          fetchBackgroundTasks()
            .then(next => {
              if (!disposed) setTasks(sortTasks(next))
            })
            .catch(() => {
              /* ignore fetch failures during reconnect */
            })
            .finally(() => {
              if (!disposed) connect()
            })
        }, delay)
      }
    }

    fetchBackgroundTasks()
      .then(next => {
        if (!disposed) setTasks(sortTasks(next))
      })
      .catch(() => {
        /* ignore initial fetch failures */
      })

    connect()

    // Poll every 5 seconds as a fallback for missed SSE updates
    const pollInterval = window.setInterval(() => {
      if (disposed) return
      fetchBackgroundTasks()
        .then(next => {
          if (!disposed) setTasks(sortTasks(next))
        })
        .catch(() => { /* ignore poll failures */ })
    }, 2_000)

    return () => {
      disposed = true
      clearReconnectTimer()
      window.clearInterval(pollInterval)
      sourceRef.current?.close()
      sourceRef.current = null

      Object.values(completeTimersRef.current).forEach(timer => {
        window.clearTimeout(timer.fade)
        window.clearTimeout(timer.hide)
      })
      completeTimersRef.current = {}
    }
  }, [])

  useEffect(() => {
    const completeTasks = tasks.filter(task => task.status === 'Complete')
    const completeIds = new Set(completeTasks.map(task => task.id))

    completeTasks.forEach(task => {
      if (completeTimersRef.current[task.id]) return

      const fade = window.setTimeout(() => {
        setFadingIds(current => (current.includes(task.id) ? current : [...current, task.id]))
      }, COMPLETE_FADE_DELAY_MS)

      const hide = window.setTimeout(() => {
        setHiddenIds(current => (current.includes(task.id) ? current : [...current, task.id]))
        setFadingIds(current => current.filter(id => id !== task.id))
        delete completeTimersRef.current[task.id]
      }, COMPLETE_HIDE_DELAY_MS)

      completeTimersRef.current[task.id] = { fade, hide }
    })

    Object.entries(completeTimersRef.current).forEach(([taskId, timer]) => {
      if (completeIds.has(taskId)) return

      window.clearTimeout(timer.fade)
      window.clearTimeout(timer.hide)
      delete completeTimersRef.current[taskId]
      setFadingIds(current => current.filter(id => id !== taskId))
      setHiddenIds(current => current.filter(id => id !== taskId))
    })
  }, [tasks])

  const activeTasks = useMemo(
    () => tasks.filter(task => task.status === 'Queued' || task.status === 'Running'),
    [tasks],
  )

  const visibleTasks = useMemo(() => {
    const hidden = new Set(hiddenIds)
    const activeIds = new Set(activeTasks.map(task => task.id))
    const recentTerminal = tasks
      .filter(task => !activeIds.has(task.id) && !hidden.has(task.id))
      .slice(0, MAX_RECENT_TASKS)

    return [...activeTasks, ...recentTerminal]
  }, [activeTasks, hiddenIds, tasks])

  if (tasks.length === 0) return null

  if (!expanded && activeTasks.length === 0) return null

  if (!expanded) {
    return (
      <button
        type="button"
        onClick={() => setExpanded(true)}
        className="fixed bottom-6 right-6 z-50 inline-flex items-center gap-2 rounded-full border border-amber/30 bg-obsidian/95 px-4 py-2.5 text-light shadow-[0_20px_50px_rgba(0,0,0,0.45)] backdrop-blur-md transition-all hover:-translate-y-0.5 hover:border-amber/50 hover:text-amber"
      >
        <Loader2 className="h-4 w-4 animate-spin text-amber" />
        <span className="text-sm font-semibold">
          {activeTasks.length} task{activeTasks.length === 1 ? '' : 's'}
        </span>
      </button>
    )
  }

  return (
    <section className="fixed bottom-6 right-6 z-50 w-80 overflow-hidden rounded-[24px] border border-stone/50 bg-obsidian/95 shadow-[0_24px_60px_rgba(0,0,0,0.48)] backdrop-blur-md">
      <button
        type="button"
        onClick={() => setExpanded(false)}
        className="flex w-full items-center justify-between border-b border-stone/40 px-4 py-3 text-left transition-colors hover:bg-stone/20"
      >
        <div>
          <p className="text-[11px] font-semibold uppercase tracking-[0.24em] text-dust">Live activity</p>
          <h2 className="mt-1 text-sm font-semibold text-light">Background tasks</h2>
        </div>
        <ChevronDown className="h-4 w-4 text-fog" />
      </button>

      <div className="max-h-80 space-y-2 overflow-y-auto p-3">
        {visibleTasks.length === 0 && (
          <p className="py-6 text-center text-xs text-dust">No background tasks in flight.</p>
        )}

        {visibleTasks.map(task => {
          const isRunning = task.status === 'Running'
          const isQueued = task.status === 'Queued'
          const isFading = fadingIds.includes(task.id)

          return (
            <article
              key={task.id}
              className={`rounded-2xl border border-stone/40 bg-charcoal/70 p-3 transition-all duration-500 ${
                isFading ? 'opacity-0 translate-y-1' : 'opacity-100'
              }`}
            >
              <div className="flex items-start justify-between gap-3">
                <div className="min-w-0 flex-1">
                  <p className="truncate text-sm font-medium text-light">{task.description}</p>
                  {task.progressMessage && (
                    <p className="mt-1 text-[11px] leading-5 text-fog">{task.progressMessage}</p>
                  )}
                </div>

                <div className="flex shrink-0 items-center gap-2">
                  {isRunning && <Loader2 className="h-4 w-4 animate-spin text-amber" />}
                  {task.status === 'Complete' && <CheckCircle2 className="h-4 w-4 text-emerald-300" />}
                  {task.status === 'Failed' && <XCircle className="h-4 w-4 text-rose-300" />}
                  <span className={`rounded-full border px-2 py-1 text-[10px] font-semibold uppercase tracking-[0.18em] ${statusBadgeClass(task.status)}`}>
                    {task.status}
                  </span>
                </div>
              </div>

              {(isRunning || isQueued) && (
                <div className="mt-3 space-y-2">
                  {task.stages && task.stages.length > 1 ? (
                    task.stages.map((stage, idx) => {
                      const stageRunning = stage.status === 'Running'
                      const stageDone = stage.status === 'Complete'
                      const stageQueued = stage.status === 'Queued'
                      return (
                        <div key={idx}>
                          <div className="flex items-center justify-between text-[10px] uppercase tracking-[0.16em]">
                            <span className={stageDone ? 'text-emerald-300' : stageRunning ? 'text-amber' : 'text-dust'}>
                              {stageDone && '✓ '}{stage.name}
                            </span>
                            <span className="text-dust">
                              {stageQueued ? '—' : `${stage.progressPercent}%`}
                            </span>
                          </div>
                          <div className="mt-0.5 h-1 overflow-hidden rounded-full bg-basalt">
                            <div
                              className={`h-full rounded-full transition-all duration-300 ${
                                stageDone ? 'bg-emerald-400' : stageRunning ? 'bg-gradient-to-r from-amber via-amber-glow to-cyan-400' : 'bg-basalt'
                              }`}
                              style={{ width: `${stageDone ? 100 : Math.max(0, Math.min(stage.progressPercent, 100))}%` }}
                            />
                          </div>
                          {stageRunning && stage.progressMessage && (
                            <p className="mt-0.5 text-[10px] text-fog">{stage.progressMessage}</p>
                          )}
                        </div>
                      )
                    })
                  ) : (
                    <>
                      <div className="h-1.5 overflow-hidden rounded-full bg-basalt">
                        <div
                          className="h-full rounded-full bg-gradient-to-r from-amber via-amber-glow to-cyan-400 transition-all duration-300"
                          style={{ width: `${Math.max(0, Math.min(task.progressPercent, 100))}%` }}
                        />
                      </div>
                      <div className="mt-1 flex items-center justify-between text-[10px] uppercase tracking-[0.16em] text-dust">
                        <span>{isQueued ? 'Queued' : task.progressMessage || 'Working'}</span>
                        <span>{task.progressPercent}%</span>
                      </div>
                    </>
                  )}
                </div>
              )}

              {task.status === 'Failed' && task.error && (
                <p className="mt-2 text-[11px] leading-5 text-rose-300">{task.error}</p>
              )}
            </article>
          )
        })}
      </div>
    </section>
  )
}
