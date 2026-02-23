import { useState, useEffect, useCallback } from 'react'
import type { ModelProvider, ProviderType, ModelPurpose, ModelAuthConfig, CopilotModelInfo } from '../types'
import { EmbeddingCapableProviders } from '../types'
import {
  fetchModelProviders,
  createModelProvider,
  updateModelProvider,
  deleteModelProvider,
  testModelProvider,
  testEmbeddingProvider,
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
  // TODO: Re-enable once Copilot SDK integration is stable
  // { value: 'GitHubCopilot', label: 'GitHub Copilot SDK', hint: 'Uses bundled Copilot CLI. Requires GitHub Copilot subscription.' },
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
    purpose: 'Chat',
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
      const provider = providers.find(p => p.id === id)
      const result = provider?.purpose === 'Embedding'
        ? await testEmbeddingProvider(id)
        : await testModelProvider(id)
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
    return <div className="text-dust">Loading model providers...</div>
  }

  if (mode !== 'list') {
    return (
      <div className="space-y-6">
        <div className="flex flex-wrap items-center gap-3">
          <button
            onClick={() => setMode('list')}
            className="text-sm text-dust hover:text-light"
          >
            ← Back
          </button>
          <h1 className="font-display text-xl font-semibold text-light">
            {mode === 'create' ? 'Add Model Provider' : `Edit: ${editingId}`}
          </h1>
        </div>

        {error && (
          <div className="rounded bg-ember/15 px-4 py-2 text-sm text-rose">{error}</div>
        )}

        <div className="space-y-4 rounded-xl bg-charcoal p-6">
          {/* ID (only on create) */}
          {mode === 'create' && (
            <div>
              <label className="mb-1 block text-sm text-fog">Provider ID</label>
              <input
                type="text"
                value={form.id ?? ''}
                onChange={e => setForm(prev => ({ ...prev, id: e.target.value }))}
                placeholder={form.providerType === 'GitHubCopilot' ? 'e.g. copilot-gpt4o' : 'e.g. gpt4o-prod, ollama-local'}
                className="w-full rounded bg-basalt px-3 py-2 text-sm text-light placeholder-dust/60 input-focus focus:ring-1 focus:ring-blue-500"
              />
              <p className="mt-1 text-xs text-dust">Unique key used to reference this provider</p>
            </div>
          )}

          {/* Name */}
          <div>
            <label className="mb-1 block text-sm text-fog">Display Name</label>
            <input
              type="text"
              value={form.name ?? ''}
              onChange={e => setForm(prev => ({ ...prev, name: e.target.value }))}
              placeholder={form.providerType === 'GitHubCopilot' ? 'e.g. Copilot GPT-4o' : 'e.g. GPT-4o Production'}
              className="w-full rounded bg-basalt px-3 py-2 text-sm text-light placeholder-dust/60 input-focus focus:ring-1 focus:ring-blue-500"
            />
          </div>

          {/* Purpose */}
          <div>
            <label className="mb-1 block text-sm text-fog">Purpose</label>
            <select
              value={form.purpose ?? 'Chat'}
              onChange={e => {
                const newPurpose = e.target.value as ModelPurpose
                setForm(prev => {
                  const updated = { ...prev, purpose: newPurpose }
                  // Reset provider type if current one doesn't support embedding
                  if (newPurpose === 'Embedding' && prev.providerType && !EmbeddingCapableProviders.includes(prev.providerType)) {
                    updated.providerType = 'OpenAI'
                  }
                  return updated
                })
              }}
              className="w-full rounded bg-basalt px-3 py-2 text-sm text-light input-focus focus:ring-1 focus:ring-blue-500"
            >
              <option value="Chat">Chat (LLM text generation)</option>
              <option value="Embedding">Embedding (vector search)</option>
            </select>
            {form.purpose === 'Embedding' && (
              <p className="mt-1 text-xs text-amber">
                Embedding providers are used by skills for vector search (e.g. device matching, prompt caching). Agents never use embedding models directly.
              </p>
            )}
          </div>

          {/* Provider Type */}
          <div>
            <label className="mb-1 block text-sm text-fog">Provider Type</label>
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
              className="w-full rounded bg-basalt px-3 py-2 text-sm text-light input-focus focus:ring-1 focus:ring-blue-500"
            >
              {PROVIDER_TYPES
                .filter(pt => form.purpose !== 'Embedding' || EmbeddingCapableProviders.includes(pt.value))
                .map(pt => (
                  <option key={pt.value} value={pt.value}>
                    {pt.label}
                  </option>
                ))}
            </select>
            <p className="mt-1 text-xs text-dust">
              {PROVIDER_TYPES.find(pt => pt.value === form.providerType)?.hint}
            </p>
          </div>

          {/* ──── GitHub Copilot-specific form ──── */}
          {form.providerType === 'GitHubCopilot' ? (
            <div className="space-y-4">
              {/* GitHub Token (optional) */}
              <div>
                <label className="mb-1 block text-sm text-fog">
                  GitHub Token <span className="text-dust">(optional)</span>
                </label>
                <input
                  type="password"
                  value={form.auth?.apiKey ?? ''}
                  onChange={e => updateAuth({ apiKey: e.target.value, authType: 'api-key' })}
                  placeholder="ghp_... (leave blank to use logged-in user)"
                  className="w-full rounded bg-basalt px-3 py-2 text-sm text-light placeholder-dust/60 input-focus focus:ring-1 focus:ring-blue-500"
                />
                <p className="mt-1 text-xs text-dust">
                  If empty, the Copilot CLI will use your currently authenticated GitHub session.
                </p>
              </div>

              {/* Connect button */}
              <div>
                <button
                  onClick={handleCopilotConnect}
                  disabled={copilotConnecting}
                  className="rounded bg-rose/20 px-4 py-2 text-sm font-medium text-light hover:bg-purple-500 disabled:opacity-50"
                >
                  {copilotConnecting ? 'Connecting...' : copilotConnected ? '↻ Refresh Models' : '⚡ Connect to Copilot CLI'}
                </button>
              </div>

              {/* Connection status message */}
              {copilotMessage && (
                <div className={`rounded px-4 py-2 text-sm ${copilotConnected ? 'bg-green-900/30 text-sage' : 'bg-red-900/30 text-rose'}`}>
                  {copilotMessage}
                  {!copilotConnected && copilotMessage.includes('not found') && (
                    <a
                      href="https://docs.github.com/en/copilot/how-tos/copilot-cli/set-up-copilot-cli/install-copilot-cli"
                      target="_blank"
                      rel="noopener noreferrer"
                      className="ml-2 underline text-amber hover:text-amber"
                    >
                      Installation guide →
                    </a>
                  )}
                </div>
              )}

              {/* Model selection */}
              {copilotConnected && copilotModels.length > 0 && (
                <div className="space-y-3">
                  <h3 className="text-sm font-medium text-fog">Select a Model</h3>
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
                            : 'bg-basalt hover:bg-basalt'
                        }`}
                      >
                        <div className="flex items-center justify-between">
                          <div className="font-medium text-light">{model.name}</div>
                          <div className="flex flex-wrap items-center gap-2">
                            {model.supportsVision && (
                              <span className="whitespace-nowrap rounded bg-blue-900/50 px-1.5 py-0.5 text-[10px] text-amber">Vision</span>
                            )}
                            {model.supportsReasoningEffort && (
                              <span className="whitespace-nowrap rounded bg-amber-900/50 px-1.5 py-0.5 text-[10px] text-amber-300">Reasoning</span>
                            )}
                            {model.policyState && (
                              <span className={`whitespace-nowrap rounded px-1.5 py-0.5 text-[10px] ${
                                model.policyState === 'enabled' ? 'bg-green-900/50 text-sage' : 'bg-stone text-dust'
                              }`}>
                                {model.policyState}
                              </span>
                            )}
                          </div>
                        </div>
                        <div className="mt-1 text-xs text-dust font-mono">{model.id}</div>
                        <div className="mt-1 flex flex-wrap gap-x-4 gap-y-1 text-xs text-dust">
                          <span>Context: {(model.maxContextWindowTokens / 1000).toFixed(0)}k tokens</span>
                          {model.maxPromptTokens && <span>Max prompt: {(model.maxPromptTokens / 1000).toFixed(0)}k</span>}
                          <span>Billing: {model.billingMultiplier}x</span>
                          {model.supportedReasoningEfforts.length > 0 && (
                            <span>Reasoning: {model.supportedReasoningEfforts.join(', ')}</span>
                          )}
                        </div>
                        {model.policyTerms && (
                          <div className="mt-1 text-[10px] text-dust">
                            <a href={model.policyTerms} target="_blank" rel="noopener noreferrer" className="hover:text-dust">
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
                  <div className="text-rose font-medium">Selected: {selectedCopilotModel.name}</div>
                  <div className="text-xs text-dust font-mono mt-1">{selectedCopilotModel.id}</div>
                </div>
              )}
            </div>
          ) : (
            <>
              {/* ──── Standard provider fields ──── */}
              {/* Endpoint */}
              <div>
                <label className="mb-1 block text-sm text-fog">
                  Endpoint URL
                  {form.providerType === 'Ollama' && <span className="text-dust"> (default: http://localhost:11434)</span>}
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
                  className="w-full rounded bg-basalt px-3 py-2 text-sm text-light placeholder-dust/60 input-focus focus:ring-1 focus:ring-blue-500"
                />
              </div>

              {/* Model Name */}
              <div>
                <label className="mb-1 block text-sm text-fog">Model / Deployment Name</label>
                <input
                  type="text"
                  value={form.modelName ?? ''}
                  onChange={e => setForm(prev => ({ ...prev, modelName: e.target.value }))}
                  placeholder="e.g. gpt-4o, claude-sonnet-4-20250514, llama3.2:3b"
                  className="w-full rounded bg-basalt px-3 py-2 text-sm text-light placeholder-dust/60 input-focus focus:ring-1 focus:ring-blue-500"
                />
              </div>

              {/* Auth Config */}
              <div className="space-y-3 rounded bg-basalt p-4">
                <h3 className="text-sm font-medium text-fog">Authentication</h3>

                <div>
                  <label className="mb-1 block text-xs text-dust">Auth Type</label>
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
                    className="w-full rounded bg-basalt px-3 py-2 text-sm text-light input-focus focus:ring-1 focus:ring-blue-500"
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
                    <label className="mb-1 block text-xs text-dust">API Key</label>
                    <input
                      type="password"
                      value={form.auth?.apiKey ?? ''}
                      onChange={e => updateAuth({ apiKey: e.target.value })}
                      placeholder="sk-... or your API key"
                      className="w-full rounded bg-basalt px-3 py-2 text-sm text-light placeholder-dust/60 input-focus focus:ring-1 focus:ring-blue-500"
                    />
                  </div>
                )}

                {form.auth?.authType === 'azure-credential' && (
                  <p className="text-xs text-dust">
                    Uses DefaultAzureCredential — works with Managed Identity, Azure CLI, environment variables, etc.
                  </p>
                )}
              </div>
            </>
          )}

          {/* Enabled */}
          <label className="flex items-center gap-2 text-sm text-fog">
            <input
              type="checkbox"
              checked={form.enabled ?? true}
              onChange={e => setForm(prev => ({ ...prev, enabled: e.target.checked }))}
              className="rounded bg-basalt"
            />
            Enabled
          </label>

          {/* Save */}
          <div className="flex gap-3 pt-2">
            <button
              onClick={handleSave}
              disabled={form.providerType === 'GitHubCopilot' && !selectedCopilotModel && mode === 'create'}
              className="rounded bg-amber px-4 py-2 text-sm font-medium text-void hover:bg-amber-glow disabled:opacity-50 disabled:cursor-not-allowed"
            >
              {mode === 'create' ? 'Create Provider' : 'Save Changes'}
            </button>
            <button
              onClick={() => setMode('list')}
              className="rounded bg-basalt px-4 py-2 text-sm text-fog hover:bg-stone"
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
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="font-display text-xl font-semibold text-light">Model Providers</h1>
        <button
          onClick={handleCreate}
          className="rounded bg-amber px-4 py-2 text-sm font-medium text-void hover:bg-amber-glow"
        >
          + Add Provider
        </button>
      </div>

      {error && (
        <div className="rounded bg-ember/15 px-4 py-2 text-sm text-rose">{error}</div>
      )}

      {providers.length === 0 ? (
        <div className="rounded-xl bg-charcoal p-8 text-center text-dust">
          <p>No model providers configured yet.</p>
          <p className="mt-1 text-sm">Add a provider to connect your agents to LLMs.</p>
        </div>
      ) : (
        <div className="space-y-3">
          {providers.map(p => (
            <div
              key={p.id}
              className="rounded-xl bg-charcoal p-4"
            >
              <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                <div className="min-w-0 space-y-1">
                  <div className="flex flex-wrap items-center gap-2">
                    <span className="font-medium text-light">{p.name}</span>
                    <span className="whitespace-nowrap rounded bg-basalt px-2 py-0.5 text-xs text-fog">
                      {p.providerType}
                    </span>
                    {p.purpose === 'Embedding' && (
                      <span className="whitespace-nowrap rounded bg-purple-900/50 px-2 py-0.5 text-xs text-rose">
                        embedding
                      </span>
                    )}
                    {p.isBuiltIn && (
                      <span className="whitespace-nowrap rounded bg-blue-900/50 px-2 py-0.5 text-xs text-amber">
                        default
                      </span>
                    )}
                    <span
                      className={`whitespace-nowrap rounded px-2 py-0.5 text-xs ${
                        p.enabled
                          ? 'bg-green-900/50 text-sage'
                          : 'bg-basalt text-dust'
                      }`}
                    >
                      {p.enabled ? 'enabled' : 'disabled'}
                    </span>
                  </div>
                  <div className="text-sm text-dust">
                    <span className="font-mono text-xs">{p.id}</span>
                    <span className="hidden sm:inline">
                      {' · '}
                      Model: <span className="text-fog">{p.modelName}</span>
                      {p.endpoint && (
                        <>
                          {' · '}
                          <span className="text-dust" title={p.endpoint}>
                            {p.endpoint.length > 40 ? p.endpoint.slice(0, 40) + '…' : p.endpoint}
                          </span>
                        </>
                      )}
                    </span>
                  </div>
                  <div className="text-xs text-dust sm:hidden">
                    Model: <span className="text-fog">{p.modelName}</span>
                  </div>
                  {p.endpoint && (
                    <div className="text-xs text-dust sm:hidden" title={p.endpoint}>
                      {p.endpoint.length > 35 ? p.endpoint.slice(0, 35) + '…' : p.endpoint}
                    </div>
                  )}
                  <div className="text-xs text-dust">
                    Auth: {p.auth.authType}
                    {p.auth.useDefaultCredentials && ' (Azure credential)'}
                  </div>
                </div>

                <div className="flex shrink-0 items-center gap-2">
                  <button
                    onClick={() => handleTest(p.id)}
                    disabled={testing === p.id}
                    className="rounded bg-basalt px-3 py-1 text-xs text-fog hover:bg-stone disabled:opacity-50"
                  >
                    {testing === p.id ? 'Testing...' : p.purpose === 'Embedding' ? 'Test Embedding' : 'Test'}
                  </button>
                  <button
                    onClick={() => handleEdit(p)}
                    className="rounded bg-basalt px-3 py-1 text-xs text-fog hover:bg-stone"
                  >
                    Edit
                  </button>
                  {!p.isBuiltIn && (
                    <button
                      onClick={() => handleDelete(p.id)}
                      className="rounded bg-ember/15 px-3 py-1 text-xs text-rose hover:bg-red-900"
                    >
                      Delete
                    </button>
                  )}
                </div>
              </div>

              {/* Test result */}
              {testResults[p.id] && (
                <div
                  className={`mt-2 rounded px-3 py-2 text-xs ${
                    testResults[p.id].success
                      ? 'bg-green-900/30 text-sage'
                      : 'bg-red-900/30 text-rose'
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
