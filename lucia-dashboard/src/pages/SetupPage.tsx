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
        // Always start at welcome so users can navigate through all steps
        // and regenerate keys if needed
        if (s.setupComplete) setStep('done')
        else setStep('welcome')
      })
      .finally(() => setLoading(false))
  }, [])

  if (loading) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-gray-900">
        <div className="text-gray-400">Loading setup...</div>
      </div>
    )
  }

  return (
    <div className="flex min-h-screen items-center justify-center bg-gray-900 p-4">
      <div className="w-full max-w-2xl rounded-xl border border-gray-700 bg-gray-800 p-8 shadow-2xl">
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
  )
}

/* ── Step indicator ─────────────────────────────────── */

function StepIndicator({ current }: { current: WizardStep }) {
  const steps: { key: WizardStep; label: string }[] = [
    { key: 'welcome', label: 'Welcome' },
    { key: 'lucia-ha', label: 'Configure' },
    { key: 'ha-plugin', label: 'Connect HA' },
    { key: 'done', label: 'Done' },
  ]
  const idx = steps.findIndex((s) => s.key === current)

  return (
    <div className="mb-8 flex items-center justify-center gap-2">
      {steps.map((s, i) => (
        <div key={s.key} className="flex items-center gap-2">
          <div
            className={`flex h-8 w-8 items-center justify-center rounded-full text-xs font-bold ${
              i <= idx ? 'bg-indigo-600 text-white' : 'bg-gray-700 text-gray-500'
            }`}
          >
            {i < idx ? '✓' : i + 1}
          </div>
          <span className={`text-sm ${i <= idx ? 'text-white' : 'text-gray-500'}`}>{s.label}</span>
          {i < steps.length - 1 && <div className="mx-2 h-px w-8 bg-gray-600" />}
        </div>
      ))}
    </div>
  )
}

/* ── Step 1: Welcome ────────────────────────────────── */

