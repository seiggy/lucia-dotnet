import { useEffect, useMemo, useState } from 'react'
import {
  ArrowLeft,
  ArrowRight,
  Check,
  CheckCircle2,
  Cpu,
  Eye,
  EyeOff,
  HardDrive,
  Loader2,
  LockKeyhole,
  Radio,
  RefreshCw,
  Router,
  ShieldCheck,
  Sparkles,
  Wifi,
} from 'lucide-react'
import {
  fetchInstallerDisks,
  fetchInstallerNetworks,
  fetchInstallerStatus,
  claimInstaller,
  startInstallation,
} from '../installer-api'
import { ThemeSelector } from '../theme/ThemeSelector'
import type {
  InstallerDisk,
  InstallerNetwork,
  InstallerStatus,
} from '../installer-api'

type InstallerStep = 'claim' | 'storage' | 'identity' | 'review' | 'installing'
type BootState = 'preparing' | 'active' | 'exiting' | 'hidden'

const stepOrder: InstallerStep[] = [
  'claim',
  'storage',
  'identity',
  'review',
  'installing',
]

const inputStyle = 'min-h-12 w-full rounded-xl border border-stone bg-basalt px-4 py-3 text-base text-light placeholder:text-dust input-focus'
const primaryButton = 'inline-flex min-h-12 items-center justify-center gap-2 rounded-xl bg-amber px-5 py-3 text-sm font-semibold text-on-accent transition-colors hover:bg-amber-glow focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-amber/60 disabled:cursor-not-allowed disabled:opacity-40'
const secondaryButton = 'inline-flex min-h-12 items-center justify-center gap-2 rounded-xl border border-stone bg-basalt px-5 py-3 text-sm font-semibold text-fog transition-colors hover:border-amber/30 hover:text-light focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-amber/60'

