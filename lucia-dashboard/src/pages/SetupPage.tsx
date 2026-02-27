import { useState, useEffect, useCallback } from 'react'
import { useAuth } from '../auth/AuthContext'
import {
  fetchSetupStatus,
  generateDashboardKey,
  configureHomeAssistant,
  testHaConnection,
  generateHaKey,
  fetchHaStatus,
  fetchAgentStatus,
  completeSetup,
  fetchModelProviders,
  createModelProvider,
  testModelProvider,
  testEmbeddingProvider,
  deleteModelProvider,
} from '../api'
import type { SetupStatus, GenerateKeyResponse, TestHaConnectionResponse, AgentStatusResponse } from '../api'
import type { ProviderType, ModelPurpose, ModelAuthConfig, ModelProvider } from '../types'
import { Sparkles, ArrowRight, Key, Plug, CheckCircle2, Copy, Check, Loader2, Radio, Brain, Cpu, Trash2, FlaskConical } from 'lucide-react'

type WizardStep = 'welcome' | 'lucia-ha' | 'ai-providers' | 'agent-status' | 'ha-plugin' | 'done'

export default function SetupPage() {
  const { refresh, login, authenticated: authFromContext } = useAuth()
  const [step, setStep] = useState<WizardStep>('welcome')
  const [status, setStatus] = useState<SetupStatus | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    fetchSetupStatus()
      .then((s) => {
        setStatus(s)
        if (s.setupComplete) setStep('done')
        else setStep('welcome')
      })
      .finally(() => setLoading(false))
  }, [])

  if (loading) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-observatory">
        <Loader2 className="h-5 w-5 animate-spin text-amber" />
        <span className="ml-2 font-display text-sm text-fog">Loading setup...</span>
      </div>
    )
  }

  return (
    <div className="flex min-h-screen items-center justify-center bg-observatory p-4">
      <div className="pointer-events-none absolute inset-0 overflow-hidden">
        <div className="absolute left-1/2 top-1/4 h-[600px] w-[600px] -translate-x-1/2 -translate-y-1/2 rounded-full bg-amber/[0.03] blur-[120px]" />
      </div>
      <div className="relative w-full max-w-2xl">
        <div className="glass-panel rounded-2xl p-6 sm:p-8 glow-amber-sm">
          <StepIndicator current={step} />
          {step === 'welcome' && <WelcomeStep onNext={() => setStep('lucia-ha')} />}
          {step === 'lucia-ha' && (
            <LuciaHaStep
              status={status}
              login={login}
              authFromContext={authFromContext}
              onComplete={(s) => {
                setStatus(s)
                setStep('ai-providers')
              }}
            />
          )}
          {step === 'ai-providers' && (
            <AiProviderStep
              onComplete={() => setStep('agent-status')}
            />
          )}
          {step === 'agent-status' && (
            <AgentStatusStep
              onComplete={() => setStep('ha-plugin')}
            />
          )}
          {step === 'ha-plugin' && (
            <HaPluginStep
              status={status}
              onComplete={async () => {
                await completeSetup()
                await refresh()
                setStep('done')
              }}
            />
          )}
          {step === 'done' && <DoneStep />}
        </div>
      </div>
    </div>
  )
}

/* ── Step indicator ─────────────────────────────────── */

function StepIndicator({ current }: { current: WizardStep }) {
  const steps: { key: WizardStep; label: string; icon: typeof Sparkles }[] = [
    { key: 'welcome', label: 'Welcome', icon: Sparkles },
    { key: 'lucia-ha', label: 'Configure', icon: Key },
    { key: 'ai-providers', label: 'AI Provider', icon: Brain },
    { key: 'agent-status', label: 'Agents', icon: Cpu },
    { key: 'ha-plugin', label: 'Connect', icon: Plug },
    { key: 'done', label: 'Done', icon: CheckCircle2 },
  ]
  const idx = steps.findIndex((s) => s.key === current)

  return (
    <div className="mb-8 flex w-full items-center justify-between">
      {steps.map((s, i) => {
        const Icon = s.icon
        const active = i <= idx
        return (
          <div key={s.key} className="flex items-center">
            <div className="flex flex-col items-center gap-1">
              <div
                className={`flex h-7 w-7 shrink-0 items-center justify-center rounded-full transition-colors sm:h-8 sm:w-8 ${
                  active ? 'bg-amber/20 text-amber' : 'bg-basalt text-dust'
                }`}
              >
                {i < idx ? <Check className="h-3.5 w-3.5" /> : <Icon className="h-3.5 w-3.5" />}
              </div>
              <span className={`text-[10px] font-medium sm:text-xs ${active ? 'text-light' : 'text-dust'}`}>
                {s.label}
              </span>
            </div>
            {i < steps.length - 1 && <div className={`mx-1 h-px flex-1 self-start mt-3.5 sm:mt-4 min-w-3 sm:mx-2 ${active ? 'bg-amber/30' : 'bg-stone'}`} />}
          </div>
        )
      })}
    </div>
  )
}