function WelcomeStep({ onNext }: { onNext: () => void }) {
  return (
    <div className="space-y-4 text-center">
      <h2 className="text-2xl font-bold text-indigo-400">Welcome to Lucia</h2>
      <p className="text-gray-300">
        Lucia is your privacy-first home automation assistant. This wizard will help you set up
        secure access and connect to Home Assistant.
      </p>
      <div className="rounded-lg border border-gray-600 bg-gray-700/50 p-4 text-left text-sm text-gray-300">
        <p className="mb-2 font-semibold text-white">What we'll do:</p>
        <ol className="list-inside list-decimal space-y-1">
          <li>Generate an API key for the Lucia dashboard</li>
          <li>Connect Lucia to your Home Assistant instance</li>
          <li>Set up the Home Assistant plugin to talk back to Lucia</li>
        </ol>
      </div>
      <button
        onClick={onNext}
        className="rounded-lg bg-indigo-600 px-6 py-2 text-sm font-medium text-white transition hover:bg-indigo-500"
      >
        Get Started
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
      <h2 className="text-xl font-bold text-white">Configure Lucia & Home Assistant</h2>

      {/* 2a: Dashboard API Key */}
      <section className="rounded-lg border border-gray-600 bg-gray-700/30 p-4">
        <h3 className="mb-2 font-semibold text-indigo-400">1. Dashboard API Key</h3>
        {!hasDashKey ? (
          <>
            <p className="mb-3 text-sm text-gray-300">
              Generate an API key to log into the Lucia dashboard. Save it — you won't see it again.
            </p>
            <button
              onClick={handleGenerateKey}
              disabled={busy}
              className="rounded bg-indigo-600 px-4 py-1.5 text-sm font-medium text-white hover:bg-indigo-500 disabled:opacity-50"
            >
              {busy ? 'Generating...' : 'Generate Dashboard Key'}
            </button>
          </>
        ) : dashboardKey ? (
          <div className="space-y-2">
            <p className="text-sm text-green-400">✓ Key generated — save it now!</p>
            <div className="flex items-center gap-2">
              <code className="flex-1 rounded bg-gray-900 px-3 py-2 font-mono text-sm text-yellow-300 select-all">
                {dashboardKey.key}
              </code>
              <button
                onClick={handleCopyKey}
                className="rounded bg-gray-600 px-3 py-2 text-xs text-white hover:bg-gray-500"
              >
                {keyCopied ? 'Copied!' : 'Copy'}
              </button>
            </div>
            <p className="text-xs text-gray-400">
              This key will be used to log into the dashboard after setup completes.
            </p>
          </div>
        ) : (
          <div className="space-y-2">
            <p className="text-sm text-green-400">✓ Dashboard key already exists</p>
            <p className="text-sm text-gray-400">
              Lost your key? You can regenerate it below. The old key will be revoked.
            </p>
            <button
              onClick={handleRegenerateKey}
              disabled={busy}
              className="rounded bg-amber-600 px-4 py-1.5 text-sm font-medium text-white hover:bg-amber-500 disabled:opacity-50"
            >
              {busy ? 'Regenerating...' : 'Regenerate Dashboard Key'}
            </button>
          </div>
        )}
      </section>

      {/* 2b: Home Assistant Connection */}
      <section className="rounded-lg border border-gray-600 bg-gray-700/30 p-4">
        <h3 className="mb-2 font-semibold text-indigo-400">2. Home Assistant Connection</h3>
        <p className="mb-3 text-sm text-gray-300">
          Enter your Home Assistant URL and a long-lived access token.
        </p>
        <div className="mb-2 rounded border border-gray-600 bg-gray-800 p-3 text-xs text-gray-400">
          <p className="mb-1 font-semibold text-gray-300">How to create a long-lived access token:</p>
          <ol className="list-inside list-decimal space-y-0.5">
            <li>Open your Home Assistant instance</li>
            <li>Click your profile picture (bottom-left)</li>
            <li>Scroll to <strong>Security</strong> tab</li>
            <li>Under "Long-lived access tokens", click <strong>Create Token</strong></li>
            <li>Name it "Lucia" and copy the token</li>
          </ol>
        </div>
        <div className="space-y-2">
          <input
            type="url"
            placeholder="http://homeassistant.local:8123"
            value={haUrl}
            onChange={(e) => setHaUrl(e.target.value)}
            className="w-full rounded border border-gray-600 bg-gray-700 px-3 py-2 text-sm text-white placeholder-gray-500 focus:border-indigo-500 focus:outline-none"
          />
          <input
            type="password"
            placeholder="Paste your long-lived access token"
            value={haToken}
            onChange={(e) => setHaToken(e.target.value)}
            className="w-full rounded border border-gray-600 bg-gray-700 px-3 py-2 text-sm text-white placeholder-gray-500 focus:border-indigo-500 focus:outline-none"
          />
          <div className="flex gap-2">
            <button
              onClick={handleSaveHa}
              disabled={busy || !haUrl || !haToken}
              className="rounded bg-indigo-600 px-4 py-1.5 text-sm font-medium text-white hover:bg-indigo-500 disabled:opacity-50"
            >
              {busy ? 'Saving...' : 'Save'}
            </button>
            {haSaved && (
              <button
                onClick={handleTestConnection}
                disabled={busy}
                className="rounded bg-emerald-600 px-4 py-1.5 text-sm font-medium text-white hover:bg-emerald-500 disabled:opacity-50"
              >
                {busy ? 'Testing...' : 'Test Connection'}
              </button>
            )}
          </div>
        </div>
        {testResult && (
          <div
            className={`mt-2 rounded p-2 text-sm ${
              testResult.connected
                ? 'border border-green-800 bg-green-900/30 text-green-300'
                : 'border border-red-800 bg-red-900/30 text-red-300'
            }`}
          >
            {testResult.connected
              ? `✓ Connected to ${testResult.locationName ?? 'Home Assistant'} (v${testResult.haVersion})`
              : `✗ ${testResult.error}`}
          </div>
        )}
      </section>

      {error && (
        <div className="rounded border border-red-800 bg-red-900/30 px-3 py-2 text-sm text-red-300">
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
          className="rounded-lg bg-indigo-600 px-6 py-2 text-sm font-medium text-white transition hover:bg-indigo-500 disabled:cursor-not-allowed disabled:opacity-50"
        >
          Next →
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
    const maxAttempts = 60 // 5 minutes at 5s intervals
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
      <h2 className="text-xl font-bold text-white">Connect Home Assistant Plugin</h2>

      {/* 3a: Generate HA key */}
      <section className="rounded-lg border border-gray-600 bg-gray-700/30 p-4">
        <h3 className="mb-2 font-semibold text-indigo-400">1. Generate Home Assistant API Key</h3>
        <p className="mb-3 text-sm text-gray-300">
          This key lets the Home Assistant plugin authenticate with Lucia.
        </p>
        {!haKey ? (
          <button
            onClick={handleGenerateHaKey}
            disabled={busy}
            className="rounded bg-indigo-600 px-4 py-1.5 text-sm font-medium text-white hover:bg-indigo-500 disabled:opacity-50"
          >
            {busy ? 'Generating...' : 'Generate HA Key'}
          </button>
        ) : (
          <div className="space-y-2">
            <p className="text-sm text-green-400">✓ Key generated — copy it for the HA plugin config</p>
            <div className="flex items-center gap-2">
              <code className="flex-1 rounded bg-gray-900 px-3 py-2 font-mono text-sm text-yellow-300 select-all">
                {haKey.key}
              </code>
              <button
                onClick={handleCopyKey}
                className="rounded bg-gray-600 px-3 py-2 text-xs text-white hover:bg-gray-500"
              >
                {keyCopied ? 'Copied!' : 'Copy'}
              </button>
            </div>
          </div>
        )}
      </section>

      {/* 3b: Configure HA Plugin */}
      <section className="rounded-lg border border-gray-600 bg-gray-700/30 p-4">
        <h3 className="mb-2 font-semibold text-indigo-400">2. Configure the HA Plugin</h3>
        <div className="text-sm text-gray-300">
          <ol className="list-inside list-decimal space-y-1">
            <li>
              In Home Assistant, go to <strong>Settings → Devices & Services → Add Integration</strong>
            </li>
            <li>Search for <strong>Lucia</strong></li>
            <li>
              Enter your Lucia server URL (e.g., <code className="text-indigo-300">http://lucia-host:5151</code>)
            </li>
            <li>Paste the API key you just generated above</li>
            <li>Select your routing agent and complete the setup</li>
          </ol>
        </div>
      </section>

      {/* 3c: Wait for plugin callback */}
      <section className="rounded-lg border border-gray-600 bg-gray-700/30 p-4">
        <h3 className="mb-2 font-semibold text-indigo-400">3. Waiting for Plugin</h3>
        {pluginConnected ? (
          <p className="text-sm text-green-400">✓ Home Assistant plugin connected successfully!</p>
        ) : polling ? (
          <div className="flex items-center gap-2 text-sm text-yellow-300">
            <span className="inline-block h-3 w-3 animate-pulse rounded-full bg-yellow-400" />
            Waiting for Home Assistant plugin to connect...
          </div>
        ) : (
          <button
            onClick={pollForPlugin}
            disabled={!haKey}
            className="rounded bg-amber-600 px-4 py-1.5 text-sm font-medium text-white hover:bg-amber-500 disabled:opacity-50"
          >
            Start Waiting for Plugin
          </button>
        )}
      </section>

      {error && (
        <div className="rounded border border-red-800 bg-red-900/30 px-3 py-2 text-sm text-red-300">
          {error}
        </div>
      )}

      <div className="flex justify-end">
        <button
          onClick={onComplete}
          disabled={busy}
          className="rounded-lg bg-indigo-600 px-6 py-2 text-sm font-medium text-white transition hover:bg-indigo-500 disabled:opacity-50"
        >
          {pluginConnected ? 'Complete Setup' : 'Skip & Complete Later'}
        </button>
      </div>
    </div>
  )
}

/* ── Step 4: Done ───────────────────────────────────── */

function DoneStep() {
  return (
    <div className="space-y-4 text-center">
      <div className="text-5xl">🎉</div>
      <h2 className="text-2xl font-bold text-green-400">Setup Complete!</h2>
      <p className="text-gray-300">
        Lucia is configured and ready to manage your home. You can now log in using your
        Dashboard API key.
      </p>
      <a
        href="/"
        className="inline-block rounded-lg bg-indigo-600 px-6 py-2 text-sm font-medium text-white transition hover:bg-indigo-500"
      >
        Go to Dashboard
      </a>
    </div>
  )
}
