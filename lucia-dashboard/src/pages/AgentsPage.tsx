import { useState, useEffect, useCallback } from 'react'

/* ------------------------------------------------------------------ */
/*  Types                                                              */
/* ------------------------------------------------------------------ */

interface AgentSkill {
  id: string
  name: string
  description: string
  examples: string[]
  tags: string[]
}

interface AgentCapabilities {
  pushNotifications: boolean
  streaming: boolean
  stateTransitionHistory: boolean
}

interface AgentCard {
  url: string
  name: string
  description: string
  version: string
  capabilities: AgentCapabilities
  defaultInputModes: string[]
  defaultOutputModes: string[]
  skills: AgentSkill[]
}

interface A2AMessagePart {
  kind: string
  text?: string
}

interface A2AMessage {
  messageId: string
  contextId: string
  role: string
  parts: A2AMessagePart[]
}

/* ------------------------------------------------------------------ */
/*  API helpers                                                        */
/* ------------------------------------------------------------------ */

const BASE = '/api'

async function fetchAgents(): Promise<AgentCard[]> {
  const res = await fetch('/agents')
  if (!res.ok) throw new Error(`Failed to fetch agents: ${res.statusText}`)
  return res.json()
}

async function registerAgent(agentUri: string): Promise<void> {
  const form = new FormData()
  form.append('agentId', agentUri)
  const res = await fetch('/agents/register', { method: 'POST', body: form })
  if (!res.ok) {
    const text = await res.text()
    throw new Error(text || res.statusText)
  }
}

async function refreshAgent(agentUrl: string): Promise<void> {
  const res = await fetch(`/agents/${encodeURIComponent(agentUrl)}`, { method: 'PUT' })
  if (!res.ok) {
    const text = await res.text()
    throw new Error(text || res.statusText)
  }
}

async function unregisterAgent(agentUrl: string): Promise<void> {
  const res = await fetch(`/agents/${encodeURIComponent(agentUrl)}`, { method: 'DELETE' })
  if (!res.ok) throw new Error(`Failed to unregister: ${res.statusText}`)
}

