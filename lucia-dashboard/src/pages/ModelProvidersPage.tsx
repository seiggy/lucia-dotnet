import { useState, useEffect, useCallback } from 'react'
import type { ModelProvider, ProviderType, ModelAuthConfig, CopilotModelInfo } from '../types'
import {
  fetchModelProviders,
  createModelProvider,
  updateModelProvider,
  deleteModelProvider,
  testModelProvider,
  connectCopilotCli,
} from '../api'

type FormMode = 'list' | 'create' | 'edit'

const PROVIDER_TYPES: { value: ProviderType; label: string; hint: string }[] = [
  { value: 'OpenAI', label: 'OpenAI', hint: 'Also works for OpenRouter, GitHub Models, and any OpenAI-compatible endpoint' },
  { value: 'AzureOpenAI', label: 'Azure OpenAI', hint: 'Azure-hosted OpenAI deployments' },
  { value: 'AzureAIInference', label: 'Azure AI Inference', hint: 'Azure AI model inference endpoint' },
  { value: 'Ollama', label: 'Ollama', hint: 'Local Ollama instance (default: http://localhost:11434)' },
  { value: 'Anthropic', label: 'Anthropic', hint: 'Claude models via Anthropic API' },
  { value: 'GoogleGemini', label: 'Google Gemini', hint: 'Gemini models via OpenAI-compatible endpoint' },
  { value: 'GitHubCopilot', label: 'GitHub Copilot SDK', hint: 'Requires copilot CLI installed and authenticated. Endpoint field overrides CLI path.' },
]

const AUTH_TYPES = [
  { value: 'api-key', label: 'API Key' },
  { value: 'azure-credential', label: 'Azure Default Credential' },
  { value: 'none', label: 'None (local)' },
]

function emptyProvider(): Partial<ModelProvider> {
  return {
    id: '',
    name: '',
    providerType: 'OpenAI',
    endpoint: '',
    modelName: '',
    auth: { authType: 'api-key', apiKey: '', useDefaultCredentials: false },
    enabled: true,
  }
}

