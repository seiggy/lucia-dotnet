export type InstallerPhase =
  | 'waiting-for-configuration'
  | 'authorized'
  | 'installing'
  | 'installed'

export type InstallerStage =
  | 'validating'
  | 'writing'
  | 'image-written'
  | 'provisioning'
  | 'syncing'
  | 'powering-off'

export interface InstallerStatus {
  phase: InstallerPhase
  stage?: InstallerStage
  bytesWritten?: number
  totalBytes?: number
}

export interface InstallerDisk {
  id: string
  model: string
  serial: string
  confirmationPhrase: string
  sizeBytes: number
  classification: 'blank' | 'occupied' | 'protected' | 'too-small'
  action: 'install' | 'confirmation-required' | 'reject'
}

export interface InstallerNetwork {
  ssid: string
  signal: number
  security: string
}

export interface InstallerConfiguration {
  deviceId: string
  eraseConfirmation: string
  hostname: string
  recoveryPassword: string
  wifi: {
    ssid: string
    passphrase: string
  } | null
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}

function isInstallerPhase(value: unknown): value is InstallerPhase {
  return value === 'waiting-for-configuration'
    || value === 'authorized'
    || value === 'installing'
    || value === 'installed'
}

function isInstallerStage(value: unknown): value is InstallerStage {
  return value === 'validating'
    || value === 'writing'
    || value === 'image-written'
    || value === 'provisioning'
    || value === 'syncing'
    || value === 'powering-off'
}

function parseStatus(value: unknown): InstallerStatus {
  if (!isRecord(value) || !isInstallerPhase(value.phase)) {
    throw new Error('The installer returned an invalid status.')
  }
  if (value.stage !== undefined && !isInstallerStage(value.stage)) {
    throw new Error('The installer returned an invalid progress stage.')
  }
  if (value.bytesWritten !== undefined && typeof value.bytesWritten !== 'number') {
    throw new Error('The installer returned an invalid byte count.')
  }
  if (value.totalBytes !== undefined && typeof value.totalBytes !== 'number') {
    throw new Error('The installer returned an invalid image size.')
  }
  return {
    phase: value.phase,
    stage: value.stage,
    bytesWritten: value.bytesWritten,
    totalBytes: value.totalBytes,
  }
}

function parseDisk(value: unknown): InstallerDisk {
  if (
    !isRecord(value)
    || typeof value.id !== 'string'
    || typeof value.model !== 'string'
    || typeof value.serial !== 'string'
    || typeof value.confirmationPhrase !== 'string'
    || typeof value.sizeBytes !== 'number'
    || !['blank', 'occupied', 'protected', 'too-small'].includes(
      typeof value.classification === 'string' ? value.classification : '',
    )
    || !['install', 'confirmation-required', 'reject'].includes(
      typeof value.action === 'string' ? value.action : '',
    )
  ) {
    throw new Error('The installer returned an invalid storage device.')
  }

  const classification = value.classification
  const action = value.action
  if (
    classification !== 'blank'
    && classification !== 'occupied'
    && classification !== 'protected'
    && classification !== 'too-small'
  ) {
    throw new Error('The installer returned an unknown storage state.')
  }
  if (
    action !== 'install'
    && action !== 'confirmation-required'
    && action !== 'reject'
  ) {
    throw new Error('The installer returned an unknown storage action.')
  }

  return {
    id: value.id,
    model: value.model,
    serial: value.serial,
    confirmationPhrase: value.confirmationPhrase,
    sizeBytes: value.sizeBytes,
    classification,
    action,
  }
}

function parseNetwork(value: unknown): InstallerNetwork {
  if (
    !isRecord(value)
    || typeof value.ssid !== 'string'
    || typeof value.signal !== 'number'
    || typeof value.security !== 'string'
  ) {
    throw new Error('The installer returned an invalid Wi-Fi network.')
  }
  return {
    ssid: value.ssid,
    signal: value.signal,
    security: value.security,
  }
}

async function readJson(response: Response): Promise<unknown> {
  if (!response.ok) {
    if (response.status === 401) {
      throw new Error('That setup code did not match this Lucia.')
    }
    throw new Error(`Installer request failed with status ${response.status}.`)
  }
  return response.json()
}

export async function isInstallerMode(): Promise<boolean> {
  const response = await fetch('/api/installer/capabilities')
  if (response.status === 404) return false
  const value = await readJson(response)
  return isRecord(value) && value.mode === 'installer'
}

export async function claimInstaller(): Promise<void> {
  const response = await fetch('/api/installer/claim', { method: 'POST' })
  await readJson(response)
}

export async function fetchInstallerStatus(): Promise<InstallerStatus> {
  const response = await fetch('/api/installer/status')
  return parseStatus(await readJson(response))
}

export async function fetchInstallerDisks(): Promise<InstallerDisk[]> {
  const response = await fetch('/api/installer/disks')
  const value = await readJson(response)
  if (!Array.isArray(value)) {
    throw new Error('The installer returned an invalid storage list.')
  }
  return value.map(parseDisk)
}

export async function fetchInstallerNetworks(): Promise<InstallerNetwork[]> {
  const response = await fetch('/api/installer/networks')
  const value = await readJson(response)
  if (!Array.isArray(value)) {
    throw new Error('The installer returned an invalid Wi-Fi list.')
  }
  return value.map(parseNetwork)
}

export async function startInstallation(
  configuration: InstallerConfiguration,
): Promise<InstallerStatus> {
  const response = await fetch('/api/installer/install', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(configuration),
  })
  return parseStatus(await readJson(response))
}
