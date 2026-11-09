/** Administrator API for family members, their permissions, model controls, devices, and usage. */

const BASE = '/api'

export type FamilyRole = 'Administrator' | 'Adult' | 'Child' | 'Guest'
export type ActionRule = 'Allow' | 'AskGuardian' | 'Deny'
export type CapabilityKind = 'Agent' | 'Tool'
export type MemberIdentityKind = 'ApiKey' | 'SpeakerProfile' | 'Oidc'

export interface CapabilityRule {
  kind: CapabilityKind
  capabilityId: string
  rule: ActionRule
}

export interface QuietHoursWindow {
  start: string
  end: string
  timeZoneId?: string | null
}

export interface MemberGuardrails {
  blockPromptInjection: boolean
  redactPersonalInfo: boolean
}

export interface MemberLlmControls {
  allowedProviderIds: string[]
  preferredProviderId?: string | null
  dailyBudgetUsd?: number | null
  monthlyBudgetUsd?: number | null
  systemPrompt?: string | null
  blockedTopics: string[]
  quietHours?: QuietHoursWindow | null
  guardrails: MemberGuardrails
}

export interface FamilyMember {
  id: string
  displayName: string
  role: FamilyRole
  guardianIds: string[]
  birthYear?: number | null
  isActive: boolean
  accessExpiresAt?: string | null
  capabilityRules: CapabilityRule[]
  llmControls: MemberLlmControls
  useAssistantHarness: boolean
  createdAt: string
  updatedAt: string
}

export interface FamilyMemberRequest {
  displayName: string
  role: FamilyRole
  guardianIds: string[]
  birthYear?: number | null
  isActive?: boolean
  accessExpiresAt?: string | null
  useAssistantHarness?: boolean
}

export interface MemberIdentityLink {
  memberId: string
  kind: MemberIdentityKind
  issuer: string
  subject: string
  label?: string | null
  createdAt: string
}

export interface DevicePairing {
  id: string
  memberId: string
  deviceName?: string | null
  createdAt: string
  expiresAt: string
  redeemedAt?: string | null
  apiKeyId?: string | null
}

export interface PairingCode {
  id: string
  memberId: string
  code: string
  expiresAt: string
  serverUrl: string
  pairingUri: string
}

export interface UsageTotals {
  calls: number
  inputTokens: number
  outputTokens: number
  costUsd: number
}

export interface UsageRecord {
  id: string
  timestamp: string
  providerId: string
  model?: string | null
  agentId?: string | null
  inputTokens: number
  outputTokens: number
  costUsd: number
}

export interface MemberUsage {
  today: UsageTotals
  month: UsageTotals
  recent: UsageRecord[]
}

export interface OidcSettings {
  enabled: boolean
  authority?: string | null
  audience?: string | null
  requireHttpsMetadata: boolean
  active: boolean
  restartRequired: boolean
}

async function request<T>(path: string, fallback: string, init?: RequestInit): Promise<T> {
  const res = await fetch(`${BASE}${path}`, {
    credentials: 'include',
    cache: 'no-store',
    ...init,
    headers: init?.body ? { 'Content-Type': 'application/json', ...init.headers } : init?.headers,
  })
  if (!res.ok) {
    let message = fallback
    try {
      const body = await res.json()
      if (typeof body?.error === 'string') message = body.error
      else if (typeof body?.detail === 'string') message = body.detail
    } catch {
      /* keep fallback */
    }
    throw new Error(message)
  }
  return res.status === 204 ? (undefined as T) : res.json()
}

const json = (method: string, body: unknown): RequestInit => ({ method, body: JSON.stringify(body) })
const member = (id: string) => `/family/members/${encodeURIComponent(id)}`

export const listMembers = (signal?: AbortSignal) =>
  request<FamilyMember[]>('/family/members', 'Could not load family members.', { signal })

export const createMember = (body: FamilyMemberRequest) =>
  request<FamilyMember>('/family/members', 'Could not add this member.', json('POST', body))

export const updateMember = (id: string, body: FamilyMemberRequest) =>
  request<FamilyMember>(member(id), 'Could not save this member.', json('PUT', body))

export const deleteMember = (id: string) =>
  request<void>(member(id), 'Could not remove this member.', { method: 'DELETE' })

export const savePermissions = (id: string, rules: CapabilityRule[]) =>
  request<CapabilityRule[]>(`${member(id)}/permissions`, 'Could not save permissions.', json('PUT', rules))

export const saveLlmControls = (id: string, controls: MemberLlmControls) =>
  request<MemberLlmControls>(`${member(id)}/llm-controls`, 'Could not save model and safety settings.', json('PUT', controls))

export const listIdentities = (id: string, signal?: AbortSignal) =>
  request<MemberIdentityLink[]>(`${member(id)}/identities`, 'Could not load sign-in methods.', { signal })

export const linkIdentity = (id: string, body: { kind: MemberIdentityKind; issuer?: string; subject: string; label?: string }) =>
  request<MemberIdentityLink>(`${member(id)}/identities`, 'Could not link this sign-in method.', json('POST', body))

export const unlinkIdentity = (id: string, link: MemberIdentityLink) => {
  const query = new URLSearchParams({ kind: link.kind, subject: link.subject })
  if (link.issuer) query.set('issuer', link.issuer)
  return request<void>(`${member(id)}/identities?${query}`, 'Could not remove this sign-in method.', { method: 'DELETE' })
}

export const listPairings = (id: string, signal?: AbortSignal) =>
  request<DevicePairing[]>(`${member(id)}/pairings`, 'Could not load device pairings.', { signal })

export const createPairing = (id: string, lifetimeMinutes?: number) =>
  request<PairingCode>(`${member(id)}/pairings`, 'Could not create a pairing code.', json('POST', { lifetimeMinutes }))

export const cancelPairing = (pairingId: string) =>
  request<void>(`/family/pairings/${encodeURIComponent(pairingId)}`, 'Could not cancel this pairing code.', { method: 'DELETE' })

export const getUsage = (id: string, signal?: AbortSignal) =>
  request<MemberUsage>(`${member(id)}/usage?limit=25`, 'Could not load usage.', { signal })

export const getOidcSettings = (signal?: AbortSignal) =>
  request<OidcSettings>('/settings/oidc', 'Could not load app sign-in settings.', { signal })

export const saveOidcSettings = (body: { enabled: boolean; authority?: string; audience?: string; requireHttpsMetadata: boolean }) =>
  request<OidcSettings>('/settings/oidc', 'Could not save app sign-in settings.', json('PUT', body))

export const ROLE_LABELS: Record<FamilyRole, string> = {
  Administrator: 'Administrator',
  Adult: 'Adult',
  Child: 'Child',
  Guest: 'Guest',
}

export const ROLE_DEFAULTS: Record<FamilyRole, string> = {
  Administrator: 'Can use every agent and tool.',
  Adult: 'Can use every agent and tool.',
  Child: 'Can read home state. Changes ask a guardian first. Locks, alarms, and the security agent are blocked.',
  Guest: 'Can read home state. Changes and the security agent are blocked.',
}

export function toRequest(m: FamilyMember): FamilyMemberRequest {
  return {
    displayName: m.displayName,
    role: m.role,
    guardianIds: m.guardianIds,
    birthYear: m.birthYear ?? null,
    isActive: m.isActive,
    accessExpiresAt: m.accessExpiresAt ?? null,
    useAssistantHarness: m.useAssistantHarness,
  }
}