export default function ModelProvidersPage() {
  const [providers, setProviders] = useState<ModelProvider[]>([])
  const [mode, setMode] = useState<FormMode>('list')
  const [form, setForm] = useState<Partial<ModelProvider>>(emptyProvider())
  const [editingId, setEditingId] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [testResults, setTestResults] = useState<Record<string, { success: boolean; message: string }>>({})
  const [testing, setTesting] = useState<string | null>(null)

  // Copilot-specific state
  const [copilotModels, setCopilotModels] = useState<CopilotModelInfo[]>([])
  const [copilotConnecting, setCopilotConnecting] = useState(false)
  const [copilotConnected, setCopilotConnected] = useState(false)
  const [copilotMessage, setCopilotMessage] = useState<string | null>(null)
  const [selectedCopilotModel, setSelectedCopilotModel] = useState<CopilotModelInfo | null>(null)

  const loadData = useCallback(async () => {
    try {
      setLoading(true)
      const data = await fetchModelProviders()
      setProviders(data)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load providers')
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => { loadData() }, [loadData])

  const handleCreate = () => {
    setForm(emptyProvider())
    setEditingId(null)
    setMode('create')
    setError(null)
    resetCopilotState()
  }

  const resetCopilotState = () => {
    setCopilotModels([])
    setCopilotConnecting(false)
    setCopilotConnected(false)
    setCopilotMessage(null)
    setSelectedCopilotModel(null)
  }

  const handleEdit = (p: ModelProvider) => {
    setForm({ ...p, auth: { ...p.auth } })
    setEditingId(p.id)
    setMode('edit')
    setError(null)
  }

  const handleDelete = async (id: string) => {
    if (!confirm(`Delete provider "${id}"?`)) return
    try {
      await deleteModelProvider(id)
      await loadData()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to delete')
    }
  }

  const handleTest = async (id: string) => {
    try {
      setTesting(id)
      const result = await testModelProvider(id)
      setTestResults(prev => ({ ...prev, [id]: result }))
    } catch (err) {
      setTestResults(prev => ({
        ...prev,
        [id]: { success: false, message: err instanceof Error ? err.message : 'Test failed' },
      }))
    } finally {
      setTesting(null)
    }
  }

  const handleSave = async () => {
    try {
      setError(null)

      // For Copilot providers, store the selected model metadata
      if (form.providerType === 'GitHubCopilot' && selectedCopilotModel) {
        form.modelName = selectedCopilotModel.id
        form.copilotMetadata = {
          supportsVision: selectedCopilotModel.supportsVision,
          supportsReasoningEffort: selectedCopilotModel.supportsReasoningEffort,
          maxPromptTokens: selectedCopilotModel.maxPromptTokens,
          maxOutputTokens: selectedCopilotModel.maxOutputTokens,
          maxContextWindowTokens: selectedCopilotModel.maxContextWindowTokens,
          policyState: selectedCopilotModel.policyState,
          policyTerms: selectedCopilotModel.policyTerms,
          billingMultiplier: selectedCopilotModel.billingMultiplier,
          supportedReasoningEfforts: selectedCopilotModel.supportedReasoningEfforts,
          defaultReasoningEffort: selectedCopilotModel.defaultReasoningEffort,
        }
      }

      if (mode === 'create') {
        await createModelProvider(form)
      } else if (editingId) {
        await updateModelProvider(editingId, form)
      }
      setMode('list')
      await loadData()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to save')
    }
  }

  const handleCopilotConnect = async () => {
    try {
      setCopilotConnecting(true)
      setCopilotMessage(null)
      setError(null)
      const result = await connectCopilotCli(form.auth?.apiKey || undefined)
      setCopilotMessage(result.message)
      if (result.success) {
        setCopilotModels(result.models)
        setCopilotConnected(true)
      } else {
        setCopilotConnected(false)
      }
    } catch (err) {
      setCopilotMessage(err instanceof Error ? err.message : 'Connection failed')
      setCopilotConnected(false)
    } finally {
      setCopilotConnecting(false)
    }
  }

  const updateAuth = (patch: Partial<ModelAuthConfig>) => {
    setForm(prev => ({
      ...prev,
      auth: { ...prev.auth!, ...patch },
    }))
  }

  if (loading && providers.length === 0) {
    return <div className="text-gray-400">Loading model providers...</div>
  }

  if (mode !== 'list') {
    return (
      <div className="space-y-6">
        <div className="flex items-center gap-4">
          <button
            onClick={() => setMode('list')}
            className="text-sm text-gray-400 hover:text-white"
          >
            ← Back
          </button>
          <h1 className="text-xl font-semibold text-white">
            {mode === 'create' ? 'Add Model Provider' : `Edit: ${editingId}`}
          </h1>
        </div>

        {error && (
          <div className="rounded bg-red-900/50 px-4 py-2 text-sm text-red-300">{error}</div>
        )}

        <div className="space-y-4 rounded-lg bg-gray-800 p-6">
          {/* ID (only on create) */}
          {mode === 'create' && (
            <div>
              <label className="mb-1 block text-sm text-gray-300">Provider ID</label>
              <input
                type="text"
                value={form.id ?? ''}
                onChange={e => setForm(prev => ({ ...prev, id: e.target.value }))}
                placeholder={form.providerType === 'GitHubCopilot' ? 'e.g. copilot-gpt4o' : 'e.g. gpt4o-prod, ollama-local'}
                className="w-full rounded bg-gray-700 px-3 py-2 text-sm text-white placeholder-gray-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
              />
              <p className="mt-1 text-xs text-gray-500">Unique key used to reference this provider</p>
            </div>
          )}

          {/* Name */}
          <div>
            <label className="mb-1 block text-sm text-gray-300">Display Name</label>
            <input
              type="text"
              value={form.name ?? ''}
              onChange={e => setForm(prev => ({ ...prev, name: e.target.value }))}
              placeholder={form.providerType === 'GitHubCopilot' ? 'e.g. Copilot GPT-4o' : 'e.g. GPT-4o Production'}
              className="w-full rounded bg-gray-700 px-3 py-2 text-sm text-white placeholder-gray-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
            />
          </div>

          {/* Provider Type */}
          <div>
            <label className="mb-1 block text-sm text-gray-300">Provider Type</label>
            <select
              value={form.providerType ?? 'OpenAI'}
              onChange={e => {
                const newType = e.target.value as ProviderType
                setForm(prev => ({
                  ...prev,
                  providerType: newType,
                  // Reset Copilot-specific fields when switching away
                  ...(newType === 'GitHubCopilot'
                    ? { auth: { authType: 'api-key', apiKey: '', useDefaultCredentials: false }, endpoint: '', modelName: '' }
                    : {}),
                }))
                if (newType !== 'GitHubCopilot') resetCopilotState()
              }}
              className="w-full rounded bg-gray-700 px-3 py-2 text-sm text-white focus:outline-none focus:ring-1 focus:ring-blue-500"
            >
              {PROVIDER_TYPES.map(pt => (
                <option key={pt.value} value={pt.value}>
                  {pt.label}
                </option>
              ))}
            </select>
            <p className="mt-1 text-xs text-gray-500">
              {PROVIDER_TYPES.find(pt => pt.value === form.providerType)?.hint}
            </p>
          </div>

          {/* ──── GitHub Copilot-specific form ──── */}
          {form.providerType === 'GitHubCopilot' ? (
            <div className="space-y-4">
              {/* GitHub Token (optional) */}
              <div>
                <label className="mb-1 block text-sm text-gray-300">
                  GitHub Token <span className="text-gray-500">(optional)</span>
                </label>
                <input
                  type="password"
                  value={form.auth?.apiKey ?? ''}
                  onChange={e => updateAuth({ apiKey: e.target.value, authType: 'api-key' })}
                  placeholder="ghp_... (leave blank to use logged-in user)"
                  className="w-full rounded bg-gray-700 px-3 py-2 text-sm text-white placeholder-gray-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
                />
                <p className="mt-1 text-xs text-gray-500">
                  If empty, the Copilot CLI will use your currently authenticated GitHub session.
                </p>
              </div>

              {/* Connect button */}
              <div>
                <button
                  onClick={handleCopilotConnect}
                  disabled={copilotConnecting}
                  className="rounded bg-purple-600 px-4 py-2 text-sm font-medium text-white hover:bg-purple-500 disabled:opacity-50"
                >
                  {copilotConnecting ? 'Connecting...' : copilotConnected ? '↻ Refresh Models' : '⚡ Connect to Copilot CLI'}
                </button>
              </div>

              {/* Connection status message */}
              {copilotMessage && (
                <div className={`rounded px-4 py-2 text-sm ${copilotConnected ? 'bg-green-900/30 text-green-300' : 'bg-red-900/30 text-red-300'}`}>
                  {copilotMessage}
                  {!copilotConnected && copilotMessage.includes('not found') && (
                    <a
                      href="https://docs.github.com/en/copilot/managing-copilot/configure-personal-settings/installing-the-github-copilot-extension-for-your-cli"
                      target="_blank"
                      rel="noopener noreferrer"
                      className="ml-2 underline text-blue-400 hover:text-blue-300"
                    >
                      Installation guide →
                    </a>
                  )}
                </div>
              )}

              {/* Model selection */}
              {copilotConnected && copilotModels.length > 0 && (
                <div className="space-y-3">
                  <h3 className="text-sm font-medium text-gray-300">Select a Model</h3>
                  <div className="max-h-96 space-y-2 overflow-y-auto">
                    {copilotModels.map(model => (
                      <button
                        key={model.id}
                        onClick={() => {
                          setSelectedCopilotModel(model)
                          setForm(prev => ({ ...prev, modelName: model.id }))
                        }}
                        className={`w-full rounded p-3 text-left transition-colors ${
                          selectedCopilotModel?.id === model.id
                            ? 'bg-purple-900/50 ring-1 ring-purple-500'
                            : 'bg-gray-700 hover:bg-gray-650'
                        }`}
                      >
                        <div className="flex items-center justify-between">
                          <div className="font-medium text-white">{model.name}</div>
                          <div className="flex items-center gap-2">
                            {model.supportsVision && (
                              <span className="rounded bg-blue-900/50 px-1.5 py-0.5 text-[10px] text-blue-300">Vision</span>
                            )}
                            {model.supportsReasoningEffort && (
                              <span className="rounded bg-amber-900/50 px-1.5 py-0.5 text-[10px] text-amber-300">Reasoning</span>
                            )}
                            {model.policyState && (
                              <span className={`rounded px-1.5 py-0.5 text-[10px] ${
                                model.policyState === 'enabled' ? 'bg-green-900/50 text-green-300' : 'bg-gray-600 text-gray-400'
                              }`}>
                                {model.policyState}
                              </span>
                            )}
                          </div>
                        </div>
                        <div className="mt-1 text-xs text-gray-400 font-mono">{model.id}</div>
                        <div className="mt-1 flex flex-wrap gap-x-4 gap-y-1 text-xs text-gray-500">
                          <span>Context: {(model.maxContextWindowTokens / 1000).toFixed(0)}k tokens</span>
                          {model.maxPromptTokens && <span>Max prompt: {(model.maxPromptTokens / 1000).toFixed(0)}k</span>}
                          <span>Billing: {model.billingMultiplier}x</span>
                          {model.supportedReasoningEfforts.length > 0 && (
                            <span>Reasoning: {model.supportedReasoningEfforts.join(', ')}</span>
                          )}
                        </div>
                        {model.policyTerms && (
                          <div className="mt-1 text-[10px] text-gray-600">
                            <a href={model.policyTerms} target="_blank" rel="noopener noreferrer" className="hover:text-gray-400">
                              Terms & Conditions
                            </a>
                          </div>
                        )}
                      </button>
                    ))}
                  </div>
                </div>
              )}

              {/* Selected model summary */}
              {selectedCopilotModel && (
                <div className="rounded bg-purple-900/20 px-4 py-3 text-sm">
                  <div className="text-purple-300 font-medium">Selected: {selectedCopilotModel.name}</div>
                  <div className="text-xs text-gray-400 font-mono mt-1">{selectedCopilotModel.id}</div>
                </div>
              )}
            </div>
          ) : (
            <>
              {/* ──── Standard provider fields ──── */}
              {/* Endpoint */}
              <div>
                <label className="mb-1 block text-sm text-gray-300">
                  Endpoint URL
                  {form.providerType === 'Ollama' && <span className="text-gray-500"> (default: http://localhost:11434)</span>}
                </label>
                <input
                  type="text"
                  value={form.endpoint ?? ''}
                  onChange={e => setForm(prev => ({ ...prev, endpoint: e.target.value }))}
                  placeholder={
                    form.providerType === 'Ollama'
                      ? 'http://localhost:11434'
                      : form.providerType === 'OpenAI'
                        ? 'Leave blank for api.openai.com, or set custom (e.g. https://openrouter.ai/api/v1)'
                        : 'https://...'
                  }
                  className="w-full rounded bg-gray-700 px-3 py-2 text-sm text-white placeholder-gray-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
                />
              </div>

              {/* Model Name */}
              <div>
                <label className="mb-1 block text-sm text-gray-300">Model / Deployment Name</label>
                <input
                  type="text"
                  value={form.modelName ?? ''}
                  onChange={e => setForm(prev => ({ ...prev, modelName: e.target.value }))}
                  placeholder="e.g. gpt-4o, claude-sonnet-4-20250514, llama3.2:3b"
                  className="w-full rounded bg-gray-700 px-3 py-2 text-sm text-white placeholder-gray-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
                />
              </div>

              {/* Auth Config */}
              <div className="space-y-3 rounded bg-gray-750 p-4">
                <h3 className="text-sm font-medium text-gray-300">Authentication</h3>

                <div>
                  <label className="mb-1 block text-xs text-gray-400">Auth Type</label>
                  <select
                    value={form.auth?.authType ?? 'api-key'}
                    onChange={e => {
                      const authType = e.target.value
                      updateAuth({
                        authType,
                        useDefaultCredentials: authType === 'azure-credential',
                        apiKey: authType === 'none' ? '' : form.auth?.apiKey,
                      })
                    }}
                    className="w-full rounded bg-gray-700 px-3 py-2 text-sm text-white focus:outline-none focus:ring-1 focus:ring-blue-500"
                  >
                    {AUTH_TYPES.map(at => (
                      <option key={at.value} value={at.value}>
                        {at.label}
                      </option>
                    ))}
                  </select>
                </div>

                {form.auth?.authType === 'api-key' && (
                  <div>
                    <label className="mb-1 block text-xs text-gray-400">API Key</label>
                    <input
                      type="password"
                      value={form.auth?.apiKey ?? ''}
                      onChange={e => updateAuth({ apiKey: e.target.value })}
                      placeholder="sk-... or your API key"
                      className="w-full rounded bg-gray-700 px-3 py-2 text-sm text-white placeholder-gray-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
                    />
                  </div>
                )}

                {form.auth?.authType === 'azure-credential' && (
                  <p className="text-xs text-gray-500">
                    Uses DefaultAzureCredential — works with Managed Identity, Azure CLI, environment variables, etc.
                  </p>
                )}
              </div>
            </>
          )}

          {/* Enabled */}
          <label className="flex items-center gap-2 text-sm text-gray-300">
            <input
              type="checkbox"
              checked={form.enabled ?? true}
              onChange={e => setForm(prev => ({ ...prev, enabled: e.target.checked }))}
              className="rounded bg-gray-700"
            />
            Enabled
          </label>

          {/* Save */}
          <div className="flex gap-3 pt-2">
            <button
              onClick={handleSave}
              disabled={form.providerType === 'GitHubCopilot' && !selectedCopilotModel && mode === 'create'}
              className="rounded bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-500 disabled:opacity-50 disabled:cursor-not-allowed"
            >
              {mode === 'create' ? 'Create Provider' : 'Save Changes'}
            </button>
            <button
              onClick={() => setMode('list')}
              className="rounded bg-gray-700 px-4 py-2 text-sm text-gray-300 hover:bg-gray-600"
            >
              Cancel
            </button>
          </div>
        </div>
      </div>
    )
  }

  // List mode
  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="text-xl font-semibold text-white">Model Providers</h1>
        <button
          onClick={handleCreate}
          className="rounded bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-500"
        >
          + Add Provider
        </button>
      </div>

      {error && (
        <div className="rounded bg-red-900/50 px-4 py-2 text-sm text-red-300">{error}</div>
      )}

      {providers.length === 0 ? (
        <div className="rounded-lg bg-gray-800 p-8 text-center text-gray-400">
          <p>No model providers configured yet.</p>
          <p className="mt-1 text-sm">Add a provider to connect your agents to LLMs.</p>
        </div>
      ) : (
        <div className="space-y-3">
          {providers.map(p => (
            <div
              key={p.id}
              className="rounded-lg bg-gray-800 p-4"
            >
              <div className="flex items-start justify-between">
                <div className="space-y-1">
                  <div className="flex items-center gap-2">
                    <span className="font-medium text-white">{p.name}</span>
                    <span className="rounded bg-gray-700 px-2 py-0.5 text-xs text-gray-300">
                      {p.providerType}
                    </span>
                    <span
                      className={`rounded px-2 py-0.5 text-xs ${
                        p.enabled
                          ? 'bg-green-900/50 text-green-300'
                          : 'bg-gray-700 text-gray-500'
                      }`}
                    >
                      {p.enabled ? 'enabled' : 'disabled'}
                    </span>
                  </div>
                  <div className="text-sm text-gray-400">
                    <span className="font-mono text-xs">{p.id}</span>
                    {' · '}
                    Model: <span className="text-gray-300">{p.modelName}</span>
                    {p.endpoint && (
                      <>
                        {' · '}
                        <span className="text-gray-500">{p.endpoint}</span>
                      </>
                    )}
                  </div>
                  <div className="text-xs text-gray-500">
                    Auth: {p.auth.authType}
                    {p.auth.useDefaultCredentials && ' (Azure credential)'}
                  </div>
                </div>

                <div className="flex items-center gap-2">
                  <button
                    onClick={() => handleTest(p.id)}
                    disabled={testing === p.id}
                    className="rounded bg-gray-700 px-3 py-1 text-xs text-gray-300 hover:bg-gray-600 disabled:opacity-50"
                  >
                    {testing === p.id ? 'Testing...' : 'Test'}
                  </button>
                  <button
                    onClick={() => handleEdit(p)}
                    className="rounded bg-gray-700 px-3 py-1 text-xs text-gray-300 hover:bg-gray-600"
                  >
                    Edit
                  </button>
                  <button
                    onClick={() => handleDelete(p.id)}
                    className="rounded bg-red-900/50 px-3 py-1 text-xs text-red-300 hover:bg-red-900"
                  >
                    Delete
                  </button>
                </div>
              </div>

              {/* Test result */}
              {testResults[p.id] && (
                <div
                  className={`mt-2 rounded px-3 py-2 text-xs ${
                    testResults[p.id].success
                      ? 'bg-green-900/30 text-green-300'
                      : 'bg-red-900/30 text-red-300'
                  }`}
                >
                  {testResults[p.id].message}
                </div>
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  )
}
