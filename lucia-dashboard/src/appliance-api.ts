export interface ApplianceServiceStatus {
  id: string
  activeState: string
  unitFileState: string
}

export interface ApplianceStatus {
  hostname: string
  architecture: string
  board: string
  luciaVersion: string
  storageBytes: number
  rebootRequired: boolean
  network: {
    ssid: string
    signal: number | null
  }
  os: {
    name: string
    versionId: string
    imageVersion: string
    jetsonLinuxVersion: string
  }
  services: ApplianceServiceStatus[]
}

export interface ApplianceTelemetryStatus {
  configured: boolean
  enabled: boolean
  endpoint: string
  insecureSkipVerify: boolean
  hasAuthorization: boolean
}

export interface ApplianceTelemetryConfiguration {
  enabled: boolean
  endpoint: string
  username: string | null
  password: string | null
  clearAuthorization: boolean
  insecureSkipVerify: boolean
}

export interface ApplianceUpdateStatus {
  currentLuciaVersion: string
  currentOsVersion: string
  latestLuciaVersion: string | null
  latestOsVersion: string | null
  manifestAvailable: boolean
  compatible: boolean
  luciaNewerDiscovered: boolean
  osNewerDiscovered: boolean
  luciaUpdateAvailable: boolean
  osUpdateAvailable: boolean
  releaseUrl: string | null
  message: string | null
}

async function request(path: string, init?: RequestInit): Promise<unknown> {
  const response = await fetch(`/api/appliance${path}`, init)
  if (!response.ok) {
    const problem = await response.json().catch(() => null)
    const detail = isRecord(problem) && typeof problem.detail === 'string'
      ? problem.detail
      : `Appliance request failed with status ${response.status}.`
    throw new Error(detail)
  }
  if (response.status === 202 || response.status === 204) return null
  return response.json()
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}

function requireRecord(value: unknown, name: string): Record<string, unknown> {
  if (!isRecord(value)) throw new Error(`The appliance returned invalid ${name}.`)
  return value
}

function requireString(
  value: Record<string, unknown>,
  key: string,
): string {
  if (typeof value[key] !== 'string') {
    throw new Error(`The appliance response is missing ${key}.`)
  }
  return value[key]
}

function requireBoolean(
  value: Record<string, unknown>,
  key: string,
): boolean {
  if (typeof value[key] !== 'boolean') {
    throw new Error(`The appliance response is missing ${key}.`)
  }
  return value[key]
}

function parseStatus(value: unknown): ApplianceStatus {
  const status = requireRecord(value, 'status')
  const os = requireRecord(status.os, 'OS status')
  const network = requireRecord(status.network, 'network status')
  if (
    !Array.isArray(status.services)
    || typeof status.rebootRequired !== 'boolean'
    || typeof status.storageBytes !== 'number'
  ) {
    throw new Error('The appliance returned invalid service status.')
  }
  if (network.signal !== null && typeof network.signal !== 'number') {
    throw new Error('The appliance returned invalid Wi-Fi signal strength.')
  }
  const services = status.services.map((item) => {
    const service = requireRecord(item, 'service status')
    return {
      id: requireString(service, 'id'),
      activeState: requireString(service, 'activeState'),
      unitFileState: requireString(service, 'unitFileState'),
    }
  })
  return {
    hostname: requireString(status, 'hostname'),
    architecture: requireString(status, 'architecture'),
    board: requireString(status, 'board'),
    luciaVersion: requireString(status, 'luciaVersion'),
    storageBytes: status.storageBytes,
    rebootRequired: status.rebootRequired,
    network: {
      ssid: requireString(network, 'ssid'),
      signal: network.signal,
    },
    os: {
      name: requireString(os, 'name'),
      versionId: requireString(os, 'versionId'),
      imageVersion: requireString(os, 'imageVersion'),
      jetsonLinuxVersion: requireString(os, 'jetsonLinuxVersion'),
    },
    services,
  }
}

function parseTelemetry(value: unknown): ApplianceTelemetryStatus {
  const telemetry = requireRecord(value, 'telemetry status')
  if (
    typeof telemetry.configured !== 'boolean'
    || typeof telemetry.enabled !== 'boolean'
    || typeof telemetry.insecureSkipVerify !== 'boolean'
    || typeof telemetry.hasAuthorization !== 'boolean'
  ) {
    throw new Error('The appliance returned invalid telemetry state.')
  }
  return {
    configured: telemetry.configured,
    enabled: telemetry.enabled,
    endpoint: requireString(telemetry, 'endpoint'),
    insecureSkipVerify: telemetry.insecureSkipVerify,
    hasAuthorization: telemetry.hasAuthorization,
  }
}

function parseUpdates(value: unknown): ApplianceUpdateStatus {
  const updates = requireRecord(value, 'update status')
  const optionalString = (key: string): string | null => {
    const candidate = updates[key]
    if (candidate === null || typeof candidate === 'string') return candidate
    throw new Error(`The appliance returned invalid ${key}.`)
  }
  return {
    currentLuciaVersion: requireString(updates, 'currentLuciaVersion'),
    currentOsVersion: requireString(updates, 'currentOsVersion'),
    latestLuciaVersion: optionalString('latestLuciaVersion'),
    latestOsVersion: optionalString('latestOsVersion'),
    manifestAvailable: requireBoolean(updates, 'manifestAvailable'),
    compatible: requireBoolean(updates, 'compatible'),
    luciaNewerDiscovered: requireBoolean(updates, 'luciaNewerDiscovered'),
    osNewerDiscovered: requireBoolean(updates, 'osNewerDiscovered'),
    luciaUpdateAvailable: requireBoolean(updates, 'luciaUpdateAvailable'),
    osUpdateAvailable: requireBoolean(updates, 'osUpdateAvailable'),
    releaseUrl: optionalString('releaseUrl'),
    message: optionalString('message'),
  }
}

export async function fetchApplianceStatus(): Promise<ApplianceStatus> {
  return parseStatus(await request('/status'))
}

export async function restartApplianceService(service: string): Promise<void> {
  if (service === 'agenthost') {
    const response = await fetch('/api/system/restart', { method: 'POST' })
    if (!response.ok) {
      throw new Error(`AgentHost restart failed with status ${response.status}.`)
    }
    return
  }
  await request(`/services/${encodeURIComponent(service)}/restart`, {
    method: 'POST',
  })
}

export async function rebootAppliance(): Promise<void> {
  await request('/host/reboot', { method: 'POST' })
}

export async function fetchApplianceTelemetry(): Promise<ApplianceTelemetryStatus> {
  return parseTelemetry(await request('/telemetry'))
}

export async function updateApplianceTelemetry(
  configuration: ApplianceTelemetryConfiguration,
): Promise<ApplianceTelemetryStatus> {
  return parseTelemetry(await request('/telemetry', {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(configuration),
  }))
}

export async function checkApplianceUpdates(): Promise<ApplianceUpdateStatus> {
  return parseUpdates(await request('/updates'))
}