/* ── Shared button styles ───────────────────────────── */

const btnPrimary = 'rounded-xl bg-amber px-5 py-2.5 text-sm font-semibold text-void transition-all hover:bg-amber-glow disabled:cursor-not-allowed disabled:opacity-40'
const btnSecondary = 'rounded-xl border border-stone bg-basalt px-5 py-2.5 text-sm font-medium text-fog transition-colors hover:border-amber/30 hover:text-light disabled:opacity-40'
const btnSuccess = 'rounded-xl bg-sage/20 text-sage px-5 py-2.5 text-sm font-medium transition-colors hover:bg-sage/30 disabled:opacity-40'
const inputStyle = 'w-full rounded-xl border border-stone bg-basalt px-4 py-3 text-sm text-light placeholder-dust/60 input-focus transition-colors'

/* ── Step 1: Welcome ────────────────────────────────── */

function WelcomeStep({ onNext }: { onNext: () => void }) {
  return (
    <div className="space-y-6 text-center">
      <div className="mx-auto flex h-14 w-14 items-center justify-center rounded-2xl bg-amber/10 glow-amber">
        <Sparkles className="h-7 w-7 text-amber" />
      </div>
      <div>
        <h2 className="font-display text-2xl font-semibold tracking-tight text-light">Welcome to Lucia</h2>
        <p className="mt-2 text-sm text-fog">
          Your privacy-first home automation assistant. Let's get you set up with secure access
          and connected to Home Assistant.
        </p>
      </div>
      <div className="rounded-xl border border-stone bg-basalt/50 p-5 text-left text-sm">
        <p className="mb-3 font-display text-xs font-semibold uppercase tracking-wider text-amber">What we'll do</p>
        <ol className="list-inside list-decimal space-y-2 text-fog">
          <li>Generate an API key for the Lucia dashboard</li>
          <li>Connect Lucia to your Home Assistant instance</li>
          <li>Configure an AI model provider for your agents</li>
          <li>Set up the Home Assistant plugin to talk back to Lucia</li>
        </ol>
      </div>
      <button onClick={onNext} className={`group inline-flex items-center gap-2 ${btnPrimary}`}>
        Get Started
        <ArrowRight className="h-4 w-4 transition-transform group-hover:translate-x-0.5" />
      </button>
    </div>
  )
}

/* ── Step 2: Configure Lucia + HA ───────────────────── */