export default function InstallerPage() {
  const [bootState, setBootState] = useState<BootState>(() => {
    try {
      return window.sessionStorage.getItem('lucia-installer-boot-seen') === 'true'
        ? 'hidden'
        : 'preparing'
    } catch (storageError: unknown) {
      if (!(storageError instanceof DOMException)) throw storageError
      return 'preparing'
    }
  })
  const [step, setStep] = useState<InstallerStep>('claim')
  const [disks, setDisks] = useState<InstallerDisk[]>([])
  const [networks, setNetworks] = useState<InstallerNetwork[]>([])
  const [selectedDiskId, setSelectedDiskId] = useState('')
  const [selectedSsid, setSelectedSsid] = useState('')
  const [wifiPassword, setWifiPassword] = useState('')
  const [hostname, setHostname] = useState('lucia')
  const [recoveryPassword, setRecoveryPassword] = useState('')
  const [recoveryPasswordConfirmation, setRecoveryPasswordConfirmation] = useState('')
  const [eraseConfirmation, setEraseConfirmation] = useState('')
  const [installerStatus, setInstallerStatus] = useState<InstallerStatus>({
    phase: 'waiting-for-configuration',
  })
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  const selectedDisk = useMemo(
    () => disks.find((disk) => disk.id === selectedDiskId) ?? null,
    [disks, selectedDiskId],
  )

  useEffect(() => {
    if (bootState !== 'active') return

    try {
      window.sessionStorage.setItem('lucia-installer-boot-seen', 'true')
    } catch (storageError: unknown) {
      if (!(storageError instanceof DOMException)) throw storageError
    }

    const hasReducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches
    const exitTimer = window.setTimeout(
      () => setBootState('exiting'),
      hasReducedMotion ? 100 : 1650,
    )
    return () => window.clearTimeout(exitTimer)
  }, [bootState])

  useEffect(() => {
    if (bootState !== 'exiting') return

    const hasReducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches
    const finishTimer = window.setTimeout(
      () => setBootState('hidden'),
      hasReducedMotion ? 200 : 400,
    )
    return () => window.clearTimeout(finishTimer)
  }, [bootState])

  useEffect(() => {
    if (step !== 'installing') return

    const interval = window.setInterval(() => {
      fetchInstallerStatus()
        .then((status) => {
          setInstallerStatus(status)
          setError('')
        })
        .catch(() => {
          setError('Connection to the installer was lost. Lucia may be powering off; check the appliance before disconnecting power.')
        })
    }, 1000)

    return () => window.clearInterval(interval)
  }, [step])

  async function handleClaim() {
    setBusy(true)
    setError('')
    try {
      await claimInstaller()
      const status = await fetchInstallerStatus()
      setInstallerStatus(status)
      if (status.phase !== 'waiting-for-configuration') {
        setStep('installing')
        return
      }

      const [availableDisks, availableNetworks] = await Promise.all([
        fetchInstallerDisks(),
        fetchInstallerNetworks(),
      ])
      setDisks(availableDisks)
      setNetworks(availableNetworks)
      setStep('storage')
    } catch (claimError: unknown) {
      setError(claimError instanceof Error ? claimError.message : 'Lucia could not verify that code.')
    } finally {
      setBusy(false)
    }
  }

  function handleIdentityContinue() {
    setError('')
    if (selectedSsid && wifiPassword.length < 8) {
      setError('Enter the password for your home Wi-Fi, or choose Ethernet only.')
      return
    }
    if (!/^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$/.test(hostname)) {
      setError('Use lowercase letters, numbers, and hyphens for the Lucia name.')
      return
    }
    if (recoveryPassword.length < 12) {
      setError('Choose a recovery password with at least 12 characters.')
      return
    }
    if (recoveryPassword !== recoveryPasswordConfirmation) {
      setError('The recovery passwords do not match.')
      return
    }
    setStep('review')
  }

  async function handleInstall() {
    if (!selectedDisk || eraseConfirmation !== selectedDisk.confirmationPhrase) return

    setBusy(true)
    setError('')
    try {
      const status = await startInstallation({
        deviceId: selectedDisk.id,
        eraseConfirmation,
        hostname,
        recoveryPassword,
        wifi: selectedSsid
          ? { ssid: selectedSsid, passphrase: wifiPassword }
          : null,
      })
      setInstallerStatus(status)
      setRecoveryPassword('')
      setRecoveryPasswordConfirmation('')
      setWifiPassword('')
      setStep('installing')
    } catch (installError: unknown) {
      setError(installError instanceof Error ? installError.message : 'Installation could not start.')
    } finally {
      setBusy(false)
    }
  }

  if (bootState !== 'hidden') {
    return (
      <BootSequence
        isActive={bootState === 'active' || bootState === 'exiting'}
        isExiting={bootState === 'exiting'}
        onReady={() => {
          if (bootState === 'preparing') setBootState('active')
        }}
      />
    )
  }

  return (
    <main className="installer-shell min-h-screen bg-observatory text-cloud">
      <div className="mx-auto grid w-full max-w-6xl content-start items-start gap-4 px-3 pb-[max(1rem,env(safe-area-inset-bottom))] pt-[max(0.75rem,env(safe-area-inset-top))] sm:px-6 sm:pt-6 lg:min-h-screen lg:grid-cols-[19rem_minmax(0,1fr)] lg:content-center lg:items-center lg:gap-10 lg:px-8 lg:py-16">
        <SignalPath step={step} hostname={hostname} />

        <section className="glass-panel glow-amber-sm min-w-0 rounded-2xl p-4 sm:p-8">
          {step === 'claim' && (
            <ClaimStep
              busy={busy}
              error={error}
              onContinue={handleClaim}
            />
          )}
          {step === 'storage' && (
            <StorageStep
              disks={disks}
              selectedDiskId={selectedDiskId}
              onSelect={setSelectedDiskId}
              onBack={() => setStep('claim')}
              onContinue={() => setStep('identity')}
            />
          )}
          {step === 'identity' && (
            <IdentityStep
              networks={networks}
              selectedSsid={selectedSsid}
              wifiPassword={wifiPassword}
              hostname={hostname}
              recoveryPassword={recoveryPassword}
              recoveryPasswordConfirmation={recoveryPasswordConfirmation}
              error={error}
              onSelectedSsidChange={setSelectedSsid}
              onWifiPasswordChange={setWifiPassword}
              onHostnameChange={setHostname}
              onRecoveryPasswordChange={setRecoveryPassword}
              onRecoveryPasswordConfirmationChange={setRecoveryPasswordConfirmation}
              onBack={() => setStep('storage')}
              onContinue={handleIdentityContinue}
            />
          )}
          {step === 'review' && selectedDisk && (
            <ReviewStep
              disk={selectedDisk}
              hostname={hostname}
              selectedSsid={selectedSsid}
              eraseConfirmation={eraseConfirmation}
              busy={busy}
              error={error}
              onEraseConfirmationChange={setEraseConfirmation}
              onBack={() => setStep('identity')}
              onInstall={handleInstall}
            />
          )}
          {step === 'installing' && (
            <InstallingStep
              status={installerStatus}
              hostname={hostname}
              error={error}
            />
          )}
        </section>
      </div>
    </main>
  )
}

