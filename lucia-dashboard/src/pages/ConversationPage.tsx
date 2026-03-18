import { useState, useEffect, useRef, useCallback } from 'react'
import { MessageCircle, Trash2, Send, Zap, Brain } from 'lucide-react'
import ToastContainer from '../components/ToastContainer'
import { useToast } from '../hooks/useToast'

/* ------------------------------------------------------------------ */
/*  Types                                                              */
/* ------------------------------------------------------------------ */

interface HAContext {
  timestamp: string
  day_of_week: string
  location: string
  device_id: string
  device_area: string
  device_type: string
}

interface ConversationApiResponse {
  type: 'command' | 'llm'
  text: string
  command?: {
    skillId: string
    action: string
    confidence: number
    captures?: Record<string, string>
    executionMs: number
  }
  conversationId?: string
}

interface ChatMessage {
  role: 'user' | 'agent'
  text: string
  timestamp: Date
  meta?: string
}

/* ------------------------------------------------------------------ */
/*  Constants                                                          */
/* ------------------------------------------------------------------ */

const EXAMPLE_COMMANDS = [
  'Turn on the kitchen lights',
  'Set the thermostat to 72',
  'Make it warmer',
  'Activate the movie scene',
  'Set bedroom brightness to 50 percent',
  "What's the weather like?",
]

/* ------------------------------------------------------------------ */
/*  HA Simulation helpers                                              */
/* ------------------------------------------------------------------ */

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

/* ------------------------------------------------------------------ */
/*  API helper                                                         */
/* ------------------------------------------------------------------ */

async function sendConversation(
  text: string,
  context: HAContext | null,
  conversationId?: string,
): Promise<ConversationApiResponse> {
  const body: Record<string, unknown> = { text }

  if (context) {
    body.context = {
      timestamp: new Date().toISOString(),
      conversationId: conversationId ?? undefined,
      deviceId: context.device_id,
      deviceArea: context.device_area,
      deviceType: context.device_type,
      userId: 'dashboard-test',
      location: context.location,
    }
  } else {
    body.context = {
      timestamp: new Date().toISOString(),
      conversationId: conversationId ?? undefined,
      userId: 'dashboard-test',
    }
  }

  const res = await fetch('/api/conversation', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })

  if (!res.ok) {
    const errText = await res.text()
    throw new Error(errText || res.statusText)
  }

  const contentType = res.headers.get('content-type') ?? ''

  if (contentType.includes('text/event-stream')) {
    const reader = res.body?.getReader()
    if (!reader) throw new Error('No response body')

    const decoder = new TextDecoder()
    let fullText = ''
    let lastEvent: Record<string, unknown> | null = null

    while (true) {
      const { done, value } = await reader.read()
      if (done) break

      const chunk = decoder.decode(value, { stream: true })
      const lines = chunk.split('\n')

      for (const line of lines) {
        if (line.startsWith('data: ')) {
          try {
            lastEvent = JSON.parse(line.slice(6))
            if (lastEvent && typeof lastEvent.text === 'string') fullText = lastEvent.text
          } catch { /* ignore parse errors */ }
        }
      }
    }

    return {
      type: 'llm',
      text: fullText || (lastEvent?.text as string) || 'No response',
      conversationId: lastEvent?.conversationId as string | undefined,
    }
  }

  return await res.json()
}

/* ------------------------------------------------------------------ */
/*  HA Simulation Panel                                                */
/* ------------------------------------------------------------------ */

function HASimulationPanel({
  enabled,
  onToggle,
  context,
  onContextChange,
}: {
  enabled: boolean
  onToggle: (val: boolean) => void
  context: HAContext
  onContextChange: (ctx: HAContext) => void
}) {
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
          <button
            onClick={() => onContextChange(getDefaultHAContext())}
            className="rounded px-2 py-0.5 text-[10px] font-medium text-dust hover:text-fog transition-colors"
            title="Reset to current time/defaults"
          >
            Reset
          </button>
        )}
      </div>

      {/* Context fields */}
      {enabled && (
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
    </div>
  )
}

/* ------------------------------------------------------------------ */
/*  Main Page                                                          */
/* ------------------------------------------------------------------ */