function LuciaHaStep({
  status,
  login,
  authFromContext,
  onComplete,
}: {
  status: SetupStatus | null
  login: (apiKey: string) => Promise<void>
  authFromContext: boolean
  onComplete: (s: SetupStatus) => void
}) {
  const [dashboardKey, setDashboardKey] = useState<GenerateKeyResponse | null>(null)
  const [keyCopied, setKeyCopied] = useState(false)
  const [resumeKey, setResumeKey] = useState('')
  const [resumeError, setResumeError] = useState('')
  const [resumeBusy, setResumeBusy] = useState(false)
  const [resumed, setResumed] = useState(false)
  const [haUrl, setHaUrl] = useState(status?.haUrl ?? '')
  const [haToken, setHaToken] = useState('')
  const [haSaved, setHaSaved] = useState(false)
  const [testResult, setTestResult] = useState<TestHaConnectionResponse | null>(null)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  const hasDashKey = status?.hasDashboardKey || dashboardKey !== null
  const isAuthenticated = dashboardKey !== null || resumed || authFromContext

  async function handleGenerateKey() {
    setError('')
    setBusy(true)
    try {
      const result = await generateDashboardKey()
      setDashboardKey(result)
      // Auto-login so the session cookie is set for subsequent authenticated setup calls
      await login(result.key)
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Failed to generate key')
    } finally {
      setBusy(false)
    }
  }

  async function handleSaveHa() {
    setError('')
    setBusy(true)
    try {
      await configureHomeAssistant(haUrl, haToken)
      setHaSaved(true)
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Failed to save HA config')
    } finally {
      setBusy(false)
    }
  }

  async function handleTestConnection() {
    setError('')
    setBusy(true)
    try {
      const result = await testHaConnection()
      setTestResult(result)
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Failed to test connection')
    } finally {
      setBusy(false)
    }
  }

  async function handleCopyKey() {
    if (dashboardKey) {
      await navigator.clipboard.writeText(dashboardKey.key)
      setKeyCopied(true)
    }
  }

  async function handleResumeLogin() {
    setResumeError('')
    setResumeBusy(true)
    try {
      await login(resumeKey)
      setResumed(true)
    } catch {
      setResumeError('Invalid API key. Please check and try again.')
    } finally {
      setResumeBusy(false)
    }
  }

  return (
    <div className="space-y-6">
      <h2 className="font-display text-xl font-semibold text-light">Configure Lucia & Home Assistant</h2>

      {/* 2a: Dashboard API Key */}
      <section className="rounded-xl border border-stone bg-basalt/50 p-5">
        <h3 className="mb-3 flex items-center gap-2 font-display text-sm font-semibold text-amber">
          <Key className="h-4 w-4" /> Dashboard API Key
        </h3>
        {!hasDashKey ? (
          <>
            <p className="mb-3 text-sm text-fog">
              Generate an API key to log into the Lucia dashboard. Save it — you won't see it again.
            </p>
            <button onClick={handleGenerateKey} disabled={busy} className={btnPrimary}>
              {busy ? 'Generating...' : 'Generate Dashboard Key'}
            </button>
          </>
        ) : dashboardKey ? (
          <div className="space-y-3">
            <p className="flex items-center gap-1.5 text-sm text-sage">
              <CheckCircle2 className="h-4 w-4" /> Key generated — save it now!
            </p>
            <div className="flex items-center gap-2">
              <code className="flex-1 rounded-lg bg-void px-3 py-2.5 font-mono text-sm text-amber select-all">
                {dashboardKey.key}
              </code>
              <button onClick={handleCopyKey} className={btnSecondary + ' !px-3 !py-2.5'}>
                {keyCopied ? <Check className="h-4 w-4 text-sage" /> : <Copy className="h-4 w-4" />}
              </button>
            </div>
            <p className="text-xs text-dust">
              This key will be used to log into the dashboard after setup completes.
            </p>
          </div>
        ) : (
          <div className="space-y-3">
            {resumed || authFromContext ? (
              <p className="flex items-center gap-1.5 text-sm text-sage">
                <CheckCircle2 className="h-4 w-4" /> Authenticated — continue setup below
              </p>
            ) : (
              <>
                <p className="flex items-center gap-1.5 text-sm text-amber">
                  <Key className="h-4 w-4" /> Dashboard key was already generated
                </p>
                <p className="text-sm text-fog">
                  Enter your dashboard API key to resume setup where you left off.
                </p>
                <div className="flex gap-2">
                  <input
                    type="password"
                    placeholder="lk_..."
                    value={resumeKey}
                    onChange={(e) => setResumeKey(e.target.value)}
                    className={inputStyle + ' flex-1'}
                  />
                  <button
                    onClick={handleResumeLogin}
                    disabled={resumeBusy || !resumeKey}
                    className={btnPrimary}
                  >
                    {resumeBusy ? 'Verifying...' : 'Login'}
                  </button>
                </div>
                {resumeError && (
                  <p className="text-xs text-rose">{resumeError}</p>
                )}
                <p className="text-xs text-dust">
                  Lost your key? Reset the MongoDB configuration store and re-run the setup wizard.
                </p>
              </>
            )}
          </div>
        )}
      </section>

      {/* 2b: Home Assistant Connection */}
      <section className="rounded-xl border border-stone bg-basalt/50 p-5">
        <h3 className="mb-3 flex items-center gap-2 font-display text-sm font-semibold text-amber">
          <Plug className="h-4 w-4" /> Home Assistant Connection
        </h3>
        <p className="mb-3 text-sm text-fog">
          Enter your Home Assistant URL and a long-lived access token.
        </p>
        <div className="mb-4 rounded-lg border border-stone bg-void/50 p-3 text-xs text-dust">
          <p className="mb-1.5 font-semibold text-fog">How to create a long-lived access token:</p>
          <ol className="list-inside list-decimal space-y-0.5">
            <li>Open your Home Assistant instance</li>
            <li>Click your profile picture (bottom-left)</li>
            <li>Scroll to <strong className="text-fog">Security</strong> tab</li>
            <li>Under "Long-lived access tokens", click <strong className="text-fog">Create Token</strong></li>
            <li>Name it "Lucia" and copy the token</li>
          </ol>
        </div>
        <div className="space-y-3">
          <input
            type="url"
            placeholder="http://homeassistant.local:8123"
            value={haUrl}
            onChange={(e) => setHaUrl(e.target.value)}
            className={inputStyle}
          />
          <input
            type="password"
            placeholder="Paste your long-lived access token"
            value={haToken}
            onChange={(e) => setHaToken(e.target.value)}
            className={inputStyle}
          />
          <div className="flex gap-2">
            <button
              onClick={handleSaveHa}
              disabled={busy || !haUrl || !haToken}
              className={btnPrimary}
            >
              {busy ? 'Saving...' : 'Save'}
            </button>
            {haSaved && (
              <button onClick={handleTestConnection} disabled={busy} className={btnSuccess}>
                {busy ? 'Testing...' : 'Test Connection'}
              </button>
            )}
          </div>
        </div>
        {testResult && (
          <div
            className={`mt-3 rounded-lg p-3 text-sm ${
              testResult.connected
                ? 'border border-sage/30 bg-sage/10 text-sage'
                : 'border border-ember/30 bg-ember/10 text-rose'
            }`}
          >
            {testResult.connected
              ? `✓ Connected to ${testResult.locationName ?? 'Home Assistant'} (v${testResult.haVersion})`
              : `✗ ${testResult.error}`}
          </div>
        )}
      </section>

      {error && (
        <div className="rounded-xl border border-ember/30 bg-ember/10 px-4 py-2.5 text-sm text-rose">
          {error}
        </div>
      )}

      <div className="flex justify-end">
        <button
          onClick={async () => {
            const s = await fetchSetupStatus()
            onComplete(s)
          }}
          disabled={!isAuthenticated || (!haSaved && !status?.hasHaConnection)}
          className={`group inline-flex items-center gap-2 ${btnPrimary}`}
        >
          Next
          <ArrowRight className="h-4 w-4 transition-transform group-hover:translate-x-0.5" />
        </button>
      </div>
    </div>
  )
}

