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
          className={`flex items-center gap-3 rounded-xl px-4 py-3 shadow-lg text-sm font-medium transition-all duration-300 ${
            t.type === 'success' ? 'bg-sage/20 text-light' : 'bg-ember/20 text-light'
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
          ? 'bg-green-500/15 text-sage border border-green-500/30'
          : 'bg-basalt/50 text-dust border border-stone/30'
      }`}
    >
      <span className={`inline-block h-1.5 w-1.5 rounded-full ${enabled ? 'bg-green-400' : 'bg-stone'}`} />
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
          ? 'border-amber bg-amber-glow/5 shadow-lg shadow-amber/10'
          : 'border-stone bg-charcoal hover:border-stone'
      }`}
    >
      <div className="flex items-start justify-between">
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2">
            <h3 className="text-sm font-semibold text-light truncate">{agent.name}</h3>
            <span className="text-[10px] font-mono text-dust bg-basalt/50 px-1.5 py-0.5 rounded">
              v{agent.version}
            </span>
          </div>
          <p className="mt-1 text-xs text-dust line-clamp-2">{agent.description}</p>
          <p className="mt-1.5 text-[11px] font-mono text-dust truncate">{agent.url}</p>
        </div>
        <div className="flex shrink-0 gap-1 ml-3">
          <button
            onClick={(e) => { e.stopPropagation(); onRefresh() }}
            title="Refresh agent card"
            className="rounded-xl p-1.5 text-dust hover:bg-basalt hover:text-amber transition-colors"
          >
            ↻
          </button>
          <button
            onClick={(e) => { e.stopPropagation(); onDelete() }}
            title="Unregister agent"
            className="rounded-xl p-1.5 text-dust hover:bg-basalt hover:text-rose transition-colors"
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
              className="rounded bg-basalt px-2 py-0.5 text-[10px] font-medium text-fog"
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
    <div className={`border-b transition-colors ${enabled ? 'border-orange-500/30 bg-orange-500/5' : 'border-stone'}`}>
      {/* Toggle header */}
      <div className="flex items-center justify-between px-4 py-2">
        <div className="flex items-center gap-2">
          <button
            type="button"
            role="switch"
            aria-checked={enabled}
            onClick={() => onToggle(!enabled)}
            className={`relative inline-flex h-5 w-9 shrink-0 cursor-pointer rounded-full border-2 border-transparent transition-colors duration-200 ${
              enabled ? 'bg-orange-500' : 'bg-stone'
            }`}
          >
            <span
              className={`pointer-events-none inline-block h-4 w-4 transform rounded-full bg-white shadow transition duration-200 ${
                enabled ? 'translate-x-4' : 'translate-x-0'
              }`}
            />
          </button>
          <span className={`text-xs font-medium ${enabled ? 'text-orange-400' : 'text-dust'}`}>
            🏠 Simulate Home Assistant
          </span>
        </div>
        {enabled && (
          <div className="flex gap-1">
            <button
              onClick={() => { setShowPreview(!showPreview); setShowTemplate(false) }}
              className={`rounded px-2 py-0.5 text-[10px] font-medium transition-colors ${
                showPreview ? 'bg-orange-500/20 text-orange-400' : 'text-dust hover:text-fog'
              }`}
            >
              Preview
            </button>
            <button
              onClick={() => { setShowTemplate(!showTemplate); setShowPreview(false) }}
              className={`rounded px-2 py-0.5 text-[10px] font-medium transition-colors ${
                showTemplate ? 'bg-orange-500/20 text-orange-400' : 'text-dust hover:text-fog'
              }`}
            >
              Template
            </button>
            <button
              onClick={() => onContextChange(getDefaultHAContext())}
              className="rounded px-2 py-0.5 text-[10px] font-medium text-dust hover:text-fog transition-colors"
              title="Reset to current time/defaults"
            >
              Reset
            </button>
          </div>
        )}
      </div>

      {/* Context fields */}
      {enabled && !showPreview && !showTemplate && (
        <div className="px-4 pb-3 grid grid-cols-2 sm:grid-cols-3 gap-2">
          {contextFields.map((f) => (
            <div key={f.key}>
              <label className="block text-[10px] font-medium text-dust mb-0.5">{f.label}</label>
              <input
                type="text"
                value={context[f.key]}
                onChange={(e) => onContextChange({ ...context, [f.key]: e.target.value })}
                placeholder={f.placeholder}
                className="w-full rounded border border-stone bg-basalt px-2 py-1 text-[11px] text-light placeholder-dust/60 focus:border-orange-500 focus:ring-1 focus:ring-orange-500 input-focus font-mono"
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
            className="w-full rounded border border-stone bg-basalt px-3 py-2 text-[11px] text-light font-mono placeholder-dust/60 focus:border-orange-500 focus:ring-1 focus:ring-orange-500 input-focus resize-y"
          />
          <p className="mt-1 text-[10px] text-dust">
            Tokens: {'{{timestamp}}'} {'{{day_of_week}}'} {'{{location}}'} {'{{device_id}}'} {'{{device_area}}'} {'{{device_type}}'}
          </p>
        </div>
      )}

      {/* Rendered preview */}
      {enabled && showPreview && (
        <div className="px-4 pb-3">
          <pre className="rounded border border-stone bg-void/50 p-3 text-[11px] text-fog font-mono whitespace-pre-wrap overflow-auto max-h-48">
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
      <div className="border-b border-stone p-4 md:p-6">
        <div>
          <div className="flex items-center gap-3">
            <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-amber-glow/20 text-lg font-bold text-amber">
              {agent.name.charAt(0).toUpperCase()}
            </div>
            <div className="min-w-0 flex-1">
              <h2 className="text-base font-semibold text-light truncate">{agent.name}</h2>
              <p className="text-xs text-dust font-mono truncate">{agent.url}</p>
            </div>
          </div>
          <p className="mt-2 text-sm text-dust">{agent.description}</p>

          {/* Modes */}
          <div className="mt-3 flex gap-4 text-xs text-dust">
            <span>Input: {(agent.defaultInputModes ?? ['text']).join(', ')}</span>
            <span>Output: {(agent.defaultOutputModes ?? ['text']).join(', ')}</span>
          </div>

          {/* Skills detail */}
          {agent.skills && agent.skills.length > 0 && (
            <div className="mt-3 space-y-2">
              <h4 className="text-xs font-semibold text-dust uppercase tracking-wider">Skills</h4>
              <div className="grid gap-2 md:grid-cols-2">
                {agent.skills.map((skill) => (
                  <div key={skill.id} className="rounded-xl bg-basalt border border-stone p-3">
                    <div className="flex items-center gap-2">
                      <span className="text-sm font-medium text-light">{skill.name}</span>
                      {skill.tags?.map((tag) => (
                        <span key={tag} className="rounded bg-amber-glow/10 px-1.5 py-0.5 text-[10px] text-amber">
                          {tag}
                        </span>
                      ))}
                    </div>
                    <p className="mt-1 text-xs text-dust">{skill.description}</p>
                    {skill.examples && skill.examples.length > 0 && (
                      <div className="mt-2 flex flex-wrap gap-1">
                        {skill.examples.map((ex, i) => (
                          <button
                            key={i}
                            onClick={() => setMessage(ex)}
                            className="rounded-full bg-basalt px-2.5 py-1 text-[11px] text-fog hover:bg-amber-glow/20 hover:text-amber transition-colors cursor-pointer"
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
            </div>
          )}
        </div>
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
      <div className="flex-1 overflow-y-auto p-4 md:p-6">
        <div className="space-y-3">
          {conversationHistory.length === 0 && (
            <div className="flex h-full items-center justify-center text-sm text-dust min-h-[200px]">
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
                    ? 'bg-amber-glow text-light'
                    : 'bg-basalt text-light'
                }`}
              >
                <p className="whitespace-pre-wrap">{msg.text}</p>
                <p className={`mt-1 text-[10px] ${msg.role === 'user' ? 'text-amber' : 'text-dust'}`}>
                  {msg.timestamp.toLocaleTimeString()}
                </p>
              </div>
            </div>
          ))}
          {sending && (
            <div className="flex justify-start">
              <div className="rounded-xl bg-basalt px-4 py-2.5 text-sm text-dust">
                <span className="inline-flex gap-1">
                  <span className="animate-bounce">·</span>
                  <span className="animate-bounce" style={{ animationDelay: '0.1s' }}>·</span>
                  <span className="animate-bounce" style={{ animationDelay: '0.2s' }}>·</span>
                </span>
              </div>
            </div>
          )}
        </div>
      </div>

      {/* Message input */}
      <div className="border-t border-stone p-4 md:p-6">
        <div className="flex gap-2">
          <input
            type="text"
            value={message}
            onChange={(e) => setMessage(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && handleSend()}
            placeholder="Send a test message to this agent…"
            disabled={sending}
            className="flex-1 rounded-xl border border-stone bg-basalt px-3 py-2.5 text-sm text-light placeholder-dust/60 input-focus focus:ring-1 focus:ring-amber disabled:opacity-50"
          />
          <button
            onClick={handleSend}
            disabled={!message.trim() || sending}
            className={`rounded-xl px-5 py-2.5 text-sm font-medium transition-colors ${
              !message.trim() || sending
                ? 'bg-amber-glow/40 text-light/50 cursor-not-allowed'
                : 'bg-amber-glow text-void hover:brightness-110'
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
      <div className="w-full max-w-lg rounded-xl bg-charcoal p-6 shadow-2xl border border-stone">
        <h3 className="text-lg font-semibold text-light">Register Agent</h3>
        <p className="mt-1 text-sm text-dust">
          Enter the base URL of an A2A-compatible agent. The agent card will be fetched from
          <code className="ml-1 text-xs bg-basalt px-1 py-0.5 rounded font-mono text-fog">
            /.well-known/agent-card.json
          </code>
        </p>
        <input
          type="url"
          value={uri}
          onChange={(e) => setUri(e.target.value)}
          onKeyDown={(e) => e.key === 'Enter' && handleSubmit()}
          placeholder="https://agent-host:port"
          className="mt-4 w-full rounded-xl border border-stone bg-basalt px-3 py-2 text-sm text-light placeholder-dust/60 input-focus focus:ring-1 focus:ring-amber input-focus font-mono"
          autoFocus
        />
        <div className="mt-6 flex justify-end gap-3">
          <button
            onClick={onClose}
            className="rounded-xl bg-basalt px-4 py-2 text-sm font-medium text-fog hover:bg-stone transition-colors"
          >
            Cancel
          </button>
          <button
            onClick={handleSubmit}
            disabled={!uri.trim()}
            className={`rounded-xl px-4 py-2 text-sm font-medium text-light transition-colors ${
              !uri.trim() ? 'bg-amber-glow/40 cursor-not-allowed' : 'bg-amber-glow hover:bg-amber-glow'
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
      <div className="flex h-[70vh] items-center justify-center text-dust">
        <svg className="mr-3 h-5 w-5 animate-spin text-amber" viewBox="0 0 24 24" fill="none">
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
        <div className="rounded-xl bg-charcoal p-8 text-center shadow-lg border border-stone">
          <p className="text-rose text-lg font-medium">Failed to load agents</p>
          <p className="mt-2 text-sm text-dust">{error}</p>
          <button
            onClick={loadAgents}
            className="mt-4 rounded-xl bg-amber-glow px-4 py-2 text-sm font-medium text-light hover:bg-amber-glow transition-colors"
          >
            Retry
          </button>
        </div>
      </div>
    )
  }

  // ── Main layout ───────────────────────────────────────────────

  // On mobile, when an agent is selected we show the detail panel instead of the list
  const showMobileDetail = selectedAgent !== null

  return (
    <div className="-mx-4 -my-6 sm:-mx-6 lg:-mx-8 flex h-[calc(100vh-80px)] flex-col">
      <ToastContainer toasts={toasts} onDismiss={dismissToast} />
      <RegisterDialog open={registerOpen} onClose={() => setRegisterOpen(false)} onRegister={handleRegister} />

      {/* Header — stacks on mobile */}
      <header className="border-b border-stone px-4 py-3 md:px-6 md:py-4">
        {/* Mobile: back button when viewing agent detail */}
        {showMobileDetail && (
          <button
            onClick={() => setSelectedUrl(null)}
            className="mb-2 flex items-center gap-1.5 text-sm text-amber hover:text-light transition-colors md:hidden"
          >
            ← Back to agents
          </button>
        )}
        <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
          <div>
            <h1 className="font-display text-xl font-semibold text-light">Agent Registry</h1>
            <p className="mt-0.5 text-sm text-dust">
              {agents.length} agent{agents.length !== 1 ? 's' : ''} registered
            </p>
          </div>
          <div className="flex gap-2">
            <button
              onClick={loadAgents}
              className="rounded-xl border border-stone bg-charcoal px-3 py-2 text-sm font-medium text-fog hover:bg-basalt transition-colors"
            >
              ↻ Refresh
            </button>
            <button
              onClick={() => setRegisterOpen(true)}
              className="rounded-xl bg-amber-glow px-3 py-2 text-sm font-medium text-void hover:brightness-110 transition-all"
            >
              + Register Agent
            </button>
          </div>
        </div>
      </header>

      {/* Body — side-by-side on desktop, toggle on mobile */}
      <div className="flex flex-1 overflow-hidden">
        {/* Agent list — hidden on mobile when an agent is selected */}
        <div className={`w-full md:w-[30%] md:min-w-[320px] md:max-w-[500px] md:shrink-0 overflow-y-auto md:border-r border-stone bg-void/30 p-3 space-y-2 ${showMobileDetail ? 'hidden md:block' : ''}`}>
          {agents.length === 0 && (
            <div className="flex h-full items-center justify-center">
              <div className="text-center">
                <p className="text-dust text-sm">No agents registered</p>
                <button
                  onClick={() => setRegisterOpen(true)}
                  className="mt-3 rounded-xl bg-amber-glow/10 border border-amber/30 px-4 py-2 text-sm text-amber hover:bg-amber-glow/20 transition-colors"
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

        {/* Detail / chat panel — full width on mobile, flex-1 on desktop */}
        <div className={`w-full md:flex-1 overflow-hidden bg-void/50 ${!showMobileDetail ? 'hidden md:block' : ''}`}>
          {!selectedAgent ? (
            <div className="flex h-full items-center justify-center text-dust text-sm">
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