function BootSequence({
  isActive,
  isExiting,
  onReady,
}: {
  isActive: boolean
  isExiting: boolean
  onReady: () => void
}) {
  return (
    <main
      className={`installer-boot fixed inset-0 z-[100] flex min-h-screen items-center justify-center overflow-hidden bg-void ${
        isActive ? 'is-active' : ''
      } ${
        isExiting ? 'is-exiting' : ''
      }`}
    >
      <div className="installer-boot-field" aria-hidden="true" />
      <div className="relative flex flex-col items-center px-6 text-center">
        <div className="installer-boot-mark relative">
          <span className="installer-boot-ring" aria-hidden="true" />
          <span className="installer-boot-ring" aria-hidden="true" />
          <span className="installer-boot-ring" aria-hidden="true" />
          <span className="installer-boot-orbit" aria-hidden="true" />
          <img
            src="/lucia.png"
            alt="Lucia is starting"
            onLoad={onReady}
            className="installer-boot-logo relative z-10 h-36 w-36 sm:h-44 sm:w-44"
          />
          <span className="installer-boot-spark" aria-hidden="true" />
        </div>
        <div className="installer-boot-beam" aria-hidden="true" />
        <p className="installer-boot-wordmark font-display text-3xl font-semibold tracking-tight text-light">
          Lucia
        </p>
        <p className="installer-boot-status mt-2 text-sm text-fog">
          Opening a private path home
        </p>
      </div>
    </main>
  )
}