/* ── Step 3: Configure AI Providers ──────────────────── */

const PROVIDER_TYPES: { value: ProviderType; label: string; placeholder: string }[] = [
  { value: 'OpenAI', label: 'OpenAI', placeholder: 'gpt-4o' },
  { value: 'Anthropic', label: 'Anthropic', placeholder: 'claude-sonnet-4-20250514' },
  { value: 'GoogleGemini', label: 'Google Gemini', placeholder: 'gemini-2.0-flash' },
  { value: 'Ollama', label: 'Ollama (local)', placeholder: 'llama3.2:3b' },
  { value: 'AzureOpenAI', label: 'Azure OpenAI', placeholder: 'my-gpt4o-deployment' },
  { value: 'AzureAIInference', label: 'Azure AI Inference', placeholder: 'my-model' },
]

function AiProviderStep({ onComplete }: { onComplete: () => void }) {
  const [providers, setProviders] = useState<ModelProvider[]>([])
  const [showForm, setShowForm] = useState(false)
  const [providerType, setProviderType] = useState<ProviderType>('OpenAI')
  const [purpose, setPurpose] = useState<ModelPurpose>('Chat')
  const [modelName, setModelName] = useState('')
  const [endpoint, setEndpoint] = useState('')
  const [apiKey, setApiKey] = useState('')
  const [useDefaultCreds, setUseDefaultCreds] = useState(false)
  const [testResults, setTestResults] = useState<Record<string, { success: boolean; message: string }>>({})
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  const refreshProviders = useCallback(async () => {
    try {
      const list = await fetchModelProviders()
      setProviders(list)
    } catch {
      // may fail before any providers exist
    }
  }, [])

  useEffect(() => { refreshProviders() }, [refreshProviders])

  const isAzure = providerType === 'AzureOpenAI' || providerType === 'AzureAIInference'
  const needsApiKey = providerType !== 'Ollama' && !useDefaultCreds
  const modelPlaceholder = PROVIDER_TYPES.find(p => p.value === providerType)?.placeholder ?? ''
  const hasChatProvider = providers.some(p => p.purpose === 'Chat')

  async function handleCreate() {
    setError('')
    setBusy(true)
    try {
      const id = `${providerType.toLowerCase()}-${modelName.replace(/[^a-z0-9]/gi, '-').toLowerCase()}`
      const auth: ModelAuthConfig = useDefaultCreds
        ? { authType: 'default-credential', useDefaultCredentials: true }
        : needsApiKey
          ? { authType: 'api-key', apiKey, useDefaultCredentials: false }
          : { authType: 'none', useDefaultCredentials: false }

      await createModelProvider({
        id,
        name: `${PROVIDER_TYPES.find(p => p.value === providerType)?.label} — ${modelName}`,
        providerType,
        purpose,
        endpoint: endpoint || undefined,
        modelName,
        auth,
        enabled: true,
      })

      // Reset form and refresh list
      setModelName('')
      setEndpoint('')
      setApiKey('')
      setUseDefaultCreds(false)
      setPurpose('Chat')
      setShowForm(false)
      await refreshProviders()
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Failed to create provider')
    } finally {
      setBusy(false)
    }
  }

  async function handleTest(id: string) {
    try {
      const provider = providers.find(p => p.id === id)
      const result = provider?.purpose === 'Embedding'
        ? await testEmbeddingProvider(id)
        : await testModelProvider(id)
      setTestResults(prev => ({ ...prev, [id]: result }))
    } catch {
      setTestResults(prev => ({ ...prev, [id]: { success: false, message: 'Test request failed' } }))
    }
  }

  async function handleDelete(id: string) {
    try {
      await deleteModelProvider(id)
      setTestResults(prev => { const copy = { ...prev }; delete copy[id]; return copy })
      await refreshProviders()
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Failed to delete provider')
    }
  }

  return (
    <div className="space-y-6">
      <h2 className="font-display text-xl font-semibold text-light">Configure AI Provider</h2>
      <p className="text-sm text-fog">
        Lucia needs at least one Chat model provider to power its agents.
        Add your preferred AI service below.
      </p>

      {/* Existing providers */}
      {providers.length > 0 && (
        <section className="space-y-2">
          {providers.map(p => {
            const result = testResults[p.id]
            return (
              <div key={p.id} className="flex items-center gap-3 rounded-xl border border-stone bg-basalt/50 px-4 py-3">
                <Brain className="h-4 w-4 shrink-0 text-amber" />
                <div className="min-w-0 flex-1">
                  <p className="truncate text-sm font-medium text-light">{p.name}</p>
                  <p className="text-xs text-dust">{p.providerType} · {p.purpose} · {p.modelName}</p>
                </div>
                {result && (
                  <span className={`text-xs ${result.success ? 'text-sage' : 'text-rose'}`}>
                    {result.success ? '✓ OK' : '✗ Failed'}
                  </span>
                )}
                <button onClick={() => handleTest(p.id)} className="rounded-lg bg-basalt px-2.5 py-1.5 text-xs text-fog hover:bg-stone" title="Test">
                  <FlaskConical className="h-3.5 w-3.5" />
                </button>
                {!p.isBuiltIn && (
                  <button onClick={() => handleDelete(p.id)} className="rounded-lg bg-basalt px-2.5 py-1.5 text-xs text-rose/70 hover:bg-ember/20" title="Delete">
                    <Trash2 className="h-3.5 w-3.5" />
                  </button>
                )}
              </div>
            )
          })}
        </section>
      )}

      {/* Add provider form */}
      {showForm ? (
        <section className="rounded-xl border border-stone bg-basalt/50 p-5 space-y-4">
          <h3 className="flex items-center gap-2 font-display text-sm font-semibold text-amber">
            <Brain className="h-4 w-4" /> New Provider
          </h3>

          {/* Purpose */}
          <div>
            <label className="mb-1 block text-sm text-fog">Purpose</label>
            <select value={purpose} onChange={e => setPurpose(e.target.value as ModelPurpose)} className={inputStyle}>
              <option value="Chat">Chat (required)</option>
              <option value="Embedding">Embedding (optional)</option>
            </select>
          </div>

          {/* Provider Type */}
          <div>
            <label className="mb-1 block text-sm text-fog">Provider Type</label>
            <select
              value={providerType}
              onChange={e => { setProviderType(e.target.value as ProviderType); setEndpoint(''); setUseDefaultCreds(false) }}
              className={inputStyle}
            >
              {PROVIDER_TYPES.map(pt => (
                <option key={pt.value} value={pt.value}>{pt.label}</option>
              ))}
            </select>
          </div>

          {/* Model Name */}
          <div>
            <label className="mb-1 block text-sm text-fog">{isAzure ? 'Deployment Name' : 'Model Name'}</label>
            <input
              type="text"
              value={modelName}
              onChange={e => setModelName(e.target.value)}
              placeholder={modelPlaceholder}
              className={inputStyle}
            />
          </div>

          {/* Endpoint */}
          <div>
            <label className="mb-1 block text-sm text-fog">
              Endpoint URL
              {isAzure
                ? <span className="text-dust"> (required — your Azure OpenAI resource URL)</span>
                : <span className="text-dust"> (optional{providerType === 'Ollama' ? ', default: http://localhost:11434' : ''})</span>
              }
            </label>
            <input
              type="text"
              value={endpoint}
              onChange={e => setEndpoint(e.target.value)}
              placeholder={isAzure ? 'https://my-resource.openai.azure.com' : providerType === 'Ollama' ? 'http://localhost:11434' : 'Leave blank for default'}
              className={inputStyle}
            />
          </div>

          {/* Azure Default Credential toggle */}
          {isAzure && (
            <label className="flex items-center gap-3 cursor-pointer">
              <input
                type="checkbox"
                checked={useDefaultCreds}
                onChange={e => setUseDefaultCreds(e.target.checked)}
                className="h-4 w-4 rounded border-stone bg-basalt accent-amber"
              />
              <span className="text-sm text-fog">
                Use Azure Default Credential <span className="text-dust">(Managed Identity, Azure CLI, etc.)</span>
              </span>
            </label>
          )}

          {/* API Key */}
          {needsApiKey && (
            <div>
              <label className="mb-1 block text-sm text-fog">API Key</label>
              <input
                type="password"
                value={apiKey}
                onChange={e => setApiKey(e.target.value)}
                placeholder="sk-... or your API key"
                className={inputStyle}
              />
            </div>
          )}

          <div className="flex gap-2 pt-1">
            <button
              onClick={handleCreate}
              disabled={busy || !modelName || (needsApiKey && !apiKey) || (isAzure && !endpoint)}
              className={btnPrimary}
            >
              {busy ? 'Saving...' : 'Save Provider'}
            </button>
            <button onClick={() => setShowForm(false)} className={btnSecondary}>Cancel</button>
          </div>
        </section>
      ) : (
        <button onClick={() => setShowForm(true)} className={btnSecondary}>
          + Add AI Provider
        </button>
      )}

      {error && (
        <div className="rounded-xl border border-ember/30 bg-ember/10 px-4 py-2.5 text-sm text-rose">
          {error}
        </div>
      )}

      <div className="flex items-center justify-between">
        <button
          onClick={onComplete}
          className="text-sm text-dust hover:text-fog underline underline-offset-2"
        >
          Skip for now
        </button>
        <button
          onClick={onComplete}
          disabled={!hasChatProvider}
          className={`group inline-flex items-center gap-2 ${btnPrimary}`}
        >
          Next
          <ArrowRight className="h-4 w-4 transition-transform group-hover:translate-x-0.5" />
        </button>
      </div>
    </div>
  )
}