async function sendA2AMessage(
  agentUrl: string,
  message: string,
  contextId?: string,
): Promise<A2AMessage> {
  const body = {
    jsonrpc: '2.0',
    method: 'message/send',
    id: crypto.randomUUID(),
    params: {
      message: {
        kind: 'message',
        messageId: crypto.randomUUID(),
        ...(contextId ? { contextId } : {}),
        role: 'user',
        parts: [{ kind: 'text', text: message }],
      },
    },
  }

  // Proxy through the AgentHost so the browser doesn't need direct access
  // to internal Aspire service URLs
  const res = await fetch(`/agents/proxy?agentUrl=${encodeURIComponent(agentUrl)}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })

  if (!res.ok) {
    const text = await res.text()
    throw new Error(text || res.statusText)
  }

  const json = await res.json()
  // A2A JSON-RPC response: { result: { kind: "task", contextId, status: { message: {...} }, ... } }
  const task = json.result ?? json
  const taskMessage = task?.status?.message ?? task
  // Attach contextId from the task onto the message for multi-turn tracking
  if (task?.contextId && taskMessage) {
    taskMessage.contextId = task.contextId
  }
  return taskMessage
}

/* ------------------------------------------------------------------ */
/*  Toast                                                              */
/* ------------------------------------------------------------------ */

interface Toast {
  id: number
  message: string
  type: 'success' | 'error'
}

let toastId = 0

function ToastContainer({ toasts, onDismiss }: { toasts: Toast[]; onDismiss: (id: number) => void }) {
  return (
    <div className="fixed top-4 right-4 z-50 flex flex-col gap-2">
      {toasts.map((t) => (
        <div
          key={t.id}
          className={`flex items-center gap-3 rounded-lg px-4 py-3 shadow-lg text-sm font-medium transition-all duration-300 ${
            t.type === 'success' ? 'bg-green-600 text-white' : 'bg-red-600 text-white'
          }`}
        >
          <span>{t.type === 'success' ? '✓' : '✕'}</span>
          <span className="flex-1">{t.message}</span>
          <button onClick={() => onDismiss(t.id)} className="ml-2 opacity-70 hover:opacity-100">×</button>
        </div>
      ))}
    </div>
  )
}

/* ------------------------------------------------------------------ */
/*  Capability Badge                                                   */
/* ------------------------------------------------------------------ */

function CapBadge({ label, enabled }: { label: string; enabled: boolean }) {
  return (
    <span
      className={`inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[11px] font-medium ${
        enabled
          ? 'bg-green-500/15 text-green-400 border border-green-500/30'
          : 'bg-gray-700/50 text-gray-500 border border-gray-600/30'
      }`}
    >
      <span className={`inline-block h-1.5 w-1.5 rounded-full ${enabled ? 'bg-green-400' : 'bg-gray-600'}`} />
      {label}
    </span>
  )
}

/* ------------------------------------------------------------------ */
/*  Agent Card Component                                               */
/* ------------------------------------------------------------------ */

function AgentCardView({
  agent,
  onSelect,
  onRefresh,
  onDelete,
  selected,
}: {
  agent: AgentCard
  onSelect: () => void
  onRefresh: () => void
  onDelete: () => void
  selected: boolean
}) {
  return (
    <div
      onClick={onSelect}
      className={`cursor-pointer rounded-xl border p-4 transition-all ${
        selected
          ? 'border-indigo-500 bg-indigo-500/5 shadow-lg shadow-indigo-500/10'
          : 'border-gray-700 bg-gray-800 hover:border-gray-600'
      }`}
    >
      <div className="flex items-start justify-between">
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2">
            <h3 className="text-sm font-semibold text-white truncate">{agent.name}</h3>
            <span className="text-[10px] font-mono text-gray-500 bg-gray-700/50 px-1.5 py-0.5 rounded">
              v{agent.version}
            </span>
          </div>
          <p className="mt-1 text-xs text-gray-400 line-clamp-2">{agent.description}</p>
          <p className="mt-1.5 text-[11px] font-mono text-gray-600 truncate">{agent.url}</p>
        </div>
        <div className="flex shrink-0 gap-1 ml-3">
          <button
            onClick={(e) => { e.stopPropagation(); onRefresh() }}
            title="Refresh agent card"
            className="rounded-lg p-1.5 text-gray-500 hover:bg-gray-700 hover:text-indigo-400 transition-colors"
          >
            ↻
          </button>
          <button
            onClick={(e) => { e.stopPropagation(); onDelete() }}
            title="Unregister agent"
            className="rounded-lg p-1.5 text-gray-500 hover:bg-gray-700 hover:text-red-400 transition-colors"
          >
            ✕
          </button>
        </div>
      </div>

      {/* Capabilities */}
      <div className="mt-3 flex flex-wrap gap-1.5">
        <CapBadge label="Push" enabled={agent.capabilities?.pushNotifications ?? false} />
        <CapBadge label="Streaming" enabled={agent.capabilities?.streaming ?? false} />
        <CapBadge label="History" enabled={agent.capabilities?.stateTransitionHistory ?? false} />
      </div>

      {/* Skills */}
      {agent.skills && agent.skills.length > 0 && (
        <div className="mt-3 flex flex-wrap gap-1">
          {agent.skills.map((s) => (
            <span
              key={s.id}
              className="rounded bg-gray-700 px-2 py-0.5 text-[10px] font-medium text-gray-300"
              title={s.description}
            >
              {s.name}
            </span>
          ))}
        </div>
      )}
    </div>
  )
}

/* ------------------------------------------------------------------ */
/*  HA Simulation Context                                              */
/* ------------------------------------------------------------------ */

const DEFAULT_HA_TEMPLATE = `HOME ASSISTANT CONTEXT:

REQUEST_CONTEXT:
{
  "timestamp": "{{timestamp}}",
  "day_of_week": "{{day_of_week}}",
  "location": "{{location}}",
  "device": {
    "id": "{{device_id}}",
    "area": "{{device_area}}",
    "type": "{{device_type}}"
  }
}

The user is requesting assistance with their Home Assistant-controlled smart home. Use the entity IDs above to reference specific devices when delegating to specialized agents. Consider the current time and device states when planning actions.`

interface HAContext {
  timestamp: string
  day_of_week: string
  location: string
  device_id: string
  device_area: string
  device_type: string
}

function getDefaultHAContext(): HAContext {
  const now = new Date()
  return {
    timestamp: now.toISOString().replace('T', ' ').substring(0, 19),
    day_of_week: now.toLocaleDateString('en-US', { weekday: 'long' }),
    location: 'Home',
    device_id: 'conversation.lucia',
    device_area: 'Living Room',
    device_type: 'conversation',
  }
}

function renderTemplate(template: string, ctx: HAContext): string {
  return template
    .replace(/\{\{timestamp\}\}/g, ctx.timestamp)
    .replace(/\{\{day_of_week\}\}/g, ctx.day_of_week)
    .replace(/\{\{location\}\}/g, ctx.location)
    .replace(/\{\{device_id\}\}/g, ctx.device_id)
    .replace(/\{\{device_area\}\}/g, ctx.device_area)
    .replace(/\{\{device_type\}\}/g, ctx.device_type)
}

function HASimulationPanel({
  enabled,
  onToggle,
  context,
  onContextChange,
  template,
  onTemplateChange,
}: {
  enabled: boolean
  onToggle: (val: boolean) => void
  context: HAContext
  onContextChange: (ctx: HAContext) => void
  template: string
  onTemplateChange: (t: string) => void
}) {
  const [showPreview, setShowPreview] = useState(false)
  const [showTemplate, setShowTemplate] = useState(false)

  const contextFields: { key: keyof HAContext; label: string; placeholder: string }[] = [
    { key: 'timestamp', label: 'Timestamp', placeholder: '2026-02-19 12:00:00' },
    { key: 'day_of_week', label: 'Day of Week', placeholder: 'Wednesday' },
    { key: 'location', label: 'Location', placeholder: 'Home' },
    { key: 'device_id', label: 'Device ID', placeholder: 'conversation.lucia' },
    { key: 'device_area', label: 'Device Area', placeholder: 'Living Room' },
    { key: 'device_type', label: 'Device Type', placeholder: 'conversation' },
  ]

  return (
    <div className={`border-b transition-colors ${enabled ? 'border-orange-500/30 bg-orange-500/5' : 'border-gray-700'}`}>
      {/* Toggle header */}
      <div className="flex items-center justify-between px-4 py-2">
        <div className="flex items-center gap-2">
          <button
            type="button"
            role="switch"
            aria-checked={enabled}
            onClick={() => onToggle(!enabled)}
            className={`relative inline-flex h-5 w-9 shrink-0 cursor-pointer rounded-full border-2 border-transparent transition-colors duration-200 ${
              enabled ? 'bg-orange-500' : 'bg-gray-600'
            }`}
          >
            <span
              className={`pointer-events-none inline-block h-4 w-4 transform rounded-full bg-white shadow transition duration-200 ${
                enabled ? 'translate-x-4' : 'translate-x-0'
              }`}
            />
          </button>
          <span className={`text-xs font-medium ${enabled ? 'text-orange-400' : 'text-gray-500'}`}>
            🏠 Simulate Home Assistant
          </span>
        </div>
        {enabled && (
          <div className="flex gap-1">
            <button
              onClick={() => { setShowPreview(!showPreview); setShowTemplate(false) }}
              className={`rounded px-2 py-0.5 text-[10px] font-medium transition-colors ${
                showPreview ? 'bg-orange-500/20 text-orange-400' : 'text-gray-500 hover:text-gray-300'
              }`}
            >
              Preview
            </button>
            <button
              onClick={() => { setShowTemplate(!showTemplate); setShowPreview(false) }}
              className={`rounded px-2 py-0.5 text-[10px] font-medium transition-colors ${
                showTemplate ? 'bg-orange-500/20 text-orange-400' : 'text-gray-500 hover:text-gray-300'
              }`}
            >
              Template
            </button>
            <button
              onClick={() => onContextChange(getDefaultHAContext())}
              className="rounded px-2 py-0.5 text-[10px] font-medium text-gray-500 hover:text-gray-300 transition-colors"
              title="Reset to current time/defaults"
            >
              Reset
            </button>
          </div>
        )}
      </div>

      {/* Context fields */}
      {enabled && !showPreview && !showTemplate && (
        <div className="px-4 pb-3 grid grid-cols-3 gap-2">
          {contextFields.map((f) => (
            <div key={f.key}>
              <label className="block text-[10px] font-medium text-gray-500 mb-0.5">{f.label}</label>
              <input
                type="text"
                value={context[f.key]}
                onChange={(e) => onContextChange({ ...context, [f.key]: e.target.value })}
                placeholder={f.placeholder}
                className="w-full rounded border border-gray-600 bg-gray-700 px-2 py-1 text-[11px] text-white placeholder-gray-500 focus:border-orange-500 focus:ring-1 focus:ring-orange-500 focus:outline-none font-mono"
              />
            </div>
          ))}
        </div>
      )}

      {/* Template editor */}
      {enabled && showTemplate && (
        <div className="px-4 pb-3">
          <textarea
            value={template}
            onChange={(e) => onTemplateChange(e.target.value)}
            rows={12}
            className="w-full rounded border border-gray-600 bg-gray-700 px-3 py-2 text-[11px] text-white font-mono placeholder-gray-500 focus:border-orange-500 focus:ring-1 focus:ring-orange-500 focus:outline-none resize-y"
          />
          <p className="mt-1 text-[10px] text-gray-600">
            Tokens: {'{{timestamp}}'} {'{{day_of_week}}'} {'{{location}}'} {'{{device_id}}'} {'{{device_area}}'} {'{{device_type}}'}
          </p>
        </div>
      )}

      {/* Rendered preview */}
      {enabled && showPreview && (
        <div className="px-4 pb-3">
          <pre className="rounded border border-gray-600 bg-gray-900 p-3 text-[11px] text-gray-300 font-mono whitespace-pre-wrap overflow-auto max-h-48">
            {renderTemplate(template, context)}
          </pre>
        </div>
      )}
    </div>
  )
}

/* ------------------------------------------------------------------ */
/*  Agent Detail Panel                                                 */
/* ------------------------------------------------------------------ */

function AgentDetailPanel({
  agent,
  onSendMessage,
  sending,
  conversationHistory,
}: {
  agent: AgentCard
  onSendMessage: (msg: string) => void
  sending: boolean
  conversationHistory: { role: string; text: string; timestamp: Date }[]
}) {
  const [message, setMessage] = useState('')
  const [haEnabled, setHaEnabled] = useState(false)
  const [haContext, setHaContext] = useState<HAContext>(getDefaultHAContext)
  const [haTemplate, setHaTemplate] = useState(DEFAULT_HA_TEMPLATE)

  const handleSend = () => {
    if (!message.trim() || sending) return
    let fullMessage = message.trim()
    if (haEnabled) {
      // Refresh timestamp to "now" on each send
      const freshCtx = { ...haContext, timestamp: new Date().toISOString().replace('T', ' ').substring(0, 19) }
      setHaContext(freshCtx)
      const rendered = renderTemplate(haTemplate, freshCtx)
      fullMessage = rendered + '\n\n' + fullMessage
    }
    onSendMessage(fullMessage)
    setMessage('')
  }

  return (
    <div className="flex h-full flex-col">
      {/* Agent info header */}
      <div className="border-b border-gray-700 p-4">
        <div className="flex items-center gap-3">
          <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-lg bg-indigo-500/20 text-lg font-bold text-indigo-400">
            {agent.name.charAt(0).toUpperCase()}
          </div>
          <div className="min-w-0 flex-1">
            <h2 className="text-base font-semibold text-white truncate">{agent.name}</h2>
            <p className="text-xs text-gray-500 font-mono truncate">{agent.url}</p>
          </div>
        </div>
        <p className="mt-2 text-sm text-gray-400">{agent.description}</p>

        {/* Modes */}
        <div className="mt-3 flex gap-4 text-xs text-gray-500">
          <span>Input: {(agent.defaultInputModes ?? ['text']).join(', ')}</span>
          <span>Output: {(agent.defaultOutputModes ?? ['text']).join(', ')}</span>
        </div>

        {/* Skills detail */}
        {agent.skills && agent.skills.length > 0 && (
          <div className="mt-3 space-y-2">
            <h4 className="text-xs font-semibold text-gray-400 uppercase tracking-wider">Skills</h4>
            {agent.skills.map((skill) => (
              <div key={skill.id} className="rounded-lg bg-gray-750 border border-gray-700 p-3">
                <div className="flex items-center gap-2">
                  <span className="text-sm font-medium text-white">{skill.name}</span>
                  {skill.tags?.map((tag) => (
                    <span key={tag} className="rounded bg-indigo-500/10 px-1.5 py-0.5 text-[10px] text-indigo-400">
                      {tag}
                    </span>
                  ))}
                </div>
                <p className="mt-1 text-xs text-gray-400">{skill.description}</p>
                {skill.examples && skill.examples.length > 0 && (
                  <div className="mt-2 flex flex-wrap gap-1">
                    {skill.examples.map((ex, i) => (
                      <button
                        key={i}
                        onClick={() => setMessage(ex)}
                        className="rounded-full bg-gray-700 px-2.5 py-1 text-[11px] text-gray-300 hover:bg-indigo-500/20 hover:text-indigo-300 transition-colors cursor-pointer"
                        title="Click to use as test message"
                      >
                        "{ex}"
                      </button>
                    ))}
                  </div>
                )}
              </div>
            ))}
          </div>
        )}
      </div>

      {/* HA Simulation */}
      <HASimulationPanel
        enabled={haEnabled}
        onToggle={setHaEnabled}
        context={haContext}
        onContextChange={setHaContext}
        template={haTemplate}
        onTemplateChange={setHaTemplate}
      />

      {/* Conversation area */}
      <div className="flex-1 overflow-y-auto p-4 space-y-3">
        {conversationHistory.length === 0 && (
          <div className="flex h-full items-center justify-center text-sm text-gray-600">
            Send a message to test this agent
          </div>
        )}
        {conversationHistory.map((msg, i) => (
          <div
            key={i}
            className={`flex ${msg.role === 'user' ? 'justify-end' : 'justify-start'}`}
          >
            <div
              className={`max-w-[80%] rounded-xl px-4 py-2.5 text-sm ${
                msg.role === 'user'
                  ? 'bg-indigo-500 text-white'
                  : 'bg-gray-700 text-gray-200'
              }`}
            >
              <p className="whitespace-pre-wrap">{msg.text}</p>
              <p className={`mt-1 text-[10px] ${msg.role === 'user' ? 'text-indigo-200' : 'text-gray-500'}`}>
                {msg.timestamp.toLocaleTimeString()}
              </p>
            </div>
          </div>
        ))}
        {sending && (
          <div className="flex justify-start">
            <div className="rounded-xl bg-gray-700 px-4 py-2.5 text-sm text-gray-400">
              <span className="inline-flex gap-1">
                <span className="animate-bounce">·</span>
                <span className="animate-bounce" style={{ animationDelay: '0.1s' }}>·</span>
                <span className="animate-bounce" style={{ animationDelay: '0.2s' }}>·</span>
              </span>
            </div>
          </div>
        )}
      </div>

      {/* Message input */}
      <div className="border-t border-gray-700 p-4">
        <div className="flex gap-2">
          <input
            type="text"
            value={message}
            onChange={(e) => setMessage(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && handleSend()}
            placeholder="Send a test message to this agent…"
            disabled={sending}
            className="flex-1 rounded-lg border border-gray-600 bg-gray-700 px-3 py-2 text-sm text-white placeholder-gray-500 focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500 focus:outline-none disabled:opacity-50"
          />
          <button
            onClick={handleSend}
            disabled={!message.trim() || sending}
            className={`rounded-lg px-4 py-2 text-sm font-medium text-white transition-colors ${
              !message.trim() || sending
                ? 'bg-indigo-500/40 cursor-not-allowed'
                : 'bg-indigo-500 hover:bg-indigo-400'
            }`}
          >
            Send
          </button>
        </div>
      </div>
    </div>
  )
}

/* ------------------------------------------------------------------ */
/*  Register Agent Dialog                                              */
/* ------------------------------------------------------------------ */

function RegisterDialog({
  open,
  onClose,
  onRegister,
}: {
  open: boolean
  onClose: () => void
  onRegister: (uri: string) => void
}) {
  const [uri, setUri] = useState('')

  if (!open) return null

  const handleSubmit = () => {
    if (!uri.trim()) return
    onRegister(uri.trim())
    setUri('')
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60">
      <div className="w-full max-w-lg rounded-xl bg-gray-800 p-6 shadow-2xl border border-gray-700">
        <h3 className="text-lg font-semibold text-white">Register Agent</h3>
        <p className="mt-1 text-sm text-gray-400">
          Enter the base URL of an A2A-compatible agent. The agent card will be fetched from
          <code className="ml-1 text-xs bg-gray-700 px-1 py-0.5 rounded font-mono text-gray-300">
            /.well-known/agent-card.json
          </code>
        </p>
        <input
          type="url"
          value={uri}
          onChange={(e) => setUri(e.target.value)}
          onKeyDown={(e) => e.key === 'Enter' && handleSubmit()}
          placeholder="https://agent-host:port"
          className="mt-4 w-full rounded-lg border border-gray-600 bg-gray-700 px-3 py-2 text-sm text-white placeholder-gray-500 focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500 focus:outline-none font-mono"
          autoFocus
        />
        <div className="mt-6 flex justify-end gap-3">
          <button
            onClick={onClose}
            className="rounded-lg bg-gray-700 px-4 py-2 text-sm font-medium text-gray-300 hover:bg-gray-600 transition-colors"
          >
            Cancel
          </button>
          <button
            onClick={handleSubmit}
            disabled={!uri.trim()}
            className={`rounded-lg px-4 py-2 text-sm font-medium text-white transition-colors ${
              !uri.trim() ? 'bg-indigo-500/40 cursor-not-allowed' : 'bg-indigo-500 hover:bg-indigo-400'
            }`}
          >
            Register
          </button>
        </div>
      </div>
    </div>
  )
}

/* ------------------------------------------------------------------ */
/*  Main Page                                                          */
/* ------------------------------------------------------------------ */

export default function AgentsPage() {
  const [agents, setAgents] = useState<AgentCard[]>([])
  const [selectedUrl, setSelectedUrl] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [registerOpen, setRegisterOpen] = useState(false)
  const [sending, setSending] = useState(false)

  // Per-agent conversation history
  const [conversations, setConversations] = useState<
    Record<string, { role: string; text: string; timestamp: Date }[]>
  >({})

  // Per-agent contextId for multi-turn conversation continuity
  const [contextIds, setContextIds] = useState<Record<string, string>>({})

  // Toasts
  const [toasts, setToasts] = useState<Toast[]>([])

  const showToast = useCallback((message: string, type: 'success' | 'error') => {
    const id = ++toastId
    setToasts((prev) => [...prev, { id, message, type }])
    setTimeout(() => setToasts((prev) => prev.filter((t) => t.id !== id)), 3000)
  }, [])

  const dismissToast = useCallback((id: number) => {
    setToasts((prev) => prev.filter((t) => t.id !== id))
  }, [])

  const loadAgents = useCallback(async () => {
    try {
      setLoading(true)
      const data = await fetchAgents()
      setAgents(data)
      setError(null)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load agents')
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    loadAgents()
  }, [loadAgents])

  const handleRegister = useCallback(
    async (uri: string) => {
      try {
        await registerAgent(uri)
        showToast(`Agent registered from ${uri}`, 'success')
        setRegisterOpen(false)
        await loadAgents()
      } catch (err) {
        showToast(`Registration failed: ${err instanceof Error ? err.message : 'Unknown error'}`, 'error')
      }
    },
    [loadAgents, showToast],
  )

  const handleRefresh = useCallback(
    async (agentUrl: string) => {
      try {
        await refreshAgent(agentUrl)
        showToast('Agent card refreshed', 'success')
        await loadAgents()
      } catch (err) {
        showToast(`Refresh failed: ${err instanceof Error ? err.message : 'Unknown error'}`, 'error')
      }
    },
    [loadAgents, showToast],
  )

  const handleDelete = useCallback(
    async (agentUrl: string) => {
      if (!window.confirm(`Unregister agent "${agentUrl}"?`)) return
      try {
        await unregisterAgent(agentUrl)
        showToast('Agent unregistered', 'success')
        if (selectedUrl === agentUrl) setSelectedUrl(null)
        await loadAgents()
      } catch (err) {
        showToast(`Unregister failed: ${err instanceof Error ? err.message : 'Unknown error'}`, 'error')
      }
    },
    [loadAgents, selectedUrl, showToast],
  )

  const handleSendMessage = useCallback(
    async (message: string) => {
      if (!selectedUrl) return
      const agent = agents.find((a) => a.url === selectedUrl)
      if (!agent) return

      // Add user message to conversation
      setConversations((prev) => ({
        ...prev,
        [selectedUrl]: [
          ...(prev[selectedUrl] ?? []),
          { role: 'user', text: message, timestamp: new Date() },
        ],
      }))

      setSending(true)
      try {
        const currentContextId = contextIds[selectedUrl]
        const response = await sendA2AMessage(agent.url, message, currentContextId)

        // Track the contextId from the response for multi-turn continuity
        if (response?.contextId) {
          setContextIds((prev) => ({ ...prev, [selectedUrl]: response.contextId }))
        }

        const responseText =
          response?.parts?.map((p: A2AMessagePart) => p.text).filter(Boolean).join('\n') ??
          JSON.stringify(response, null, 2)

        setConversations((prev) => ({
          ...prev,
          [selectedUrl]: [
            ...(prev[selectedUrl] ?? []),
            { role: 'agent', text: responseText, timestamp: new Date() },
          ],
        }))
      } catch (err) {
        const errMsg = err instanceof Error ? err.message : 'Unknown error'
        setConversations((prev) => ({
          ...prev,
          [selectedUrl]: [
            ...(prev[selectedUrl] ?? []),
            { role: 'agent', text: `⚠ Error: ${errMsg}`, timestamp: new Date() },
          ],
        }))
        showToast(`Message failed: ${errMsg}`, 'error')
      } finally {
        setSending(false)
      }
    },
    [agents, selectedUrl, showToast],
  )

  const selectedAgent = agents.find((a) => a.url === selectedUrl) ?? null

  // ── Loading / Error ────────────────────────────────────────────

  if (loading) {
    return (
      <div className="flex h-[70vh] items-center justify-center text-gray-400">
        <svg className="mr-3 h-5 w-5 animate-spin text-indigo-500" viewBox="0 0 24 24" fill="none">
          <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
          <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z" />
        </svg>
        Loading agents…
      </div>
    )
  }

  if (error) {
    return (
      <div className="flex h-[70vh] items-center justify-center">
        <div className="rounded-xl bg-gray-800 p-8 text-center shadow-lg border border-gray-700">
          <p className="text-red-400 text-lg font-medium">Failed to load agents</p>
          <p className="mt-2 text-sm text-gray-500">{error}</p>
          <button
            onClick={loadAgents}
            className="mt-4 rounded-lg bg-indigo-500 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-400 transition-colors"
          >
            Retry
          </button>
        </div>
      </div>
    )
  }

  // ── Main layout ───────────────────────────────────────────────

  return (
    <div className="flex h-[calc(100vh-80px)] flex-col">
      <ToastContainer toasts={toasts} onDismiss={dismissToast} />
      <RegisterDialog open={registerOpen} onClose={() => setRegisterOpen(false)} onRegister={handleRegister} />

      {/* Header */}
      <header className="flex items-center justify-between border-b border-gray-700 px-6 py-4">
        <div>
          <h1 className="text-xl font-semibold">Agent Registry</h1>
          <p className="mt-0.5 text-sm text-gray-400">
            {agents.length} agent{agents.length !== 1 ? 's' : ''} registered
          </p>
        </div>
        <div className="flex gap-2">
          <button
            onClick={loadAgents}
            className="rounded-lg border border-gray-600 bg-gray-800 px-4 py-2 text-sm font-medium text-gray-300 hover:bg-gray-700 transition-colors"
          >
            ↻ Refresh
          </button>
          <button
            onClick={() => setRegisterOpen(true)}
            className="rounded-lg bg-indigo-500 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-400 transition-colors"
          >
            + Register Agent
          </button>
        </div>
      </header>

      {/* Body */}
      <div className="flex flex-1 overflow-hidden">
        {/* Agent list */}
        <div className="w-96 shrink-0 overflow-y-auto border-r border-gray-700 bg-gray-800/30 p-3 space-y-2">
          {agents.length === 0 && (
            <div className="flex h-full items-center justify-center">
              <div className="text-center">
                <p className="text-gray-500 text-sm">No agents registered</p>
                <button
                  onClick={() => setRegisterOpen(true)}
                  className="mt-3 rounded-lg bg-indigo-500/10 border border-indigo-500/30 px-4 py-2 text-sm text-indigo-400 hover:bg-indigo-500/20 transition-colors"
                >
                  Register your first agent
                </button>
              </div>
            </div>
          )}
          {agents.map((agent) => (
            <AgentCardView
              key={agent.url}
              agent={agent}
              selected={agent.url === selectedUrl}
              onSelect={() => setSelectedUrl(agent.url)}
              onRefresh={() => handleRefresh(agent.url)}
              onDelete={() => handleDelete(agent.url)}
            />
          ))}
        </div>

        {/* Detail / chat panel */}
        <div className="flex-1 overflow-hidden bg-gray-900">
          {!selectedAgent ? (
            <div className="flex h-full items-center justify-center text-gray-600 text-sm">
              Select an agent to view details and send test messages
            </div>
          ) : (
            <AgentDetailPanel
              agent={selectedAgent}
              onSendMessage={handleSendMessage}
              sending={sending}
              conversationHistory={conversations[selectedUrl!] ?? []}
            />
          )}
        </div>
      </div>
    </div>
  )
}
