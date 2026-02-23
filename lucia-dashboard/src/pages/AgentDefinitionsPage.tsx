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
      <div className="mb-6 flex items-center justify-between">
        <h1 className="text-2xl font-bold">Agent Definitions</h1>
        <div className="flex gap-2">
          <button
            onClick={handleReload}
            className="rounded bg-gray-700 px-4 py-2 text-sm font-medium text-gray-300 hover:bg-gray-600"
          >
            ↻ Reload Agents
          </button>
          <button
            onClick={() => setMode('create')}
            className="rounded bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-500"
          >
            + New Agent
          </button>
        </div>
      </div>

      {error && (
        <div className="mb-4 rounded bg-red-900/50 px-4 py-2 text-red-300">
          {error}
          <button onClick={() => setError(null)} className="ml-2 text-red-400 hover:text-red-200">✕</button>
        </div>
      )}

      {loading ? (
        <div className="text-gray-400">Loading...</div>
      ) : definitions.length === 0 ? (
        <div className="rounded border border-gray-700 bg-gray-800 p-8 text-center text-gray-400">
          No custom agents defined. Click "New Agent" to create one.
        </div>
      ) : (
        <div className="space-y-4">
          {definitions.map(def => (
            <div key={def.id} className="rounded border border-gray-700 bg-gray-800 p-4">
              <div className="flex items-start justify-between">
                <div>
                  <div className="flex items-center gap-3">
                    <h3 className="text-lg font-semibold">{def.displayName || def.name}</h3>
                    <code className="text-xs text-gray-500">{def.name}</code>
                    {!def.enabled && (
                      <span className="rounded bg-yellow-900/50 px-2 py-0.5 text-xs text-yellow-400">disabled</span>
                    )}
                  </div>
                  <p className="mt-1 text-sm text-gray-400">{def.description || 'No description'}</p>
                  <div className="mt-2 flex flex-wrap gap-1">
                    {def.tools.map((tool, i) => (
                      <span key={i} className="rounded bg-indigo-900/40 px-2 py-0.5 text-xs text-indigo-300">
                        {tool.serverId}/{tool.toolName}
                      </span>
                    ))}
                    {def.tools.length === 0 && (
                      <span className="text-xs text-gray-500">No tools assigned</span>
                    )}
                  </div>
                  {def.modelConnectionName && (
                    <p className="mt-1 text-xs text-gray-500">Model: {def.modelConnectionName}</p>
                  )}
                </div>
                <div className="flex gap-2">
                  <button
                    onClick={() => { setEditingDef(def); setMode('edit') }}
                    className="rounded bg-gray-700 px-3 py-1 text-xs hover:bg-gray-600"
                  >
                    Edit
                  </button>
                  <button
                    onClick={() => handleDelete(def.id)}
                    className="rounded bg-red-900/50 px-3 py-1 text-xs text-red-300 hover:bg-red-800"
                  >
                    Delete
                  </button>
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
    enabled: definition?.enabled ?? true,
  })
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
      <h1 className="mb-6 text-2xl font-bold">{definition ? 'Edit' : 'Create'} Agent Definition</h1>

      {error && (
        <div className="mb-4 rounded bg-red-900/50 px-4 py-2 text-red-300">{error}</div>
      )}

      <form onSubmit={handleSubmit} className="space-y-4">
        <div className="rounded border border-gray-700 bg-gray-800 p-6 space-y-4">
          <h2 className="text-lg font-semibold text-gray-200">Agent Details</h2>
          <div className="grid grid-cols-2 gap-4">
            <label className="block">
              <span className="text-sm text-gray-400">Agent ID</span>
              <input
                value={form.id}
                onChange={e => setForm(f => ({ ...f, id: e.target.value }))}
                placeholder="Auto-generated from name"
                disabled={!!definition}
                className="mt-1 block w-full rounded border border-gray-600 bg-gray-700 px-3 py-2 text-sm disabled:opacity-50"
              />
            </label>
            <label className="block">
              <span className="text-sm text-gray-400">Name *</span>
              <input
                value={form.name}
                onChange={e => setForm(f => ({ ...f, name: e.target.value }))}
                required
                placeholder="e.g. research-agent"
                className="mt-1 block w-full rounded border border-gray-600 bg-gray-700 px-3 py-2 text-sm"
              />
            </label>
          </div>

          <div className="grid grid-cols-2 gap-4">
            <label className="block">
              <span className="text-sm text-gray-400">Display Name</span>
              <input
                value={form.displayName}
                onChange={e => setForm(f => ({ ...f, displayName: e.target.value }))}
                placeholder="Research Agent"
                className="mt-1 block w-full rounded border border-gray-600 bg-gray-700 px-3 py-2 text-sm"
              />
            </label>
            <label className="block">
              <span className="text-sm text-gray-400">Model Provider</span>
              <select
                value={form.modelConnectionName}
                onChange={e => setForm(f => ({ ...f, modelConnectionName: e.target.value }))}
                className="mt-1 block w-full rounded border border-gray-600 bg-gray-700 px-3 py-2 text-sm"
              >
                <option value="">Default (system model)</option>
                {providers.map(p => (
                  <option key={p.id} value={p.id}>
                    {p.name} ({p.providerType} · {p.modelName})
                  </option>
                ))}
              </select>
            </label>
          </div>

          <label className="block">
            <span className="text-sm text-gray-400">Description</span>
            <input
              value={form.description}
              onChange={e => setForm(f => ({ ...f, description: e.target.value }))}
              placeholder="What does this agent do?"
              className="mt-1 block w-full rounded border border-gray-600 bg-gray-700 px-3 py-2 text-sm"
            />
          </label>

          <label className="block">
            <span className="text-sm text-gray-400">Instructions (System Prompt)</span>
            <textarea
              value={form.instructions}
              onChange={e => setForm(f => ({ ...f, instructions: e.target.value }))}
              rows={6}
              placeholder="You are a helpful assistant that..."
              className="mt-1 block w-full rounded border border-gray-600 bg-gray-700 px-3 py-2 text-sm font-mono"
            />
          </label>

          <label className="flex items-center gap-2">
            <input
              type="checkbox"
              checked={form.enabled}
              onChange={e => setForm(f => ({ ...f, enabled: e.target.checked }))}
              className="h-4 w-4"
            />
            <span className="text-sm text-gray-400">Enabled</span>
          </label>
        </div>

        {/* MCP Tool Picker */}
        <div className="rounded border border-gray-700 bg-gray-800 p-6 space-y-4">
          <h2 className="text-lg font-semibold text-gray-200">
            MCP Tools
            {selectedTools.length > 0 && (
              <span className="ml-2 text-sm font-normal text-gray-400">
                ({selectedTools.length} selected)
              </span>
            )}
          </h2>

          {mcpServers.length === 0 ? (
            <p className="text-sm text-gray-500">
              No MCP servers configured. Add servers in the MCP Servers page first.
            </p>
          ) : (
            <div className="space-y-4">
              {mcpServers.map(server => {
                const status = serverStatuses[server.id]
                const tools = serverTools[server.id] ?? []
                return (
                  <div key={server.id} className="rounded border border-gray-600 bg-gray-750 p-3">
                    <div className="flex items-center gap-2 mb-2">
                      <h3 className="text-sm font-medium">{server.name}</h3>
                      <span className={`rounded px-1.5 py-0.5 text-xs ${
                        status?.state === 'Connected'
                          ? 'bg-green-900/50 text-green-400'
                          : 'bg-gray-700 text-gray-400'
                      }`}>
                        {status?.state ?? 'Disconnected'}
                      </span>
                    </div>
                    {tools.length > 0 ? (
                      <div className="grid gap-1">
                        {tools.map(tool => (
                          <label key={tool.toolName} className="flex items-center gap-2 cursor-pointer rounded px-2 py-1 hover:bg-gray-700">
                            <input
                              type="checkbox"
                              checked={isToolSelected(server.id, tool.toolName)}
                              onChange={() => toggleTool(server.id, tool.toolName)}
                              className="h-3.5 w-3.5"
                            />
                            <code className="text-xs font-mono text-indigo-400">{tool.toolName}</code>
                            <span className="text-xs text-gray-500 truncate">{tool.description || ''}</span>
                          </label>
                        ))}
                      </div>
                    ) : (
                      <p className="text-xs text-gray-500">
                        {status?.state === 'Connected' ? 'No tools found' : 'Server not connected'}
                      </p>
                    )}
                  </div>
                )
              })}
            </div>
          )}
        </div>

        <div className="flex gap-3 pt-2">
          <button
            type="submit"
            disabled={saving}
            className="rounded bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-500 disabled:opacity-50"
          >
            {saving ? 'Saving...' : definition ? 'Update Agent' : 'Create Agent'}
          </button>
          <button
            type="button"
            onClick={onCancel}
            className="rounded bg-gray-700 px-4 py-2 text-sm text-gray-300 hover:bg-gray-600"
          >
            Cancel
          </button>
        </div>
      </form>
    </div>
  )
}