function SignalPath({
  step,
  hostname,
}: {
  step: InstallerStep
  hostname: string
}) {
  const activeIndex = stepOrder.indexOf(step)
  const nodes = [
    { label: 'Setup', icon: Radio },
    { label: 'Storage', icon: HardDrive },
    { label: 'Home', icon: Wifi },
    { label: 'Review', icon: ShieldCheck },
    { label: hostname ? `${hostname}.local` : 'Lucia', icon: Sparkles },
  ]
  const activeNode = nodes.at(activeIndex)
  if (!activeNode) return null

  return (
    <aside className="relative lg:sticky lg:top-10">
      <div className="mb-4 flex items-center justify-between gap-3 lg:mb-8">
        <div className="flex min-w-0 items-center gap-3">
          <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-amber/12 glow-amber">
            <Sparkles className="h-5 w-5 text-amber" aria-hidden="true" />
          </div>
          <div>
            <p className="font-display text-lg font-semibold text-light">Lucia</p>
            <p className="text-sm text-dust">Appliance setup</p>
          </div>
        </div>
        <ThemeSelector compact className="w-[132px] shrink-0 lg:hidden" />
      </div>

      <div className="mb-1 lg:hidden">
        <div className="mb-2 flex items-center justify-between gap-3 text-sm">
          <span className="font-medium text-light">{activeNode.label}</span>
          <span className="shrink-0 text-dust">Step {activeIndex + 1} of {nodes.length}</span>
        </div>
        <div
          className="h-1.5 overflow-hidden rounded-full bg-stone"
          role="progressbar"
          aria-label="Setup progress"
          aria-valuemin={1}
          aria-valuemax={nodes.length}
          aria-valuenow={activeIndex + 1}
        >
          <div
            className={`h-full rounded-full bg-amber transition-[width] duration-300 ${
              step === 'installing' ? 'installer-progress-live' : ''
            }`}
            style={{ width: `${((activeIndex + 1) / nodes.length) * 100}%` }}
          />
        </div>
      </div>

      <ThemeSelector className="mb-8 hidden w-[170px] lg:grid" />

      <div className="relative hidden gap-5 lg:grid">
        <div
          className={`installer-signal-line ${step === 'installing' ? 'is-installing' : ''}`}
          aria-hidden="true"
        />
        {nodes.map(({ label, icon: Icon }, index) => {
          const isComplete = index < activeIndex
          const isActive = index === activeIndex
          return (
            <div
              key={label}
              className={`installer-signal-node relative z-10 flex min-w-0 flex-col items-center gap-2 lg:flex-row lg:gap-3 ${
                isActive ? 'is-active' : ''
              } ${isComplete ? 'is-complete' : ''}`}
            >
              <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full border border-stone bg-charcoal text-dust transition-all duration-300">
                {isComplete ? (
                  <Check className="h-4 w-4" aria-hidden="true" />
                ) : (
                  <Icon className="h-4 w-4" aria-hidden="true" />
                )}
              </div>
              <span className="max-w-full truncate text-xs font-medium text-dust lg:text-sm">
                {label}
              </span>
            </div>
          )
        })}
      </div>
    </aside>
  )
}

function StepHeading({
  icon: Icon,
  title,
  description,
}: {
  icon: typeof Sparkles
  title: string
  description: string
}) {
  return (
    <header className="mb-5 sm:mb-7">
      <div className="mb-4 hidden h-11 w-11 items-center justify-center rounded-xl bg-amber/10 text-amber sm:flex">
        <Icon className="h-5 w-5" aria-hidden="true" />
      </div>
      <h1 className="text-balance font-display text-2xl font-semibold tracking-tight text-light sm:text-3xl">
        {title}
      </h1>
      <p className="mt-2 max-w-[65ch] text-base leading-6 text-fog">{description}</p>
    </header>
  )
}

function ClaimStep({
  busy,
  error,
  onContinue,
}: {
  busy: boolean
  error: string
  onContinue: () => void
}) {
  return (
    <div className="installer-step">
      <StepHeading
        icon={Sparkles}
        title="Bring Lucia home"
        description="You're connected directly to your new Lucia. This takes about ten minutes, and no setup information leaves this private network."
      />
      <div className="rounded-xl border border-sage/25 bg-sage/8 p-4">
        <p className="flex items-start gap-3 text-sm leading-5 text-fog">
          <ShieldCheck className="mt-0.5 h-5 w-5 shrink-0 text-sage" aria-hidden="true" />
          This browser will own setup until Lucia restarts. Other devices cannot change the installation.
        </p>
      </div>
      <ErrorMessage message={error} />
      <button
        type="button"
        onClick={onContinue}
        disabled={busy}
        className={`mt-6 w-full sm:w-auto ${primaryButton}`}
      >
        {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <ArrowRight className="h-4 w-4" />}
        {busy ? 'Claiming setup...' : 'Begin setup'}
      </button>
    </div>
  )
}