export default function ConversationPage() {
  const [messages, setMessages] = useState<ChatMessage[]>([])
  const [input, setInput] = useState('')
  const [sending, setSending] = useState(false)
  const [conversationId, setConversationId] = useState<string | undefined>()
  const [lastResponseType, setLastResponseType] = useState<'command' | 'llm' | null>(null)

  // HA simulation state
  const [haEnabled, setHaEnabled] = useState(false)
  const [haContext, setHaContext] = useState<HAContext>(getDefaultHAContext)

  // Toasts
  const { toasts, addToast: showToast, dismissToast } = useToast()

  // Auto-scroll ref
  const chatEndRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    chatEndRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [messages, sending])

  const handleSend = useCallback(async (text?: string) => {
    const messageText = (text ?? input).trim()
    if (!messageText || sending) return

    // Clear input immediately
    if (!text) setInput('')

    // Add user message
    setMessages((prev) => [...prev, { role: 'user', text: messageText, timestamp: new Date() }])
    setSending(true)

    try {
      // Refresh HA timestamp to "now" on each send
      const ctx = haEnabled
        ? { ...haContext, timestamp: new Date().toISOString().replace('T', ' ').substring(0, 19) }
        : null
      if (ctx) setHaContext(ctx)

      const response = await sendConversation(messageText, ctx, conversationId)

      // Track conversation continuity
      if (response.conversationId) {
        setConversationId(response.conversationId)
      }

      setLastResponseType(response.type)

      // Build metadata line
      let meta = ''
      if (response.type === 'command' && response.command) {
        const c = response.command
        meta = `\uD83C\uDFCE\uFE0F ${c.skillId}/${c.action} \u2022 ${(c.confidence * 100).toFixed(0)}% \u2022 ${c.executionMs}ms`
      } else if (response.type === 'llm') {
        meta = '\uD83E\uDD16 LLM'
      }

      setMessages((prev) => [
        ...prev,
        { role: 'agent', text: response.text, timestamp: new Date(), meta },
      ])
    } catch (err) {
      const errMsg = err instanceof Error ? err.message : 'Unknown error'
      showToast(`Error: ${errMsg}`, 'error')
      setMessages((prev) => [
        ...prev,
        { role: 'agent', text: `Error: ${errMsg}`, timestamp: new Date() },
      ])
    } finally {
      setSending(false)
    }
  }, [input, sending, haEnabled, haContext, conversationId, showToast])

  const handleClear = useCallback(() => {
    setMessages([])
    setConversationId(undefined)
    setLastResponseType(null)
  }, [])

  return (
    <div className="flex h-[calc(100vh-7rem)] flex-col rounded-xl border border-stone bg-charcoal overflow-hidden">
      {/* Header */}
      <div className="flex items-center justify-between border-b border-stone px-4 py-3">
        <div className="flex items-center gap-2.5">
          <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-xl bg-amber-glow/20">
            <MessageCircle className="h-4 w-4 text-amber" />
          </div>
          <h2 className="text-base font-semibold text-light">Conversation</h2>
          {lastResponseType && (
            <span className="ml-2 inline-flex items-center gap-1 rounded-full bg-basalt/50 px-2.5 py-0.5 text-[11px] font-medium text-fog border border-stone/30">
              {lastResponseType === 'command' ? (
                <><Zap className="h-3 w-3 text-amber" /> Command</>
              ) : (
                <><Brain className="h-3 w-3 text-violet-400" /> LLM</>
              )}
            </span>
          )}
        </div>
        <button
          onClick={handleClear}
          className="flex items-center gap-1.5 rounded-xl px-3 py-1.5 text-xs font-medium text-dust hover:bg-basalt hover:text-fog transition-colors"
          title="Clear conversation"
        >
          <Trash2 className="h-3.5 w-3.5" />
          Clear
        </button>
      </div>

      {/* HA Simulation */}
      <HASimulationPanel
        enabled={haEnabled}
        onToggle={setHaEnabled}
        context={haContext}
        onContextChange={setHaContext}
      />

      {/* Quick test buttons */}
      <div className="border-b border-stone px-4 py-2 flex flex-wrap gap-1.5">
        {EXAMPLE_COMMANDS.map((cmd) => (
          <button
            key={cmd}
            onClick={() => handleSend(cmd)}
            disabled={sending}
            className="rounded-full bg-basalt px-2.5 py-1 text-[11px] text-fog hover:bg-amber-glow/20 hover:text-amber transition-colors cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed"
          >
            &ldquo;{cmd}&rdquo;
          </button>
        ))}
      </div>

      {/* Conversation area */}
      <div className="flex-1 overflow-y-auto p-4 md:p-6">
        <div className="space-y-3">
          {messages.length === 0 && (
            <div className="flex h-full items-center justify-center text-sm text-dust min-h-[200px]">
              Send a message to test the conversation endpoint
            </div>
          )}
          {messages.map((msg, i) => (
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
                {msg.meta && (
                  <p className="mt-1 text-[10px] text-dust font-mono">{msg.meta}</p>
                )}
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
                  <span className="animate-bounce">&middot;</span>
                  <span className="animate-bounce" style={{ animationDelay: '0.1s' }}>&middot;</span>
                  <span className="animate-bounce" style={{ animationDelay: '0.2s' }}>&middot;</span>
                </span>
              </div>
            </div>
          )}
          <div ref={chatEndRef} />
        </div>
      </div>

      {/* Message input */}
      <div className="border-t border-stone p-4 md:p-6">
        <div className="flex gap-2">
          <input
            type="text"
            value={input}
            onChange={(e) => setInput(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && handleSend()}
            placeholder="Send a test message..."
            disabled={sending}
            className="flex-1 rounded-xl border border-stone bg-basalt px-3 py-2.5 text-sm text-light placeholder-dust/60 input-focus focus:ring-1 focus:ring-amber disabled:opacity-50"
          />
          <button
            onClick={() => handleSend()}
            disabled={!input.trim() || sending}
            className={`flex items-center gap-1.5 rounded-xl px-5 py-2.5 text-sm font-medium transition-colors ${
              !input.trim() || sending
                ? 'bg-amber-glow/40 text-light/50 cursor-not-allowed'
                : 'bg-amber-glow text-void hover:brightness-110'
            }`}
          >
            <Send className="h-4 w-4" />
            Send
          </button>
        </div>
      </div>

      {/* Toasts */}
      <ToastContainer toasts={toasts} onDismiss={dismissToast} />
    </div>
  )
}
