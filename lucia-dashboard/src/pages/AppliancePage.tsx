import { useCallback, useEffect, useState } from 'react'
import {
  Activity,
  CheckCircle2,
  CloudDownload,
  Cpu,
  ExternalLink,
  HardDrive,
  Loader2,
  Power,
  RefreshCw,
  RotateCw,
  Server,
  Wifi,
} from 'lucide-react'
import ConfirmDialog from '../components/ConfirmDialog'
import ToggleSwitch from '../components/ToggleSwitch'
import {
  checkApplianceUpdates,
  fetchApplianceStatus,
  fetchApplianceTelemetry,
  rebootAppliance,
  restartApplianceService,
  updateApplianceTelemetry,
} from '../appliance-api'
import type {
  ApplianceServiceStatus,
  ApplianceStatus,
  ApplianceTelemetryStatus,
  ApplianceUpdateStatus,
} from '../appliance-api'

const primaryButton = 'inline-flex min-h-11 items-center justify-center gap-2 rounded-xl bg-amber px-4 py-2.5 text-sm font-semibold text-on-accent transition-colors hover:bg-amber-glow focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-amber/60 disabled:cursor-not-allowed disabled:opacity-40'
const secondaryButton = 'inline-flex min-h-11 items-center justify-center gap-2 rounded-xl border border-stone bg-basalt px-4 py-2.5 text-sm font-medium text-fog transition-colors hover:border-amber/30 hover:text-light focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-amber/60 disabled:cursor-not-allowed disabled:opacity-40'
const inputStyle = 'min-h-11 w-full rounded-xl border border-stone bg-basalt px-3 py-2.5 text-base text-light placeholder:text-dust input-focus'

const serviceLabels: Record<string, { label: string; detail: string }> = {
  agenthost: {
    label: 'Lucia AgentHost',
    detail: 'Dashboard, agents, voice, and integrations',
  },
  redis: {
    label: 'Redis',
    detail: 'Active task and session persistence',
  },
  collector: {
    label: 'OpenTelemetry Collector',
    detail: 'Metrics, traces, and logs export',
  },
  'redis-exporter': {
    label: 'Redis exporter',
    detail: 'Redis health and performance metrics',
  },
}

