import { useState, useEffect, useCallback } from 'react'
import type { AgentDefinition, AgentToolReference, McpToolInfo, ModelProvider } from '../types'
import {
  fetchAgentDefinitions,
  createAgentDefinition,
  updateAgentDefinition,
  deleteAgentDefinition,
  reloadDynamicAgents,
  fetchMcpServers,
  discoverMcpTools,
  fetchMcpServerStatuses,
  fetchModelProviders,
} from '../api'
import type { McpToolServerDefinition, McpServerStatus } from '../types'

type FormMode = 'list' | 'create' | 'edit'

export default function AgentDefinitionsPage() {
  const [definitions, setDefinitions] = useState<AgentDefinition[]>([])
  const [mode, setMode] = useState<FormMode>('list')
  const [editingDef, setEditingDef] = useState<AgentDefinition | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [providers, setProviders] = useState<ModelProvider[]>([])

  const loadData = useCallback(async () => {
    try {
      setLoading(true)
      const [data, providerData] = await Promise.all([
        fetchAgentDefinitions(),
        fetchModelProviders(),
      ])
      setDefinitions(data)
      setProviders(providerData.filter(p => p.enabled))
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load data')
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => { loadData() }, [loadData])

  const handleDelete = async (id: string) => {
    if (!confirm('Delete this agent definition?')) return
    try {
      await deleteAgentDefinition(id)
      await loadData()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to delete')
    }
  }

  const handleReload = async () => {
    try {
      await reloadDynamicAgents()
      setError(null)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to reload')
    }
  }

  if (mode === 'create' || mode === 'edit') {
    return (
      <AgentForm
        definition={editingDef}
        providers={providers}
        onSave={async (def) => {
          if (mode === 'edit' && editingDef) {
            await updateAgentDefinition(editingDef.id, def)
          } else {
            await createAgentDefinition(def)
          }
          setMode('list')
          setEditingDef(null)
          await loadData()
        }}
        onCancel={() => { setMode('list'); setEditingDef(null) }}
      />
    )
  }

  return (
    <div>
      <div className="mb-6 flex flex-wrap items-center justify-between gap-3">
        <h1 className="font-display text-2xl font-bold text-light">Agent Definitions</h1>
        <div className="flex flex-wrap gap-2">
          <button
            onClick={handleReload}
            className="rounded bg-basalt px-4 py-2 text-sm font-medium text-fog hover:bg-stone"
          >
            ↻ Reload Agents
          </button>
          <button
            onClick={() => setMode('create')}
            className="rounded bg-amber px-4 py-2 text-sm font-medium text-void hover:bg-amber-glow"
          >
            + New Agent
          </button>
        </div>
      </div>

      {error && (
        <div className="mb-4 rounded bg-ember/15 px-4 py-2 text-rose">
          {error}
          <button onClick={() => setError(null)} className="ml-2 text-rose hover:text-rose">✕</button>
        </div>
      )}

      {loading ? (
        <div className="text-dust">Loading...</div>
      ) : definitions.length === 0 ? (
        <div className="rounded border border-stone bg-charcoal p-8 text-center text-dust">
          No custom agents defined. Click "New Agent" to create one.
        </div>
      ) : (
        <div className="space-y-4">
          {definitions.map(def => (
            <div key={def.id} className="rounded border border-stone bg-charcoal p-4">
              <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                <div className="min-w-0">
                  <div className="flex flex-wrap items-center gap-2">
                    <h3 className="text-lg font-semibold">{def.displayName || def.name}</h3>
                    <code className="text-xs text-dust">{def.name}</code>
                    {def.isBuiltIn && (
                      <span className="whitespace-nowrap rounded bg-blue-900/50 px-2 py-0.5 text-xs text-amber">System</span>
                    )}
                    {def.isOrchestrator && (
                      <span className="whitespace-nowrap rounded bg-amber-900/50 px-2 py-0.5 text-xs text-amber-300">Orchestrator</span>
                    )}
                    {def.isRemote && (
                      <span className="whitespace-nowrap rounded bg-purple-900/50 px-2 py-0.5 text-xs text-rose">Remote</span>
                    )}
                    {!def.enabled && (
                      <span className="whitespace-nowrap rounded bg-yellow-900/50 px-2 py-0.5 text-xs text-amber">disabled</span>
                    )}
                  </div>
                  <p className="mt-1 text-sm text-dust">{def.description || 'No description'}</p>
                  <div className="mt-2 flex flex-wrap gap-1">
                    {def.tools.map((tool, i) => (
                      <span key={i} className="whitespace-nowrap rounded bg-amber/15 px-2 py-0.5 text-xs text-amber">
                        {tool.serverId}/{tool.toolName}
                      </span>
                    ))}
                    {def.tools.length === 0 && (
                      <span className="text-xs text-dust">No tools assigned</span>
                    )}
                  </div>
                  {def.modelConnectionName && (
                    <p className="mt-1 text-xs text-dust">Model: {def.modelConnectionName}</p>
                  )}
                  {def.embeddingProviderName && (
                    <p className="mt-1 text-xs text-rose">Embedding: {def.embeddingProviderName}</p>
                  )}
                </div>
                <div className="flex shrink-0 gap-2">
                  <button
                    onClick={() => { setEditingDef(def); setMode('edit') }}
                    className="rounded bg-basalt px-3 py-1 text-xs hover:bg-stone"
                  >
                    Edit
                  </button>
                  {!def.isBuiltIn && (
                    <button
                      onClick={() => handleDelete(def.id)}
                      className="rounded bg-ember/15 px-3 py-1 text-xs text-rose hover:bg-red-800"
                    >
                      Delete
                    </button>
                  )}
                </div>
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  )
}

function AgentForm({
  definition,
  providers,
  onSave,
  onCancel,
}: {
  definition: AgentDefinition | null
  providers: ModelProvider[]
  onSave: (def: Partial<AgentDefinition>) => Promise<void>
  onCancel: () => void
}) {
  const [form, setForm] = useState({
    id: definition?.id ?? '',
    name: definition?.name ?? '',
    displayName: definition?.displayName ?? '',
    description: definition?.description ?? '',
    instructions: definition?.instructions ?? '',
    modelConnectionName: definition?.modelConnectionName ?? '',
    embeddingProviderName: definition?.embeddingProviderName ?? '',
    enabled: definition?.enabled ?? true,
  })
  const isBuiltIn = definition?.isBuiltIn ?? false
  const [selectedTools, setSelectedTools] = useState<AgentToolReference[]>(definition?.tools ?? [])
  const [mcpServers, setMcpServers] = useState<McpToolServerDefinition[]>([])
  const [serverStatuses, setServerStatuses] = useState<Record<string, McpServerStatus>>({})
  const [serverTools, setServerTools] = useState<Record<string, McpToolInfo[]>>({})
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const load = async () => {
      try {
        const [servers, statuses] = await Promise.all([
          fetchMcpServers(),
          fetchMcpServerStatuses(),
        ])
        setMcpServers(servers)
        setServerStatuses(statuses)

        // Auto-discover tools from connected servers
        for (const server of servers) {
          const status = statuses[server.id]
          if (status?.state === 'Connected') {
            try {
              const tools = await discoverMcpTools(server.id)
              setServerTools(prev => ({ ...prev, [server.id]: tools }))
            } catch {
              // Ignore discovery failures for individual servers
            }
          }
        }
      } catch {
        // Non-critical: tool picker won't be populated
      }
    }
    load()
  }, [])

  const toggleTool = (serverId: string, toolName: string) => {
    setSelectedTools(prev => {
      const exists = prev.some(t => t.serverId === serverId && t.toolName === toolName)
      if (exists) {
        return prev.filter(t => !(t.serverId === serverId && t.toolName === toolName))
      }
      return [...prev, { serverId, toolName }]
    })
  }

  const isToolSelected = (serverId: string, toolName: string) =>
    selectedTools.some(t => t.serverId === serverId && t.toolName === toolName)

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setSaving(true)
    setError(null)
    try {
      await onSave({
        id: form.id || form.name.toLowerCase().replace(/[^a-z0-9]+/g, '-'),
        name: form.name || form.id,
        displayName: form.displayName || undefined,
        description: form.description || undefined,
        instructions: form.instructions || undefined,
        tools: selectedTools,
        modelConnectionName: form.modelConnectionName || undefined,
        embeddingProviderName: form.embeddingProviderName || undefined,
        enabled: form.enabled,
      })
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to save')
    } finally {
      setSaving(false)
    }
  }

  return (
    <div>
      <div className="mb-6">
        <h1 className="text-2xl font-bold">
          {isBuiltIn ? 'Configure' : definition ? 'Edit' : 'Create'} Agent Definition
        </h1>
        {isBuiltIn && (
          <div className="mt-2 flex flex-wrap gap-2">
            <span className="whitespace-nowrap rounded bg-blue-900/50 px-2 py-1 text-sm font-normal text-amber">System Agent</span>
          </div>
        )}
      </div>

      {error && (
        <div className="mb-4 rounded bg-ember/15 px-4 py-2 text-rose">{error}</div>
      )}

      <form onSubmit={handleSubmit} className="space-y-4">
        <div className="rounded border border-stone bg-charcoal p-6 space-y-4">
          <h2 className="text-lg font-semibold text-light">Agent Details</h2>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <label className="block">
              <span className="text-sm text-dust">Agent ID</span>
              <input
                value={form.id}
                onChange={e => setForm(f => ({ ...f, id: e.target.value }))}
                placeholder="Auto-generated from name"
                disabled={!!definition || isBuiltIn}
                className="mt-1 block w-full rounded border border-stone bg-basalt px-3 py-2 text-sm disabled:opacity-50"
              />
            </label>
            <label className="block">
              <span className="text-sm text-dust">Name *</span>
              <input
                value={form.name}
                onChange={e => setForm(f => ({ ...f, name: e.target.value }))}
                required
                placeholder="e.g. research-agent"
                disabled={isBuiltIn}
                className="mt-1 block w-full rounded border border-stone bg-basalt px-3 py-2 text-sm disabled:opacity-50"
              />
            </label>
          </div>

          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <label className="block">
              <span className="text-sm text-dust">Display Name</span>
              <input
                value={form.displayName}
                onChange={e => setForm(f => ({ ...f, displayName: e.target.value }))}
                placeholder="Research Agent"
                disabled={isBuiltIn}
                className="mt-1 block w-full rounded border border-stone bg-basalt px-3 py-2 text-sm disabled:opacity-50"
              />
            </label>
            <label className="block">
              <span className="text-sm text-dust">Model Provider</span>
              <select
                value={form.modelConnectionName}
                onChange={e => setForm(f => ({ ...f, modelConnectionName: e.target.value }))}
                className="mt-1 block w-full rounded border border-stone bg-basalt px-3 py-2 text-sm"
              >
                <option value="">— Select a model provider —</option>
                {providers.filter(p => p.purpose !== 'Embedding').map(p => (
                  <option key={p.id} value={p.id}>
                    {p.name} ({p.providerType} · {p.modelName})
                  </option>
                ))}
              </select>
            </label>
            <label className="block">
              <span className="text-sm text-rose">Embedding Provider</span>
              <select
                value={form.embeddingProviderName}
                onChange={e => setForm(f => ({ ...f, embeddingProviderName: e.target.value }))}
                className="mt-1 block w-full rounded border border-stone bg-basalt px-3 py-2 text-sm"
              >
                <option value="">— Select an embedding provider —</option>
                {providers.filter(p => p.purpose === 'Embedding').map(p => (
                  <option key={p.id} value={p.id}>
                    {p.name} ({p.providerType} · {p.modelName})
                  </option>
                ))}
              </select>
              <p className="mt-1 text-xs text-dust">Used by skills for vector search (device matching, semantic lookup)</p>
            </label>
          </div>

          <label className="block">
            <span className="text-sm text-dust">Description</span>
            <input
              value={form.description}
              onChange={e => setForm(f => ({ ...f, description: e.target.value }))}
              placeholder="What does this agent do?"
              disabled={isBuiltIn}
              className="mt-1 block w-full rounded border border-stone bg-basalt px-3 py-2 text-sm disabled:opacity-50"
            />
          </label>

          <label className="block">
            <span className="text-sm text-dust">Instructions (System Prompt Override)</span>
            <textarea
              value={form.instructions}
              onChange={e => setForm(f => ({ ...f, instructions: e.target.value }))}
              rows={6}
              placeholder={isBuiltIn ? "Leave empty to use the agent's built-in instructions" : "You are a helpful assistant that..."}
              className="mt-1 block w-full rounded border border-stone bg-basalt px-3 py-2 text-sm font-mono"
            />
            {isBuiltIn && (
              <p className="mt-1 text-xs text-dust">Leave empty to use the agent's built-in instructions. Any text here will override them.</p>
            )}
          </label>

          <label className="flex items-center gap-2">
            <input
              type="checkbox"
              checked={form.enabled}
              onChange={e => setForm(f => ({ ...f, enabled: e.target.checked }))}
              disabled={isBuiltIn}
              className="h-4 w-4"
            />
            <span className="text-sm text-dust">Enabled</span>
          </label>
        </div>

        {/* MCP Tool Picker — hidden for built-in agents (tools come from code) */}
        {!isBuiltIn && (
        <div className="rounded border border-stone bg-charcoal p-6 space-y-4">
          <h2 className="text-lg font-semibold text-light">
            MCP Tools
            {selectedTools.length > 0 && (
              <span className="ml-2 text-sm font-normal text-dust">
                ({selectedTools.length} selected)
              </span>
            )}
          </h2>

          {mcpServers.length === 0 ? (
            <p className="text-sm text-dust">
              No MCP servers configured. Add servers in the MCP Servers page first.
            </p>
          ) : (
            <div className="space-y-4">
              {mcpServers.map(server => {
                const status = serverStatuses[server.id]
                const tools = serverTools[server.id] ?? []
                return (
                  <div key={server.id} className="rounded border border-stone bg-basalt p-3">
                    <div className="flex items-center gap-2 mb-2">
                      <h3 className="text-sm font-medium">{server.name}</h3>
                      <span className={`rounded px-1.5 py-0.5 text-xs ${
                        status?.state === 'Connected'
                          ? 'bg-green-900/50 text-sage'
                          : 'bg-basalt text-dust'
                      }`}>
                        {status?.state ?? 'Disconnected'}
                      </span>
                    </div>
                    {tools.length > 0 ? (
                      <div className="grid gap-1">
                        {tools.map(tool => (
                          <label key={tool.toolName} className="flex items-center gap-2 cursor-pointer rounded px-2 py-1 hover:bg-basalt">
                            <input
                              type="checkbox"
                              checked={isToolSelected(server.id, tool.toolName)}
                              onChange={() => toggleTool(server.id, tool.toolName)}
                              className="h-3.5 w-3.5"
                            />
                            <code className="text-xs font-mono text-amber">{tool.toolName}</code>
                            <span className="text-xs text-dust truncate">{tool.description || ''}</span>
                          </label>
                        ))}
                      </div>
                    ) : (
                      <p className="text-xs text-dust">
                        {status?.state === 'Connected' ? 'No tools found' : 'Server not connected'}
                      </p>
                    )}
                  </div>
                )
              })}
            </div>
          )}
        </div>
        )}

        <div className="flex gap-3 pt-2">
          <button
            type="submit"
            disabled={saving}
            className="rounded bg-amber px-4 py-2 text-sm font-medium text-void hover:bg-amber-glow disabled:opacity-50"
          >
            {saving ? 'Saving...' : definition ? 'Update Agent' : 'Create Agent'}
          </button>
          <button
            type="button"
            onClick={onCancel}
            className="rounded bg-basalt px-4 py-2 text-sm text-fog hover:bg-stone"
          >
            Cancel
          </button>
        </div>
      </form>
    </div>
  )
}
