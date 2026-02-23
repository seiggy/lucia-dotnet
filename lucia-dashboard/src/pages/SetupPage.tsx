import { useState, useEffect, useCallback } from 'react'
import { useAuth } from '../auth/AuthContext'
import {
  fetchSetupStatus,
  generateDashboardKey,
  regenerateDashboardKey,
  configureHomeAssistant,
  testHaConnection,
  generateHaKey,
  fetchHaStatus,
  completeSetup,
} from '../api'
import type { SetupStatus, GenerateKeyResponse, TestHaConnectionResponse } from '../api'
import { Sparkles, ArrowRight, Key, Plug, CheckCircle2, Copy, Check, Loader2, Radio } from 'lucide-react'

type WizardStep = 'welcome' | 'lucia-ha' | 'ha-plugin' | 'done'

export default function SetupPage() {
  const { refresh } = useAuth()
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
              onComplete={(s) => {
                setStatus(s)
                setStep('ha-plugin')
              }}
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
    { key: 'ha-plugin', label: 'Connect', icon: Plug },
    { key: 'done', label: 'Done', icon: CheckCircle2 },
  ]
  const idx = steps.findIndex((s) => s.key === current)

  return (
    <div className="mb-8 flex items-center justify-center gap-1 sm:gap-2">
      {steps.map((s, i) => {
        const Icon = s.icon
        const active = i <= idx
        return (
          <div key={s.key} className="flex items-center gap-1 sm:gap-2">
            <div
              className={`flex h-8 w-8 items-center justify-center rounded-full transition-colors ${
                active ? 'bg-amber/20 text-amber' : 'bg-basalt text-dust'
              }`}
            >
              {i < idx ? <Check className="h-4 w-4" /> : <Icon className="h-4 w-4" />}
            </div>
            <span className={`hidden text-xs font-medium sm:inline ${active ? 'text-light' : 'text-dust'}`}>
              {s.label}
            </span>
            {i < steps.length - 1 && <div className={`mx-1 h-px w-6 sm:mx-2 sm:w-10 ${active ? 'bg-amber/30' : 'bg-stone'}`} />}
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
  onComplete,
}: {
  status: SetupStatus | null
  onComplete: (s: SetupStatus) => void
}) {
  const [dashboardKey, setDashboardKey] = useState<GenerateKeyResponse | null>(null)
  const [keyCopied, setKeyCopied] = useState(false)
  const [haUrl, setHaUrl] = useState(status?.haUrl ?? '')
  const [haToken, setHaToken] = useState('')
  const [haSaved, setHaSaved] = useState(false)
  const [testResult, setTestResult] = useState<TestHaConnectionResponse | null>(null)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  const hasDashKey = status?.hasDashboardKey || dashboardKey !== null

  async function handleGenerateKey() {
    setError('')
    setBusy(true)
    try {
      const result = await generateDashboardKey()
      setDashboardKey(result)
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Failed to generate key')
    } finally {
      setBusy(false)
    }
  }

  async function handleRegenerateKey() {
    setError('')
    setBusy(true)
    try {
      const result = await regenerateDashboardKey()
      setDashboardKey(result)
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Failed to regenerate key')
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
            <p className="flex items-center gap-1.5 text-sm text-sage">
              <CheckCircle2 className="h-4 w-4" /> Dashboard key already exists
            </p>
            <p className="text-sm text-dust">
              Lost your key? You can regenerate it below. The old key will be revoked.
            </p>
            <button onClick={handleRegenerateKey} disabled={busy} className={btnSecondary}>
              {busy ? 'Regenerating...' : 'Regenerate Dashboard Key'}
            </button>
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
          disabled={!hasDashKey || (!haSaved && !status?.hasHaConnection)}
          className={`group inline-flex items-center gap-2 ${btnPrimary}`}
        >
          Next
          <ArrowRight className="h-4 w-4 transition-transform group-hover:translate-x-0.5" />
        </button>
      </div>
    </div>
  )
}

/* ── Step 3: Connect HA Plugin → Lucia ──────────────── */

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

/* ── Step 4: Done ───────────────────────────────────── */

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