export default function AppliancePage() {
  const [status, setStatus] = useState<ApplianceStatus | null>(null)
  const [telemetry, setTelemetry] = useState<ApplianceTelemetryStatus | null>(null)
  const [updates, setUpdates] = useState<ApplianceUpdateStatus | null>(null)
  const [loading, setLoading] = useState(true)
  const [checkingUpdates, setCheckingUpdates] = useState(false)
  const [busyService, setBusyService] = useState<string | null>(null)
  const [showReboot, setShowReboot] = useState(false)
  const [notice, setNotice] = useState('')
  const [error, setError] = useState('')

  const load = useCallback(async () => {
    setError('')
    try {
      const [nextStatus, nextTelemetry] = await Promise.all([
        fetchApplianceStatus(),
        fetchApplianceTelemetry(),
      ])
      setStatus(nextStatus)
      setTelemetry(nextTelemetry)
    } catch (loadError: unknown) {
      setError(loadError instanceof Error ? loadError.message : 'Appliance status is unavailable.')
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  async function handleCheckUpdates() {
    setCheckingUpdates(true)
    setError('')
    try {
      setUpdates(await checkApplianceUpdates())
    } catch (updateError: unknown) {
      setError(updateError instanceof Error ? updateError.message : 'Update check failed.')
    } finally {
      setCheckingUpdates(false)
    }
  }

  async function handleRestart(service: string) {
    setBusyService(service)
    setError('')
    try {
      await restartApplianceService(service)
      setNotice(`${serviceLabels[service]?.label ?? service} restart requested.`)
      await load()
    } catch (restartError: unknown) {
      setError(restartError instanceof Error ? restartError.message : 'Service restart failed.')
    } finally {
      setBusyService(null)
    }
  }

  async function handleReboot() {
    setShowReboot(false)
    setError('')
    try {
      await rebootAppliance()
      setNotice('Jetson reboot requested. The dashboard will disconnect briefly.')
    } catch (rebootError: unknown) {
      setError(rebootError instanceof Error ? rebootError.message : 'Jetson reboot failed.')
    }
  }

  if (loading) {
    return (
      <div className="flex min-h-[60vh] items-center justify-center gap-3 text-fog">
        <Loader2 className="h-5 w-5 animate-spin text-amber" />
        Loading appliance...
      </div>
    )
  }

  return (
    <div className="mx-auto max-w-6xl space-y-6">
      <header className="flex flex-col gap-4 border-b border-stone pb-5 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <h1 className="font-display text-2xl font-semibold tracking-tight text-light">
            {status?.hostname ?? 'Lucia appliance'}
          </h1>
          <p className="mt-1 text-sm text-fog">
            Jetson appliance health, updates, telemetry, and host controls
          </p>
        </div>
        <button type="button" onClick={() => void load()} className={secondaryButton}>
          <RefreshCw className="h-4 w-4" />
          Refresh status
        </button>
      </header>

      {error && (
        <p role="alert" className="rounded-xl border border-rose/30 bg-rose/8 px-4 py-3 text-sm text-rose">
          {error}
        </p>
      )}
      {notice && (
        <p role="status" className="rounded-xl border border-sage/30 bg-sage/8 px-4 py-3 text-sm text-sage">
          {notice}
        </p>
      )}

      {status && <IdentityStrip status={status} />}

      <section className="rounded-2xl border border-stone bg-charcoal">
        <div className="flex flex-col gap-3 border-b border-stone p-5 sm:flex-row sm:items-center sm:justify-between">
          <div>
            <h2 className="font-display text-xl font-semibold text-light">Release channels</h2>
            <p className="mt-1 text-sm text-fog">Lucia and Jetson OS update independently.</p>
          </div>
          <button
            type="button"
            onClick={handleCheckUpdates}
            disabled={checkingUpdates}
            className={primaryButton}
          >
            {checkingUpdates
              ? <Loader2 className="h-4 w-4 animate-spin" />
              : <CloudDownload className="h-4 w-4" />}
            {checkingUpdates ? 'Checking GitHub...' : 'Check for updates'}
          </button>
        </div>
        <div className={checkingUpdates ? 'appliance-update-scan' : ''}>
          <UpdateRail
            icon={Cpu}
            title="Lucia"
            current={updates?.currentLuciaVersion ?? status?.luciaVersion ?? 'unknown'}
            latest={updates?.latestLuciaVersion}
            available={updates?.luciaUpdateAvailable ?? false}
            newerDiscovered={updates?.luciaNewerDiscovered ?? false}
            manifestAvailable={updates?.manifestAvailable ?? false}
            compatible={updates?.compatible ?? true}
            checked={updates !== null}
          />
          <UpdateRail
            icon={HardDrive}
            title="Jetson OS"
            current={updates?.currentOsVersion ?? status?.os.imageVersion ?? 'unknown'}
            latest={updates?.latestOsVersion}
            available={updates?.osUpdateAvailable ?? false}
            newerDiscovered={updates?.osNewerDiscovered ?? false}
            manifestAvailable={updates?.manifestAvailable ?? false}
            compatible={updates?.compatible ?? true}
            checked={updates !== null}
          />
        </div>
        {updates?.message && (
          <p className="border-t border-stone px-5 py-3 text-sm text-amber">
            {updates.message}
          </p>
        )}
        {updates?.releaseUrl && (
          <a
            href={updates.releaseUrl}
            target="_blank"
            rel="noreferrer"
            className="inline-flex min-h-11 items-center gap-2 px-5 py-3 text-sm text-fog hover:text-light"
          >
            View GitHub release <ExternalLink className="h-4 w-4" />
          </a>
        )}
      </section>

      <section className="rounded-2xl border border-stone bg-charcoal">
        <div className="border-b border-stone p-5">
          <h2 className="font-display text-xl font-semibold text-light">Host services</h2>
          <p className="mt-1 text-sm text-fog">Restart one process without rebooting the Jetson.</p>
        </div>
        <div className="divide-y divide-stone">
          {status?.services.map((service) => (
            <ServiceRow
              key={service.id}
              service={service}
              busy={busyService === service.id}
              canRestart={service.id === 'redis'
                || (telemetry?.enabled === true
                  && (service.id === 'collector' || service.id === 'redis-exporter'))}
              onRestart={() => handleRestart(service.id)}
            />
          ))}
        </div>
      </section>

      {telemetry && (
        <TelemetryPanel
          telemetry={telemetry}
          onSaved={(nextTelemetry) => {
            setTelemetry(nextTelemetry)
            setNotice(nextTelemetry.enabled
              ? 'Telemetry configuration saved and enabled.'
              : 'Telemetry configuration saved and disabled.')
          }}
          onError={setError}
        />
      )}

      <section className="flex flex-col gap-4 rounded-2xl border border-rose/25 bg-rose/5 p-5 sm:flex-row sm:items-center sm:justify-between">
        <div>
          <h2 className="font-display text-lg font-semibold text-light">Restart Jetson OS</h2>
          <p className="mt-1 text-sm text-fog">
            Active conversations and voice processing will stop until the appliance returns.
          </p>
        </div>
        <button type="button" onClick={() => setShowReboot(true)} className={secondaryButton}>
          <Power className="h-4 w-4 text-rose" />
          Reboot Jetson
        </button>
      </section>

      <ConfirmDialog
        open={showReboot}
        title="Reboot the Jetson?"
        message="Lucia will disconnect for several minutes. Persistent tasks and configuration will remain."
        confirmLabel="Reboot Jetson"
        onConfirm={() => void handleReboot()}
        onCancel={() => setShowReboot(false)}
      />
    </div>
  )
}

function IdentityStrip({ status }: { status: ApplianceStatus }) {
  const activeServices = status.services.filter((service) => service.activeState === 'active').length
  return (
    <section className="grid gap-px overflow-hidden rounded-2xl border border-stone bg-stone sm:grid-cols-2 lg:grid-cols-4">
      <IdentityCell icon={CheckCircle2} label="Services" value={`${activeServices}/${status.services.length} active`} />
      <IdentityCell icon={Server} label="Lucia" value={status.luciaVersion} />
      <IdentityCell icon={HardDrive} label="Jetson Linux" value={status.os.jetsonLinuxVersion} />
      <IdentityCell
        icon={Wifi}
        label="Wi-Fi"
        value={status.network.signal === null
          ? status.network.ssid
          : `${status.network.ssid} · ${status.network.signal}%`}
      />
    </section>
  )
}

function IdentityCell({
  icon: Icon,
  label,
  value,
}: {
  icon: typeof Activity
  label: string
  value: string
}) {
  return (
    <div className="flex items-center gap-3 bg-charcoal px-4 py-4">
      <Icon className="h-4 w-4 shrink-0 text-amber" />
      <div>
        <p className="text-xs font-medium uppercase tracking-wider text-dust">{label}</p>
        <p className="mt-0.5 text-sm font-semibold text-light">{value}</p>
      </div>
    </div>
  )
}

function UpdateRail({
  icon: Icon,
  title,
  current,
  latest,
  available,
  newerDiscovered,
  manifestAvailable,
  compatible,
  checked,
}: {
  icon: typeof Cpu
  title: string
  current: string
  latest?: string | null
  available: boolean
  newerDiscovered: boolean
  manifestAvailable: boolean
  compatible: boolean
  checked: boolean
}) {
  const verificationRequired = checked && newerDiscovered
  const unavailable = checked && !manifestAvailable
  const incompatible = checked && manifestAvailable && !compatible
  return (
    <div className="grid gap-4 border-b border-stone p-5 last:border-b-0 sm:grid-cols-[minmax(0,1fr)_auto] sm:items-center">
      <div className="flex min-w-0 items-center gap-3">
        <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-basalt text-amber">
          <Icon className="h-5 w-5" />
        </span>
        <div className="min-w-0">
          <h3 className="font-display text-base font-semibold text-light">{title}</h3>
          <p className="mt-1 text-sm text-fog">
            Installed {current}
            {latest && ` · Latest ${latest}`}
          </p>
        </div>
      </div>
      <div className="flex items-center gap-3">
        <span className={`text-sm font-medium ${
          available || verificationRequired
            ? 'text-amber'
            : unavailable
              ? 'text-dust'
              : checked
                ? 'text-sage'
                : 'text-dust'
        }`}>
          {available
            ? 'Update available'
            : unavailable
              ? 'No appliance manifest'
              : incompatible
                ? 'Incompatible'
                : verificationRequired
                  ? 'Verification required'
                  : checked
                    ? 'Current'
                    : 'Not checked'}
        </span>
        {available && (
          <button
            type="button"
            disabled
            title="Update apply unlocks after rollback validation"
            className={secondaryButton}
          >
            Install
          </button>
        )}
      </div>
    </div>
  )
}

function ServiceRow({
  service,
  busy,
  canRestart,
  onRestart,
}: {
  service: ApplianceServiceStatus
  busy: boolean
  canRestart: boolean
  onRestart: () => void
}) {
  const labels = serviceLabels[service.id] ?? {
    label: service.id,
    detail: 'Appliance process',
  }
  const isActive = service.activeState === 'active'
  return (
    <div className="flex flex-col gap-3 px-5 py-4 sm:flex-row sm:items-center sm:justify-between">
      <div className="flex items-start gap-3">
        <span className={`mt-1 h-2.5 w-2.5 shrink-0 rounded-full ${
          isActive ? 'bg-sage' : 'bg-dust'
        }`} />
        <div>
          <h3 className="text-sm font-semibold text-light">{labels.label}</h3>
          <p className="mt-0.5 text-sm text-fog">{labels.detail}</p>
          <p className="mt-1 text-xs text-dust">
            {service.activeState} · {service.unitFileState}
          </p>
        </div>
      </div>
      {canRestart && (
        <button
          type="button"
          onClick={onRestart}
          disabled={busy}
          className={secondaryButton}
        >
          {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <RotateCw className="h-4 w-4" />}
          Restart
        </button>
      )}
    </div>
  )
}

function TelemetryPanel({
  telemetry,
  onSaved,
  onError,
}: {
  telemetry: ApplianceTelemetryStatus
  onSaved: (telemetry: ApplianceTelemetryStatus) => void
  onError: (message: string) => void
}) {
  const [enabled, setEnabled] = useState(telemetry.enabled)
  const [endpoint, setEndpoint] = useState(telemetry.endpoint)
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [clearAuthorization, setClearAuthorization] = useState(false)
  const [insecureSkipVerify, setInsecureSkipVerify] = useState(telemetry.insecureSkipVerify)
  const [saving, setSaving] = useState(false)

  async function handleSave() {
    setSaving(true)
    onError('')
    try {
      onSaved(await updateApplianceTelemetry({
        enabled,
        endpoint,
        username: username || null,
        password: password || null,
        clearAuthorization,
        insecureSkipVerify,
      }))
      setUsername('')
      setPassword('')
      setClearAuthorization(false)
    } catch (saveError: unknown) {
      onError(saveError instanceof Error ? saveError.message : 'Telemetry configuration failed.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <section className="rounded-2xl border border-stone bg-charcoal">
      <div className="flex items-center justify-between gap-4 border-b border-stone p-5">
        <div>
          <h2 className="font-display text-xl font-semibold text-light">OpenTelemetry</h2>
          <p className="mt-1 text-sm text-fog">Export Jetson and Redis infrastructure metrics.</p>
        </div>
        <ToggleSwitch checked={enabled} onChange={setEnabled} label="Telemetry enabled" />
      </div>
      <div className="grid gap-5 p-5 sm:grid-cols-2">
        <label className="sm:col-span-2">
          <span className="mb-2 block text-sm font-medium text-light">OTLP endpoint</span>
          <input
            type="url"
            value={endpoint}
            onChange={(event) => setEndpoint(event.target.value)}
            placeholder="https://telemetry.example:4317"
            className={inputStyle}
          />
        </label>
        <label>
          <span className="mb-2 block text-sm font-medium text-light">Basic auth username</span>
          <input
            value={username}
            onChange={(event) => setUsername(event.target.value)}
            disabled={clearAuthorization}
            autoComplete="username"
            className={inputStyle}
          />
        </label>
        <label>
          <span className="mb-2 block text-sm font-medium text-light">Basic auth password</span>
          <input
            type="password"
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            disabled={clearAuthorization}
            autoComplete="new-password"
            placeholder={telemetry.hasAuthorization ? 'Saved; enter to replace' : ''}
            className={inputStyle}
          />
        </label>
        <label className="flex min-h-11 items-center gap-3 text-sm text-fog">
          <input
            type="checkbox"
            checked={clearAuthorization}
            onChange={(event) => {
              setClearAuthorization(event.target.checked)
              if (event.target.checked) {
                setUsername('')
                setPassword('')
              }
            }}
            className="h-4 w-4 accent-amber"
          />
          Remove saved authorization
        </label>
        <label className="flex min-h-11 items-start gap-3 rounded-xl border border-rose/25 bg-rose/5 p-3 text-sm text-fog">
          <input
            type="checkbox"
            checked={insecureSkipVerify}
            onChange={(event) => setInsecureSkipVerify(event.target.checked)}
            className="mt-0.5 h-4 w-4 accent-rose"
          />
          <span>
            Skip TLS certificate verification
            <span className="mt-1 block text-xs text-rose">Use only for a trusted lab endpoint.</span>
          </span>
        </label>
      </div>
      <div className="flex justify-end border-t border-stone p-5">
        <button
          type="button"
          onClick={handleSave}
          disabled={saving || !endpoint}
          className={primaryButton}
        >
          {saving ? <Loader2 className="h-4 w-4 animate-spin" /> : <Activity className="h-4 w-4" />}
          {saving ? 'Applying...' : 'Save telemetry'}
        </button>
      </div>
    </section>
  )
}
