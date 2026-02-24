import { useState, useEffect, useCallback, useRef } from 'react'
import { fetchActivitySummary, fetchAgentMesh, fetchAgentActivityStats } from '../api'
import type { ActivitySummary, MeshTopology, AgentActivityStatsMap, LiveEvent } from '../types'
import { useActivityStream } from '../hooks/useActivityStream'
import type { ConnectionState } from '../hooks/useActivityStream'
import MeshGraph from '../components/MeshGraph'
import type { NodeState } from '../components/MeshGraph'

// ── Connection Status ──

function ConnectionIndicator({ state, onReconnect }: { state: ConnectionState; onReconnect: () => void }) {
  if (state === 'connected') {
    return <span className="flex items-center gap-1.5 text-xs text-sage"><span className="inline-block h-2 w-2 rounded-full bg-sage animate-pulse" /> Live</span>
  }
  if (state === 'reconnecting') {
    return <span className="flex items-center gap-1.5 text-xs text-amber"><span className="inline-block h-2 w-2 rounded-full bg-amber animate-pulse" /> Reconnecting…</span>
  }
  return (
    <span className="flex items-center gap-1.5 text-xs text-rose">
      <span className="inline-block h-2 w-2 rounded-full bg-ember" /> Disconnected
      <button onClick={onReconnect} className="ml-1 underline hover:text-light">Retry</button>
    </span>
  )
}

// ── Summary Cards ──

function SummaryCards({ summary, loading }: { summary: ActivitySummary | null; loading: boolean }) {
  const cards = summary ? [
    { label: 'Total Requests', value: summary.traces.totalTraces, color: 'text-light' },
    { label: 'Error Rate', value: summary.traces.totalTraces > 0 ? `${((summary.traces.erroredCount / summary.traces.totalTraces) * 100).toFixed(1)}%` : '0%', color: summary.traces.erroredCount > 0 ? 'text-rose' : 'text-sage' },
    { label: 'Cache Hit Rate', value: `${(summary.cache.hitRate * 100).toFixed(1)}%`, color: 'text-amber' },
    { label: 'Tasks Completed', value: summary.tasks.completedCount, color: 'text-sage' },
  ] : []

  return (
    <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4">
      {loading ? (
        Array.from({ length: 4 }).map((_, i) => (
          <div key={i} className="animate-pulse rounded-xl border border-stone bg-charcoal p-4">
            <div className="h-3 w-20 rounded bg-stone" />
            <div className="mt-2 h-6 w-12 rounded bg-stone" />
          </div>
        ))
      ) : (
        cards.map(c => (
          <div key={c.label} className="rounded-xl border border-stone bg-charcoal p-4">
            <div className="text-xs text-dust">{c.label}</div>
            <div className={`mt-1 text-2xl font-bold ${c.color}`}>{c.value}</div>
          </div>
        ))
      )}
    </div>
  )
}

// ── Agent Stats Table ──