function StorageStep({
  disks,
  selectedDiskId,
  onSelect,
  onBack,
  onContinue,
}: {
  disks: InstallerDisk[]
  selectedDiskId: string
  onSelect: (diskId: string) => void
  onBack: () => void
  onContinue: () => void
}) {
  return (
    <div className="installer-step">
      <StepHeading
        icon={HardDrive}
        title="Choose Lucia's storage"
        description="Lucia runs from an internal drive. Pick the drive you want this appliance to use."
      />
      <div className="space-y-3">
        {disks.length === 0 && (
          <div className="rounded-xl border border-rose/30 bg-rose/8 p-4 text-sm text-rose">
            No compatible storage was found. Check that an NVMe drive is installed, then restart the appliance.
          </div>
        )}
        {disks.map((disk) => {
          const isSelected = selectedDiskId === disk.id
          const isRejected = disk.action === 'reject'
          return (
            <button
              key={disk.id}
              type="button"
              disabled={isRejected}
              onClick={() => onSelect(disk.id)}
              aria-pressed={isSelected}
              className={`min-h-20 w-full rounded-xl border p-4 text-left transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-amber/60 ${
                isSelected
                  ? 'border-amber/60 bg-amber/8'
                  : 'border-stone bg-basalt/70 hover:border-amber/30'
              } disabled:cursor-not-allowed disabled:opacity-45`}
              aria-label={`Use ${disk.model || 'storage drive'}`}
            >
              <span className="flex items-start justify-between gap-4">
                <span className="min-w-0">
                  <span className="block font-display text-base font-semibold text-light">
                    {disk.model || 'Internal storage'}
                  </span>
                  <span className="mt-1 block text-sm text-fog">
                    {formatBytes(disk.sizeBytes)} · {disk.serial || 'No serial reported'}
                  </span>
                  <span className="mt-2 block break-all font-mono text-xs text-dust">
                    {disk.id}
                  </span>
                </span>
                <span className={`mt-1 flex h-6 w-6 shrink-0 items-center justify-center rounded-full border ${
                  isSelected ? 'border-amber bg-amber text-on-accent' : 'border-ash text-transparent'
                }`}>
                  <Check className="h-3.5 w-3.5" aria-hidden="true" />
                </span>
              </span>
            </button>
          )
        })}
      </div>
      <div className="mt-7 flex flex-col-reverse gap-3 sm:flex-row sm:justify-between">
        <button type="button" onClick={onBack} className={secondaryButton}>
          <ArrowLeft className="h-4 w-4" /> Back
        </button>
        <button
          type="button"
          onClick={onContinue}
          disabled={!selectedDiskId}
          className={primaryButton}
        >
          Continue to network <ArrowRight className="h-4 w-4" />
        </button>
      </div>
    </div>
  )
}

