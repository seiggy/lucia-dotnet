import { useState, useEffect, useCallback } from 'react'
import type { McpToolServerDefinition, McpServerStatus, McpToolInfo } from '../types'
import {
  fetchMcpServers,
  createMcpServer,
  updateMcpServer,
  deleteMcpServer,
  discoverMcpTools,
  connectMcpServer,
  disconnectMcpServer,
  fetchMcpServerStatuses,
} from '../api'

type FormMode = 'list' | 'create' | 'edit'

export default function McpServersPage() {
  const [servers, setServers] = useState<McpToolServerDefinition[]>([])
  const [statuses, setStatuses] = useState<Record<string, McpServerStatus>>({})
  const [mode, setMode] = useState<FormMode>('list')
  const [editingServer, setEditingServer] = useState<McpToolServerDefinition | null>(null)
  const [discoveredTools, setDiscoveredTools] = useState<Record<string, McpToolInfo[]>>({})
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const loadData = useCallback(async () => {
    try {
      setLoading(true)
      const [serversData, statusData] = await Promise.all([
        fetchMcpServers(),
        fetchMcpServerStatuses(),
      ])
      setServers(serversData)
      setStatuses(statusData)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load data')
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => { loadData() }, [loadData])

  const handleDiscover = async (serverId: string) => {
    try {
      const tools = await discoverMcpTools(serverId)
      setDiscoveredTools(prev => ({ ...prev, [serverId]: tools }))
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to discover tools')
    }
  }

  const handleConnect = async (id: string) => {
    try {
      await connectMcpServer(id)
      await loadData()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to connect')
    }
  }

  const handleDisconnect = async (id: string) => {
    try {
      await disconnectMcpServer(id)
      await loadData()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to disconnect')
    }
  }

  const handleDelete = async (id: string) => {
    if (!confirm('Delete this MCP server?')) return
    try {
      await deleteMcpServer(id)
      await loadData()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to delete')
    }
  }

  if (mode === 'create' || mode === 'edit') {
    return (
      <ServerForm
        server={editingServer}
        onSave={async (server) => {
          if (mode === 'edit' && editingServer) {
            await updateMcpServer(editingServer.id, server)
          } else {
            await createMcpServer(server)
          }
          setMode('list')
          setEditingServer(null)
          await loadData()
        }}
        onCancel={() => { setMode('list'); setEditingServer(null) }}
      />
    )
  }

  return (
    <div>
      <div className="mb-6 flex items-center justify-between">
        <h1 className="text-2xl font-bold">MCP Tool Servers</h1>
        <button
          onClick={() => setMode('create')}
          className="rounded bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-500"
        >
          + Add Server
        </button>
      </div>

      {error && (
        <div className="mb-4 rounded bg-red-900/50 px-4 py-2 text-red-300">
          {error}
          <button onClick={() => setError(null)} className="ml-2 text-red-400 hover:text-red-200">✕</button>
        </div>
      )}

      {loading ? (
        <div className="text-gray-400">Loading...</div>
      ) : servers.length === 0 ? (
        <div className="rounded border border-gray-700 bg-gray-800 p-8 text-center text-gray-400">
          No MCP servers configured. Click "Add Server" to register one.
        </div>
      ) : (
        <div className="space-y-4">
          {servers.map(server => {
            const status = statuses[server.id]
            const tools = discoveredTools[server.id]
            return (
              <div key={server.id} className="rounded border border-gray-700 bg-gray-800 p-4">
                <div className="flex items-start justify-between">
                  <div>
                    <div className="flex items-center gap-3">
                      <h3 className="text-lg font-semibold">{server.name}</h3>
                      <StatusBadge state={status?.state ?? 'Disconnected'} />
                      <span className="rounded bg-gray-700 px-2 py-0.5 text-xs text-gray-300">
                        {server.transportType}
                      </span>
                      {!server.enabled && (
                        <span className="rounded bg-yellow-900/50 px-2 py-0.5 text-xs text-yellow-400">disabled</span>
                      )}
                    </div>
                    <p className="mt-1 text-sm text-gray-400">{server.description || 'No description'}</p>
                    <p className="mt-1 text-xs text-gray-500">
                      {server.transportType === 'stdio'
                        ? `Command: ${server.command} ${(server.arguments || []).join(' ')}`
                        : `URL: ${server.url || 'N/A'}`}
                    </p>
                    {status?.toolCount ? (
                      <p className="mt-1 text-xs text-gray-500">{status.toolCount} tools available</p>
                    ) : null}
                  </div>
                  <div className="flex gap-2">
                    {status?.state === 'Connected' ? (
                      <>
                        <button onClick={() => handleDiscover(server.id)} className="rounded bg-gray-700 px-3 py-1 text-xs hover:bg-gray-600">
                          Discover Tools
                        </button>
                        <button onClick={() => handleDisconnect(server.id)} className="rounded bg-gray-700 px-3 py-1 text-xs hover:bg-gray-600">
                          Disconnect
                        </button>
                      </>
                    ) : (
                      <button onClick={() => handleConnect(server.id)} className="rounded bg-indigo-700 px-3 py-1 text-xs hover:bg-indigo-600">
                        Connect
                      </button>
                    )}
                    <button
                      onClick={() => { setEditingServer(server); setMode('edit') }}
                      className="rounded bg-gray-700 px-3 py-1 text-xs hover:bg-gray-600"
                    >
                      Edit
                    </button>
                    <button
                      onClick={() => handleDelete(server.id)}
                      className="rounded bg-red-900/50 px-3 py-1 text-xs text-red-300 hover:bg-red-800"
                    >
                      Delete
                    </button>
                  </div>
                </div>
                {status?.state === 'Error' && status.errorMessage && (
                  <div className="mt-2 rounded bg-red-900/30 px-3 py-1 text-xs text-red-400">
                    Error: {status.errorMessage}
                  </div>
                )}
                {tools && tools.length > 0 && (
                  <div className="mt-3 border-t border-gray-700 pt-3">
                    <h4 className="mb-2 text-sm font-medium text-gray-300">Available Tools ({tools.length})</h4>
                    <div className="grid gap-1">
                      {tools.map(tool => (
                        <div key={tool.toolName} className="flex items-baseline gap-2 text-xs">
                          <code className="font-mono text-indigo-400">{tool.toolName}</code>
                          <span className="text-gray-500">{tool.description || ''}</span>
                        </div>
                      ))}
                    </div>
                  </div>
                )}
              </div>
            )
          })}
        </div>
      )}
    </div>
  )
}

function StatusBadge({ state }: { state: string }) {
  const colors: Record<string, string> = {
    Connected: 'bg-green-900/50 text-green-400',
    Connecting: 'bg-yellow-900/50 text-yellow-400',
    Disconnected: 'bg-gray-700 text-gray-400',
    Error: 'bg-red-900/50 text-red-400',
  }
  return (
    <span className={`rounded px-2 py-0.5 text-xs ${colors[state] ?? colors.Disconnected}`}>
      {state}
    </span>
  )
}

function ServerForm({
  server,
  onSave,
  onCancel,
}: {
  server: McpToolServerDefinition | null
  onSave: (server: Partial<McpToolServerDefinition>) => Promise<void>
  onCancel: () => void
}) {
  const [form, setForm] = useState({
    id: server?.id ?? '',
    name: server?.name ?? '',
    description: server?.description ?? '',
    transportType: server?.transportType ?? 'stdio',
    command: server?.command ?? '',
    arguments: (server?.arguments ?? []).join(' '),
    workingDirectory: server?.workingDirectory ?? '',
    environmentVariables: Object.entries(server?.environmentVariables ?? {})
      .map(([k, v]) => `${k}=${v}`)
      .join('\n'),
    url: server?.url ?? '',
    headers: Object.entries(server?.headers ?? {})
      .map(([k, v]) => `${k}: ${v}`)
      .join('\n'),
    enabled: server?.enabled ?? true,
  })
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setSaving(true)
    setError(null)
    try {
      const envVars: Record<string, string> = {}
      form.environmentVariables.split('\n').filter(Boolean).forEach(line => {
        const idx = line.indexOf('=')
        if (idx > 0) envVars[line.slice(0, idx).trim()] = line.slice(idx + 1).trim()
      })

      const headers: Record<string, string> = {}
      form.headers.split('\n').filter(Boolean).forEach(line => {
        const idx = line.indexOf(':')
        if (idx > 0) headers[line.slice(0, idx).trim()] = line.slice(idx + 1).trim()
      })

      await onSave({
        id: form.id || form.name.toLowerCase().replace(/[^a-z0-9]+/g, '-'),
        name: form.name,
        description: form.description,
        transportType: form.transportType,
        command: form.transportType === 'stdio' ? form.command : undefined,
        arguments: form.transportType === 'stdio' ? form.arguments.split(/\s+/).filter(Boolean) : [],
        workingDirectory: form.workingDirectory || undefined,
        environmentVariables: envVars,
        url: form.transportType !== 'stdio' ? form.url : undefined,
        headers,
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
      <h1 className="mb-6 text-2xl font-bold">{server ? 'Edit' : 'Add'} MCP Server</h1>

      {error && (
        <div className="mb-4 rounded bg-red-900/50 px-4 py-2 text-red-300">{error}</div>
      )}

      <form onSubmit={handleSubmit} className="space-y-4 rounded border border-gray-700 bg-gray-800 p-6">
        <div className="grid grid-cols-2 gap-4">
          <label className="block">
            <span className="text-sm text-gray-400">Server ID</span>
            <input
              value={form.id}
              onChange={e => setForm(f => ({ ...f, id: e.target.value }))}
              placeholder="Auto-generated from name"
              disabled={!!server}
              className="mt-1 block w-full rounded border border-gray-600 bg-gray-700 px-3 py-2 text-sm disabled:opacity-50"
            />
          </label>
          <label className="block">
            <span className="text-sm text-gray-400">Name *</span>
            <input
              value={form.name}
              onChange={e => setForm(f => ({ ...f, name: e.target.value }))}
              required
              className="mt-1 block w-full rounded border border-gray-600 bg-gray-700 px-3 py-2 text-sm"
            />
          </label>
        </div>

        <label className="block">
          <span className="text-sm text-gray-400">Description</span>
          <input
            value={form.description}
            onChange={e => setForm(f => ({ ...f, description: e.target.value }))}
            className="mt-1 block w-full rounded border border-gray-600 bg-gray-700 px-3 py-2 text-sm"
          />
        </label>

        <div className="grid grid-cols-2 gap-4">
          <label className="block">
            <span className="text-sm text-gray-400">Transport Type</span>
            <select
              value={form.transportType}
              onChange={e => setForm(f => ({ ...f, transportType: e.target.value }))}
              className="mt-1 block w-full rounded border border-gray-600 bg-gray-700 px-3 py-2 text-sm"
            >
              <option value="stdio">stdio (local process)</option>
              <option value="http">HTTP/SSE (remote)</option>
            </select>
          </label>
          <label className="flex items-end gap-2 pb-2">
            <input
              type="checkbox"
              checked={form.enabled}
              onChange={e => setForm(f => ({ ...f, enabled: e.target.checked }))}
              className="h-4 w-4"
            />
            <span className="text-sm text-gray-400">Enabled</span>
          </label>
        </div>

        {form.transportType === 'stdio' ? (
          <>
            <label className="block">
              <span className="text-sm text-gray-400">Command *</span>
              <input
                value={form.command}
                onChange={e => setForm(f => ({ ...f, command: e.target.value }))}
                placeholder="e.g. npx, dnx, python"
                required
                className="mt-1 block w-full rounded border border-gray-600 bg-gray-700 px-3 py-2 text-sm font-mono"
              />
            </label>
            <label className="block">
              <span className="text-sm text-gray-400">Arguments (space-separated)</span>
              <input
                value={form.arguments}
                onChange={e => setForm(f => ({ ...f, arguments: e.target.value }))}
                placeholder="e.g. -y @modelcontextprotocol/server-github"
                className="mt-1 block w-full rounded border border-gray-600 bg-gray-700 px-3 py-2 text-sm font-mono"
              />
            </label>
            <label className="block">
              <span className="text-sm text-gray-400">Working Directory</span>
              <input
                value={form.workingDirectory}
                onChange={e => setForm(f => ({ ...f, workingDirectory: e.target.value }))}
                className="mt-1 block w-full rounded border border-gray-600 bg-gray-700 px-3 py-2 text-sm font-mono"
              />
            </label>
          </>
        ) : (
          <label className="block">
            <span className="text-sm text-gray-400">URL *</span>
            <input
              value={form.url}
              onChange={e => setForm(f => ({ ...f, url: e.target.value }))}
              placeholder="https://mcp-server.example.com"
              required
              className="mt-1 block w-full rounded border border-gray-600 bg-gray-700 px-3 py-2 text-sm font-mono"
            />
          </label>
        )}

        <label className="block">
          <span className="text-sm text-gray-400">Environment Variables (KEY=VALUE per line)</span>
          <textarea
            value={form.environmentVariables}
            onChange={e => setForm(f => ({ ...f, environmentVariables: e.target.value }))}
            rows={3}
            placeholder={"GITHUB_TOKEN=ghp_...\nAPI_KEY=sk-..."}
            className="mt-1 block w-full rounded border border-gray-600 bg-gray-700 px-3 py-2 text-sm font-mono"
          />
        </label>

        {form.transportType !== 'stdio' && (
          <label className="block">
            <span className="text-sm text-gray-400">Headers (Key: Value per line)</span>
            <textarea
              value={form.headers}
              onChange={e => setForm(f => ({ ...f, headers: e.target.value }))}
              rows={2}
              placeholder="Authorization: Bearer ..."
              className="mt-1 block w-full rounded border border-gray-600 bg-gray-700 px-3 py-2 text-sm font-mono"
            />
          </label>
        )}

        <div className="flex gap-3 pt-2">
          <button
            type="submit"
            disabled={saving}
            className="rounded bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-500 disabled:opacity-50"
          >
            {saving ? 'Saving...' : server ? 'Update Server' : 'Create Server'}
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