/* ── Step 3b: Agent Status (waiting for agents) ─────── */

function AgentStatusStep({ onComplete }: { onComplete: () => void }) {
  const [agentStatus, setAgentStatus] = useState<AgentStatusResponse | null>(null)
  const [elapsed, setElapsed] = useState(0)

  useEffect(() => {
    let cancelled = false
    const poll = async () => {
      while (!cancelled) {
        try {
          const status = await fetchAgentStatus()
          if (!cancelled) setAgentStatus(status)
          if (status.phase === 'ready') return
        } catch {
          // keep polling
        }
        await new Promise(r => setTimeout(r, 3000))
      }
    }
    poll()
    return () => { cancelled = true }
  }, [])

  useEffect(() => {
    const timer = setInterval(() => setElapsed(e => e + 1), 1000)
    return () => clearInterval(timer)
  }, [])

  const isReady = agentStatus?.phase === 'ready'
  const isInitializing = agentStatus?.phase === 'initializing'

  return (
    <div className="space-y-6">
      <h2 className="font-display text-xl font-semibold text-light">Starting Agents</h2>

      <div className="flex flex-col items-center gap-4 py-6">
        {isReady ? (
          <>
            <div className="flex h-16 w-16 items-center justify-center rounded-2xl bg-sage/10">
              <CheckCircle2 className="h-8 w-8 text-sage" />
            </div>
            <p className="text-sm text-sage">
              {agentStatus.agentCount} agent{agentStatus.agentCount !== 1 ? 's' : ''} initialized and ready!
            </p>
          </>
        ) : (
          <>
            <div className="flex h-16 w-16 items-center justify-center rounded-2xl bg-amber/10">
              <Loader2 className="h-8 w-8 animate-spin text-amber" />
            </div>
            <p className="text-sm text-fog">
              {isInitializing
                ? 'Agents are initializing — loading Home Assistant entities and registering skills...'
                : 'Waiting for configuration to propagate...'}
            </p>
            <p className="text-xs text-dust">{elapsed}s elapsed</p>
          </>
        )}
      </div>

      {/* Agent list */}
      {agentStatus && agentStatus.agents.length > 0 && (
        <section className="space-y-2">
          <h3 className="text-sm font-medium text-fog">Registered Agents</h3>
          {agentStatus.agents.map((a, i) => (
            <div key={i} className="flex items-center gap-3 rounded-xl border border-stone bg-basalt/50 px-4 py-3">
              <Cpu className="h-4 w-4 shrink-0 text-amber" />
              <div className="min-w-0 flex-1">
                <p className="truncate text-sm font-medium text-light">{a.name}</p>
                <p className="truncate text-xs text-dust">{a.description}</p>
              </div>
            </div>
          ))}
        </section>
      )}

      <div className="flex items-center justify-between">
        <button
          onClick={onComplete}
          className="text-sm text-dust hover:text-fog underline underline-offset-2"
        >
          {isReady ? '' : 'Skip — configure later'}
        </button>
        <button
          onClick={onComplete}
          disabled={!isReady}
          className={`group inline-flex items-center gap-2 ${btnPrimary}`}
        >
          Next
          <ArrowRight className="h-4 w-4 transition-transform group-hover:translate-x-0.5" />
        </button>
      </div>
    </div>
  )
}