function IdentityStep({
  networks,
  selectedSsid,
  wifiPassword,
  hostname,
  recoveryPassword,
  recoveryPasswordConfirmation,
  error,
  onSelectedSsidChange,
  onWifiPasswordChange,
  onHostnameChange,
  onRecoveryPasswordChange,
  onRecoveryPasswordConfirmationChange,
  onBack,
  onContinue,
}: {
  networks: InstallerNetwork[]
  selectedSsid: string
  wifiPassword: string
  hostname: string
  recoveryPassword: string
  recoveryPasswordConfirmation: string
  error: string
  onSelectedSsidChange: (value: string) => void
  onWifiPasswordChange: (value: string) => void
  onHostnameChange: (value: string) => void
  onRecoveryPasswordChange: (value: string) => void
  onRecoveryPasswordConfirmationChange: (value: string) => void
  onBack: () => void
  onContinue: () => void
}) {
  const [showPasswords, setShowPasswords] = useState(false)

  return (
    <div className="installer-step">
      <StepHeading
        icon={Router}
        title="Connect and protect Lucia"
        description="Choose the network Lucia will use after installation, then set its name and local recovery password."
      />
      <div className="mb-5 flex justify-end">
        <button
          type="button"
          aria-pressed={showPasswords}
          onClick={() => setShowPasswords((isVisible) => !isVisible)}
          className="inline-flex min-h-11 items-center gap-2 rounded-lg px-3 text-sm font-medium text-fog transition-colors hover:bg-stone/40 hover:text-light focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-amber/60"
        >
          {showPasswords
            ? <EyeOff className="h-4 w-4" aria-hidden="true" />
            : <Eye className="h-4 w-4" aria-hidden="true" />}
          {showPasswords ? 'Hide passwords' : 'Show passwords'}
        </button>
      </div>
      <div className="grid gap-5 sm:grid-cols-2">
        <div className="sm:col-span-2">
          <label htmlFor="home-wifi" className="mb-2 block text-sm font-medium text-light">
            Home Wi-Fi
          </label>
          <select
            id="home-wifi"
            value={selectedSsid}
            onChange={(event) => onSelectedSsidChange(event.target.value)}
            className={inputStyle}
          >
            <option value="">Use Ethernet only</option>
            {networks.map((network) => (
              <option key={network.ssid} value={network.ssid}>
                {network.ssid} · {network.signal}% · {network.security || 'Open'}
              </option>
            ))}
          </select>
        </div>
        {selectedSsid && (
          <div className="sm:col-span-2">
            <label htmlFor="wifi-password" className="mb-2 block text-sm font-medium text-light">
              Wi-Fi password
            </label>
            <input
              id="wifi-password"
              type={showPasswords ? 'text' : 'password'}
              autoComplete="new-password"
              value={wifiPassword}
              onChange={(event) => onWifiPasswordChange(event.target.value)}
              className={inputStyle}
            />
          </div>
        )}
        <div className="sm:col-span-2">
          <label htmlFor="lucia-name" className="mb-2 block text-sm font-medium text-light">
            Lucia name
          </label>
          <div className="flex items-center">
            <input
              id="lucia-name"
              value={hostname}
              onChange={(event) => onHostnameChange(event.target.value.toLowerCase())}
              className={`${inputStyle} rounded-r-none`}
            />
            <span className="flex min-h-12 items-center rounded-r-xl border border-l-0 border-stone bg-charcoal px-3 text-sm text-dust">
              .local
            </span>
          </div>
        </div>
        <div>
          <label htmlFor="recovery-password" className="mb-2 block text-sm font-medium text-light">
            Recovery password
          </label>
          <input
            id="recovery-password"
            type={showPasswords ? 'text' : 'password'}
            autoComplete="new-password"
            value={recoveryPassword}
            onChange={(event) => onRecoveryPasswordChange(event.target.value)}
            className={inputStyle}
          />
        </div>
        <div>
          <label htmlFor="confirm-recovery-password" className="mb-2 block text-sm font-medium text-light">
            Confirm recovery password
          </label>
          <input
            id="confirm-recovery-password"
            type={showPasswords ? 'text' : 'password'}
            autoComplete="new-password"
            value={recoveryPasswordConfirmation}
            onChange={(event) => onRecoveryPasswordConfirmationChange(event.target.value)}
            className={inputStyle}
          />
        </div>
      </div>
      <p className="mt-3 flex gap-2 text-sm leading-5 text-dust">
        <LockKeyhole className="mt-0.5 h-4 w-4 shrink-0 text-amber" aria-hidden="true" />
        This password unlocks local maintenance through SSH and the console. Lucia stores only a salted hash.
      </p>
      <ErrorMessage message={error} />
      <div className="mt-7 flex flex-col-reverse gap-3 sm:flex-row sm:justify-between">
        <button type="button" onClick={onBack} className={secondaryButton}>
          <ArrowLeft className="h-4 w-4" /> Back
        </button>
        <button type="button" onClick={onContinue} className={primaryButton}>
          Review installation <ArrowRight className="h-4 w-4" />
        </button>
      </div>
    </div>
  )
}