function AgentStatsTable({ stats, loading }: { stats: AgentActivityStatsMap | null; loading: boolean }) {
  if (loading) {
    return <div className="animate-pulse rounded-xl border border-stone bg-charcoal p-4"><div className="h-20 rounded bg-stone" /></div>
  }

  if (!stats || Object.keys(stats).length === 0) {
    return (
      <div className="rounded-xl border border-stone bg-charcoal p-6 text-center text-sm text-dust">
        No agent activity data yet
      </div>
    )
  }

  const entries = Object.entries(stats).sort((a, b) => b[1].requestCount - a[1].requestCount)

  return (
    <div className="overflow-x-auto rounded-xl border border-stone bg-charcoal">
      <table className="w-full text-sm">
        <thead>
          <tr className="border-b border-stone text-left text-xs text-dust">
            <th className="px-4 py-3">Agent</th>
            <th className="px-4 py-3 text-right">Requests</th>
            <th className="px-4 py-3 text-right">Error Rate</th>
          </tr>
        </thead>
        <tbody>
          {entries.map(([agentId, s]) => (
            <tr key={agentId} className="border-b border-stone/50 last:border-0">
              <td className="px-4 py-3 font-medium text-light">🤖 {agentId}</td>
              <td className="px-4 py-3 text-right text-fog">{s.requestCount}</td>
              <td className={`px-4 py-3 text-right ${s.errorRate > 5 ? 'text-rose' : 'text-sage'}`}>
                {s.errorRate}%
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

// ── Activity Timeline ──

const EVENT_LABELS: Record<string, { icon: string; label: string; color: string }> = {
  requestStart:    { icon: '📥', label: 'Request', color: 'text-amber' },
  routing:         { icon: '🔀', label: 'Routing', color: 'text-blue-400' },
  agentStart:      { icon: '🤖', label: 'Agent Start', color: 'text-sage' },
  toolCall:        { icon: '🔧', label: 'Tool Call', color: 'text-blue-400' },
  toolResult:      { icon: '✅', label: 'Tool Result', color: 'text-sage' },
  agentComplete:   { icon: '✨', label: 'Agent Done', color: 'text-sage' },
  requestComplete: { icon: '📤', label: 'Complete', color: 'text-sage' },
  error:           { icon: '❌', label: 'Error', color: 'text-rose' },
}

function ActivityTimeline({ events }: { events: LiveEvent[] }) {
  const containerRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (containerRef.current) {
      containerRef.current.scrollTop = containerRef.current.scrollHeight
    }
  }, [events.length])

  if (events.length === 0) {
    return (
      <div className="rounded-xl border border-stone bg-charcoal p-6 text-center text-sm text-dust">
        Waiting for activity…
      </div>
    )
  }

  return (
    <div
      ref={containerRef}
      className="max-h-64 overflow-y-auto rounded-xl border border-stone bg-charcoal p-3 sm:p-4 scroll-smooth"
    >
      <div className="space-y-1">
        {events.map((evt, i) => {
          const meta = EVENT_LABELS[evt.type] || { icon: '•', label: evt.type, color: 'text-dust' }
          const time = new Date(evt.timestamp).toLocaleTimeString()
          return (
            <div key={i} className="flex items-start gap-2 text-xs sm:text-sm">
              <span className="shrink-0 w-5 text-center">{meta.icon}</span>
              <span className="shrink-0 text-dust tabular-nums">{time}</span>
              <span className={`font-medium ${meta.color}`}>{meta.label}</span>
              {evt.agentName && <span className="text-fog truncate">{evt.agentName}</span>}
              {evt.toolName && <span className="text-dust truncate">→ {evt.toolName}</span>}
              {evt.errorMessage && <span className="text-rose truncate">{evt.errorMessage}</span>}
            </div>
          )
        })}
      </div>
    </div>
  )
}

// ── Main Page ──

export default function ActivityPage() {
  const [summary, setSummary] = useState<ActivitySummary | null>(null)
  const [topology, setTopology] = useState<MeshTopology | null>(null)
  const [agentStats, setAgentStats] = useState<AgentActivityStatsMap | null>(null)
  const [loading, setLoading] = useState(true)
  const [nodeStates, setNodeStates] = useState<Record<string, NodeState>>({})
  const [eventHistory, setEventHistory] = useState<LiveEvent[]>([])
  const clearTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null)

  const { lastEvent, connectionState, reconnect } = useActivityStream()

  // Load initial data
  const loadData = useCallback(async () => {
    try {
      setLoading(true)
      const [sum, mesh, stats] = await Promise.all([
        fetchActivitySummary(),
        fetchAgentMesh(),
        fetchAgentActivityStats(),
      ])
      setSummary(sum)
      setTopology(mesh)
      setAgentStats(stats)
    } catch {
      // Silently handle — cards will show loading state
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => { loadData() }, [loadData])

  // Auto-refresh summary every 30 seconds
  useEffect(() => {
    const interval = setInterval(async () => {
      try {
        const [sum, stats] = await Promise.all([
          fetchActivitySummary(),
          fetchAgentActivityStats(),
        ])
        setSummary(sum)
        setAgentStats(stats)
      } catch { /* silent */ }
    }, 30000)
    return () => clearInterval(interval)
  }, [])

  // Process live events into node states + timeline
  useEffect(() => {
    if (!lastEvent) return

    const evt = lastEvent

    // Append to timeline (keep last 100 events)
    setEventHistory(prev => [...prev.slice(-99), evt])

    const updateNode = (id: string, state: string, active: boolean) => {
      setNodeStates(prev => ({ ...prev, [id]: { state, active } }))
    }

    switch (evt.type) {
      case 'requestStart':
        updateNode('orchestrator', 'Processing Prompt...', true)
        break
      case 'routing':
        if (evt.agentName) updateNode(evt.agentName, 'Processing Prompt...', true)
        break
      case 'agentStart':
        if (evt.agentName) updateNode(evt.agentName, evt.state || 'Processing Prompt...', true)
        break
      case 'toolCall':
        if (evt.agentName) updateNode(evt.agentName, 'Calling Tools...', true)
        if (evt.agentName && evt.toolName) {
          const toolNodeId = `${evt.agentName}:${evt.toolName}`
          // Dynamically add tool node to topology if it doesn't exist yet
          setTopology(prev => {
            if (!prev) return prev
            if (prev.nodes.some(n => n.id === toolNodeId)) return prev
            return {
              ...prev,
              nodes: [...prev.nodes, { id: toolNodeId, label: evt.toolName!, nodeType: 'tool', isRemote: false }],
              edges: [...prev.edges, { source: evt.agentName!, target: toolNodeId }],
            }
          })
          updateNode(toolNodeId, 'Processing...', true)
        }
        break
      case 'toolResult':
        if (evt.agentName && evt.toolName) updateNode(`${evt.agentName}:${evt.toolName}`, 'Idle', false)
        break
      case 'agentComplete':
        if (evt.agentName) updateNode(evt.agentName, 'Generating Response...', true)
        // Fade to idle after a brief moment
        setTimeout(() => {
          if (evt.agentName) updateNode(evt.agentName, 'Idle', false)
        }, 2000)
        break
      case 'requestComplete':
        // Clear all nodes after brief delay
        if (clearTimeoutRef.current) clearTimeout(clearTimeoutRef.current)
        clearTimeoutRef.current = setTimeout(() => {
          setNodeStates({})
          // Remove dynamic tool nodes from topology
          setTopology(prev => {
            if (!prev) return prev
            return {
              ...prev,
              nodes: prev.nodes.filter(n => n.nodeType !== 'tool'),
              edges: prev.edges.filter(e => !prev.nodes.some(n => n.nodeType === 'tool' && n.id === e.target)),
            }
          })
        }, 3000)
        updateNode('orchestrator', 'Idle', false)
        break
      case 'error':
        if (evt.agentName) updateNode(evt.agentName, 'Error', true)
        break
    }
  }, [lastEvent])

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="font-display text-2xl font-bold text-light">Activity</h1>
          <p className="mt-0.5 text-sm text-dust">Platform metrics and live agent mesh</p>
        </div>
        <ConnectionIndicator state={connectionState} onReconnect={reconnect} />
      </div>

      {/* Summary Cards */}
      <SummaryCards summary={summary} loading={loading} />

      {/* Live Agent Mesh + Timeline */}
      <div className="grid grid-cols-1 gap-6 lg:grid-cols-3">
        <div className="lg:col-span-2">
          <h2 className="mb-3 text-lg font-semibold text-light">Agent Mesh</h2>
          <MeshGraph topology={topology} nodeStates={nodeStates} />
        </div>
        <div>
          <h2 className="mb-3 text-lg font-semibold text-light">Activity Feed</h2>
          <ActivityTimeline events={eventHistory} />
        </div>
      </div>

      {/* Usage Reports */}
      <div>
        <h2 className="mb-3 text-lg font-semibold text-light">Agent Usage</h2>
        <AgentStatsTable stats={agentStats} loading={loading} />
      </div>
    </div>
  )
}