/* ── Step 4: Connect HA Plugin → Lucia ──────────────── */

function HaPluginStep({
  status,
  onComplete,
}: {
  status: SetupStatus | null
  onComplete: () => Promise<void>
}) {
  const [haKey, setHaKey] = useState<GenerateKeyResponse | null>(null)
  const [keyCopied, setKeyCopied] = useState(false)
  const [pluginConnected, setPluginConnected] = useState(status?.pluginValidated ?? false)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const [polling, setPolling] = useState(false)

  async function handleGenerateHaKey() {
    setError('')
    setBusy(true)
    try {
      const result = await generateHaKey()
      setHaKey(result)
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Failed to generate HA key')
    } finally {
      setBusy(false)
    }
  }

  const pollForPlugin = useCallback(async () => {
    setPolling(true)
    const maxAttempts = 60
    for (let i = 0; i < maxAttempts; i++) {
      try {
        const result = await fetchHaStatus()
        if (result.pluginConnected) {
          setPluginConnected(true)
          setPolling(false)
          return
        }
      } catch {
        // continue polling
      }
      await new Promise((r) => setTimeout(r, 5000))
    }
    setPolling(false)
    setError('Timed out waiting for Home Assistant plugin. You can complete setup and configure later.')
  }, [])

  async function handleCopyKey() {
    if (haKey) {
      await navigator.clipboard.writeText(haKey.key)
      setKeyCopied(true)
    }
  }

  return (
    <div className="space-y-6">
      <h2 className="font-display text-xl font-semibold text-light">Connect Home Assistant Plugin</h2>

      {/* 3a: Generate HA key */}
      <section className="rounded-xl border border-stone bg-basalt/50 p-5">
        <h3 className="mb-3 flex items-center gap-2 font-display text-sm font-semibold text-amber">
          <Key className="h-4 w-4" /> Generate Home Assistant API Key
        </h3>
        <p className="mb-3 text-sm text-fog">
          This key lets the Home Assistant plugin authenticate with Lucia.
        </p>
        {!haKey ? (
          <button onClick={handleGenerateHaKey} disabled={busy} className={btnPrimary}>
            {busy ? 'Generating...' : 'Generate HA Key'}
          </button>
        ) : (
          <div className="space-y-3">
            <p className="flex items-center gap-1.5 text-sm text-sage">
              <CheckCircle2 className="h-4 w-4" /> Key generated — copy it for the HA plugin config
            </p>
            <div className="flex items-center gap-2">
              <code className="flex-1 rounded-lg bg-void px-3 py-2.5 font-mono text-sm text-amber select-all">
                {haKey.key}
              </code>
              <button onClick={handleCopyKey} className={btnSecondary + ' !px-3 !py-2.5'}>
                {keyCopied ? <Check className="h-4 w-4 text-sage" /> : <Copy className="h-4 w-4" />}
              </button>
            </div>
          </div>
        )}
      </section>

      {/* 3b: Configure HA Plugin */}
      <section className="rounded-xl border border-stone bg-basalt/50 p-5">
        <h3 className="mb-3 flex items-center gap-2 font-display text-sm font-semibold text-amber">
          <Plug className="h-4 w-4" /> Configure the HA Plugin
        </h3>
        <div className="text-sm text-fog">
          <ol className="list-inside list-decimal space-y-1.5">
            <li>
              In Home Assistant, go to <strong className="text-light">Settings → Devices & Services → Add Integration</strong>
            </li>
            <li>Search for <strong className="text-light">Lucia</strong></li>
            <li>
              Enter your Lucia server URL (e.g., <code className="text-amber">http://lucia-host:5151</code>)
            </li>
            <li>Paste the API key you just generated above</li>
            <li>Select your routing agent and complete the setup</li>
          </ol>
        </div>
      </section>

      {/* 3c: Wait for plugin callback */}
      <section className="rounded-xl border border-stone bg-basalt/50 p-5">
        <h3 className="mb-3 flex items-center gap-2 font-display text-sm font-semibold text-amber">
          <Radio className="h-4 w-4" /> Waiting for Plugin
        </h3>
        {pluginConnected ? (
          <p className="flex items-center gap-1.5 text-sm text-sage">
            <CheckCircle2 className="h-4 w-4" /> Home Assistant plugin connected successfully!
          </p>
        ) : polling ? (
          <div className="flex items-center gap-2 text-sm text-amber">
            <span className="inline-block h-2.5 w-2.5 animate-pulse rounded-full bg-amber" />
            Waiting for Home Assistant plugin to connect...
          </div>
        ) : (
          <button onClick={pollForPlugin} disabled={!haKey} className={btnSecondary}>
            Start Waiting for Plugin
          </button>
        )}
      </section>

      {error && (
        <div className="rounded-xl border border-ember/30 bg-ember/10 px-4 py-2.5 text-sm text-rose">
          {error}
        </div>
      )}

      <div className="flex justify-end">
        <button onClick={onComplete} disabled={busy} className={`group inline-flex items-center gap-2 ${btnPrimary}`}>
          {pluginConnected ? 'Complete Setup' : 'Skip & Complete Later'}
          <ArrowRight className="h-4 w-4 transition-transform group-hover:translate-x-0.5" />
        </button>
      </div>
    </div>
  )
}

/* ── Step 5: Done ───────────────────────────────────── */

function DoneStep() {
  return (
    <div className="space-y-6 text-center">
      <div className="mx-auto flex h-16 w-16 items-center justify-center rounded-2xl bg-sage/10">
        <CheckCircle2 className="h-8 w-8 text-sage" />
      </div>
      <div>
        <h2 className="font-display text-2xl font-semibold text-light">Setup Complete!</h2>
        <p className="mt-2 text-sm text-fog">
          Lucia is configured and ready to manage your home. You can now log in using your
          Dashboard API key.
        </p>
      </div>
      <a
        href="/"
        className={`group inline-flex items-center gap-2 ${btnPrimary}`}
      >
        Go to Dashboard
        <ArrowRight className="h-4 w-4 transition-transform group-hover:translate-x-0.5" />
      </a>
    </div>
  )
}