function ReviewStep({
  disk,
  hostname,
  selectedSsid,
  eraseConfirmation,
  busy,
  error,
  onEraseConfirmationChange,
  onBack,
  onInstall,
}: {
  disk: InstallerDisk
  hostname: string
  selectedSsid: string
  eraseConfirmation: string
  busy: boolean
  error: string
  onEraseConfirmationChange: (value: string) => void
  onBack: () => void
  onInstall: () => void
}) {
  return (
    <div className="installer-step">
      <StepHeading
        icon={ShieldCheck}
        title="One careful look"
        description="Lucia has everything it needs. Check the destination before making the only irreversible change."
      />
      <dl className="grid gap-x-6 gap-y-4 border-y border-stone py-5 sm:grid-cols-2">
        <div>
          <dt className="text-xs font-medium uppercase tracking-wider text-dust">Appliance address</dt>
          <dd className="mt-1 text-sm font-medium text-light">{hostname}.local</dd>
        </div>
        <div>
          <dt className="text-xs font-medium uppercase tracking-wider text-dust">Network</dt>
          <dd className="mt-1 text-sm font-medium text-light">{selectedSsid || 'Ethernet'}</dd>
        </div>
        <div className="sm:col-span-2">
          <dt className="text-xs font-medium uppercase tracking-wider text-dust">Installation drive</dt>
          <dd className="mt-1 text-sm font-medium text-light">
            {disk.model || 'Internal storage'} · {formatBytes(disk.sizeBytes)}
          </dd>
        </div>
      </dl>

      <div className="mt-5 rounded-xl border border-rose/35 bg-rose/8 p-4">
        <p className="font-display text-base font-semibold text-rose">
          Everything on {disk.model || 'this drive'} will be erased
        </p>
        <p className="mt-1 text-sm leading-5 text-fog">
          This cannot be undone. Other connected drives will not be changed.
        </p>
      </div>

      <label htmlFor="erase-confirmation" className="mb-2 mt-5 block text-sm font-medium text-light">
        Type the erase phrase to confirm
      </label>
      <code className="mb-2 block break-all rounded-lg bg-void px-3 py-2 text-xs text-amber">
        {disk.confirmationPhrase}
      </code>
      <input
        id="erase-confirmation"
        value={eraseConfirmation}
        onChange={(event) => onEraseConfirmationChange(event.target.value)}
        spellCheck={false}
        className={inputStyle}
      />
      <ErrorMessage message={error} />
      <div className="mt-7 flex flex-col-reverse gap-3 sm:flex-row sm:justify-between">
        <button type="button" onClick={onBack} className={secondaryButton}>
          <ArrowLeft className="h-4 w-4" /> Back
        </button>
        <button
          type="button"
          onClick={onInstall}
          disabled={busy || eraseConfirmation !== disk.confirmationPhrase}
          className="inline-flex min-h-12 items-center justify-center gap-2 rounded-xl bg-rose px-5 py-3 text-sm font-semibold text-white transition-colors hover:bg-rose-dim focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-rose/60 disabled:cursor-not-allowed disabled:opacity-40"
        >
          {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <HardDrive className="h-4 w-4" />}
          {busy ? 'Authorizing...' : 'Erase drive and install Lucia'}
        </button>
      </div>
    </div>
  )
}

function InstallingStep({
  status,
  hostname,
  error,
}: {
  status: InstallerStatus
  hostname: string
  error: string
}) {
  const isInstalled = status.phase === 'installed'
  const stages = [
    { key: 'validating', label: 'Verify installation image' },
    { key: 'writing', label: 'Write Lucia to NVMe' },
    { key: 'provisioning', label: 'Configure both system slots' },
    { key: 'syncing', label: 'Secure and sync storage' },
    { key: 'powering-off', label: 'Power off for SD removal' },
  ] as const
  const currentStage = status.stage === 'image-written'
    ? 'provisioning'
    : status.stage
  const currentIndex = currentStage
    ? stages.findIndex((stage) => stage.key === currentStage)
    : 0
  const bytesWritten = status.bytesWritten ?? 0
  const totalBytes = status.totalBytes ?? 0
  const hasWriteProgress = status.stage === 'writing'
    && totalBytes > 0
  const writePercentage = hasWriteProgress
    ? Math.min(100, (bytesWritten / totalBytes) * 100)
    : 0

  return (
    <div className="installer-step">
      <div className="installer-orbit mx-auto mb-7 flex h-24 w-24 items-center justify-center rounded-full border border-amber/25 bg-amber/8">
        {isInstalled
          ? <CheckCircle2 className="h-10 w-10 text-sage" />
          : <Cpu className="h-10 w-10 text-amber" />}
      </div>
      <h1 className="text-balance text-center font-display text-3xl font-semibold tracking-tight text-light">
        {isInstalled ? 'Lucia is ready to wake up' : 'Lucia is moving in'}
      </h1>
      <p className="mx-auto mt-3 max-w-lg text-center text-base leading-6 text-fog">
        {isInstalled
          ? `The installer has handed off to ${hostname}.local. Remove the SD card when the appliance powers down, then turn it back on.`
          : 'The image is being verified, written, and personalized. Keep the appliance powered on. This page may disconnect during the restart.'}
      </p>
      {hasWriteProgress && (
        <div className="mx-auto mt-7 max-w-md">
          <div className="mb-2 flex items-center justify-between gap-3 text-sm">
            <span className="font-medium text-light">
              {formatBytes(bytesWritten)} of {formatBytes(totalBytes)}
            </span>
            <span className="tabular-nums text-amber">
              {writePercentage.toFixed(1)}%
            </span>
          </div>
          <div
            className="h-2 overflow-hidden rounded-full bg-stone"
            role="progressbar"
            aria-label="NVMe image write progress"
            aria-valuemin={0}
            aria-valuemax={100}
            aria-valuenow={Math.round(writePercentage)}
          >
            <div
              className="h-full rounded-full bg-amber transition-[width] duration-500"
              style={{ width: `${writePercentage}%` }}
            />
          </div>
        </div>
      )}

      <ol className="mx-auto mt-7 max-w-md space-y-1" aria-label="Installation progress">
        {stages.map((stage, index) => {
          const isComplete = isInstalled || index < currentIndex
          const isActive = !isInstalled && index === currentIndex
          return (
            <li
              key={stage.key}
              className={`flex min-h-11 items-center gap-3 rounded-lg px-3 py-2 text-sm ${
                isActive ? 'bg-amber/8 text-light' : 'text-fog'
              }`}
            >
              <span className={`flex h-6 w-6 shrink-0 items-center justify-center rounded-full border ${
                isComplete
                  ? 'border-sage/50 bg-sage/10 text-sage'
                  : isActive
                    ? 'border-amber/60 bg-amber/10 text-amber'
                    : 'border-stone text-dust'
              }`}>
                {isComplete
                  ? <Check className="h-3.5 w-3.5" aria-hidden="true" />
                  : isActive
                    ? <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />
                    : <span className="h-1.5 w-1.5 rounded-full bg-current" />}
              </span>
              <span className={isActive ? 'font-medium' : ''}>{stage.label}</span>
            </li>
          )
        })}
      </ol>

      {error && <ErrorMessage message={error} />}
      {!isInstalled && !error && (
        <p className="mt-5 flex items-center justify-center gap-2 text-xs text-dust">
          <RefreshCw className="h-3.5 w-3.5" aria-hidden="true" />
          Status updates automatically
        </p>
      )}
    </div>
  )
}

function ErrorMessage({ message }: { message: string }) {
  if (!message) return null
  return (
    <p role="alert" className="mt-4 rounded-lg border border-rose/30 bg-rose/8 px-3 py-2 text-sm text-rose">
      {message}
    </p>
  )
}

function formatBytes(bytes: number): string {
  return new Intl.NumberFormat(undefined, {
    style: 'unit',
    unit: 'gigabyte',
    unitDisplay: 'short',
    maximumFractionDigits: 0,
  }).format(bytes / 1_000_000_000)
}
