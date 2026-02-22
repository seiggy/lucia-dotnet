import type {
  ConversationTrace,
  PagedResult,
  TraceStats,
  DatasetExportRecord,
  ExportFilterCriteria,
  LabelStatus,
  ActiveTaskSummary,
  ArchivedTask,
  CombinedTaskStats,
  McpToolServerDefinition,
  McpToolInfo,
  McpServerStatus,
  AgentDefinition,
} from './types';

const BASE = '/api';

export async function fetchTraces(
  params: Record<string, string>,
): Promise<PagedResult<ConversationTrace>> {
  const qs = new URLSearchParams(params).toString();
  const res = await fetch(`${BASE}/traces?${qs}`);
  if (!res.ok) throw new Error(`Failed to fetch traces: ${res.statusText}`);
  return res.json();
}

export async function fetchTrace(id: string): Promise<ConversationTrace> {
  const res = await fetch(`${BASE}/traces/${id}`);
  if (!res.ok) throw new Error(`Trace not found`);
  return res.json();
}

export interface RelatedTraceSummary {
  id: string;
  timestamp: string;
  traceType: string;
  agentId: string | null;
  userInput: string;
  isErrored: boolean;
  totalDurationMs: number;
}

export async function fetchRelatedTraces(id: string): Promise<RelatedTraceSummary[]> {
  const res = await fetch(`${BASE}/traces/${id}/related`);
  if (!res.ok) throw new Error(`Failed to fetch related traces`);
  return res.json();
}

export async function fetchStats(): Promise<TraceStats> {
  const res = await fetch(`${BASE}/traces/stats`);
  if (!res.ok) throw new Error(`Failed to fetch stats`);
  return res.json();
}

export async function updateLabel(
  id: string,
  body: {
    status: LabelStatus;
    reviewerNotes?: string;
    correctionText?: string;
  },
): Promise<void> {
  const res = await fetch(`${BASE}/traces/${id}/label`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
  if (!res.ok) throw new Error(`Failed to update label`);
}

export async function deleteTrace(id: string): Promise<void> {
  const res = await fetch(`${BASE}/traces/${id}`, { method: 'DELETE' });
  if (!res.ok) throw new Error(`Failed to delete trace`);
}

export async function createExport(
  filter: ExportFilterCriteria,
): Promise<DatasetExportRecord> {
  const res = await fetch(`${BASE}/exports`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(filter),
  });
  if (!res.ok) throw new Error(`Export failed: ${await res.text()}`);
  return res.json();
}

export async function fetchExports(): Promise<DatasetExportRecord[]> {
  const res = await fetch(`${BASE}/exports`);
  if (!res.ok) throw new Error(`Failed to fetch exports`);
  return res.json();
}

export function getExportDownloadUrl(id: string): string {
  return `${BASE}/exports/${id}/download`;
}

// ── Configuration API ──────────────────────────────────────────

export interface ConfigSectionSummary {
  section: string;
  keyCount: number;
  lastUpdated: string;
}

export interface ConfigEntryDto {
  key: string;
  value: string | null;
  isSensitive: boolean;
  updatedAt: string;
  updatedBy: string;
}

export interface ConfigPropertySchema {
  name: string;
  type: string;
  description: string;
  defaultValue: string;
  isSensitive: boolean;
}

export interface ConfigSectionSchema {
  section: string;
  description: string;
  properties: ConfigPropertySchema[];
  isArray: boolean;
}

export async function fetchConfigSections(): Promise<ConfigSectionSummary[]> {
  const res = await fetch(`${BASE}/config/sections`);
  if (!res.ok) throw new Error(`Failed to fetch config sections: ${res.statusText}`);
  return res.json();
}

export async function fetchConfigSection(section: string, showSecrets = false): Promise<ConfigEntryDto[]> {
  const qs = showSecrets ? '?showSecrets=true' : '';
  const res = await fetch(`${BASE}/config/sections/${encodeURIComponent(section)}${qs}`);
  if (res.status === 404) return [];
  if (!res.ok) throw new Error(`Failed to fetch section: ${res.statusText}`);
  return res.json();
}

export async function updateConfigSection(
  section: string,
  values: Record<string, string | null>,
): Promise<number> {
  const res = await fetch(`${BASE}/config/sections/${encodeURIComponent(section)}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(values),
  });
  if (!res.ok) throw new Error(`Failed to update section: ${res.statusText}`);
  return res.json();
}

export async function resetConfig(): Promise<string> {
  const res = await fetch(`${BASE}/config/reset`, { method: 'POST' });
  if (!res.ok) throw new Error(`Failed to reset config: ${res.statusText}`);
  return res.json();
}

export async function fetchConfigSchema(): Promise<ConfigSectionSchema[]> {
  const res = await fetch(`${BASE}/config/schema`);
  if (!res.ok) throw new Error(`Failed to fetch schema: ${res.statusText}`);
  return res.json();
}

export interface MusicAssistantTestResult {
  success: boolean;
  message: string;
}

export async function testMusicAssistantIntegration(integrationId: string): Promise<MusicAssistantTestResult> {
  const res = await fetch(`${BASE}/config/test/music-assistant`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ integrationId }),
  });
  if (!res.ok) throw new Error(`Test failed: ${res.statusText}`);
  return res.json();
}

// ── Auth API ─────────────────────────────────────────────────────

export interface AuthStatus {
  authenticated: boolean;
  setupComplete: boolean;
  hasKeys: boolean;
}

export interface LoginResponse {
  authenticated: boolean;
  keyName: string;
  keyPrefix: string;
}

export async function fetchAuthStatus(): Promise<AuthStatus> {
  const res = await fetch(`${BASE}/auth/status`);
  if (!res.ok) throw new Error(`Failed to fetch auth status: ${res.statusText}`);
  return res.json();
}

export async function login(apiKey: string): Promise<LoginResponse> {
  const res = await fetch(`${BASE}/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ apiKey }),
  });
  if (res.status === 401) throw new Error('Invalid API key');
  if (!res.ok) throw new Error(`Login failed: ${res.statusText}`);
  return res.json();
}

export async function logout(): Promise<void> {
  await fetch(`${BASE}/auth/logout`, { method: 'POST' });
}

// ── Setup Wizard API ─────────────────────────────────────────────

export interface SetupStatus {
  hasDashboardKey: boolean;
  hasHaConnection: boolean;
  haUrl: string | null;
  pluginValidated: boolean;
  setupComplete: boolean;
}

export interface GenerateKeyResponse {
  key: string;
  prefix: string;
  message: string;
}

export interface TestHaConnectionResponse {
  connected: boolean;
  message?: string;
  haVersion?: string;
  locationName?: string;
  error?: string;
}

export interface HaStatusResponse {
  pluginConnected: boolean;
  instanceId: string | null;
  lastValidatedAt: string | null;
}

export async function fetchSetupStatus(): Promise<SetupStatus> {
  const res = await fetch(`${BASE}/setup/status`);
  if (!res.ok) throw new Error(`Failed to fetch setup status: ${res.statusText}`);
  return res.json();
}

export async function generateDashboardKey(): Promise<GenerateKeyResponse> {
  const res = await fetch(`${BASE}/setup/generate-dashboard-key`, { method: 'POST' });
  if (!res.ok) {
    const body = await res.json().catch(() => ({}));
    throw new Error(body.error || `Failed to generate key: ${res.statusText}`);
  }
  return res.json();
}

export async function configureHomeAssistant(baseUrl: string, accessToken: string): Promise<void> {
  const res = await fetch(`${BASE}/setup/configure-ha`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ baseUrl, accessToken }),
  });
  if (!res.ok) {
    const body = await res.json().catch(() => ({}));
    throw new Error(body.error || `Failed to configure HA: ${res.statusText}`);
  }
}

export async function testHaConnection(): Promise<TestHaConnectionResponse> {
  const res = await fetch(`${BASE}/setup/test-ha-connection`, { method: 'POST' });
  if (!res.ok) throw new Error(`Failed to test connection: ${res.statusText}`);
  return res.json();
}

export async function generateHaKey(): Promise<GenerateKeyResponse> {
  const res = await fetch(`${BASE}/setup/generate-ha-key`, { method: 'POST' });
  if (!res.ok) {
    const body = await res.json().catch(() => ({}));
    throw new Error(body.error || `Failed to generate HA key: ${res.statusText}`);
  }
  return res.json();
}

export async function fetchHaStatus(): Promise<HaStatusResponse> {
  const res = await fetch(`${BASE}/setup/ha-status`);
  if (!res.ok) throw new Error(`Failed to fetch HA status: ${res.statusText}`);
  return res.json();
}

export async function completeSetup(): Promise<void> {
  const res = await fetch(`${BASE}/setup/complete`, { method: 'POST' });
  if (!res.ok) {
    const body = await res.json().catch(() => ({}));
    throw new Error(body.error || `Failed to complete setup: ${res.statusText}`);
  }
}

export async function regenerateDashboardKey(): Promise<GenerateKeyResponse> {
  const res = await fetch(`${BASE}/setup/regenerate-dashboard-key`, { method: 'POST' });
  if (!res.ok) {
    const body = await res.json().catch(() => ({}));
    throw new Error(body.error || `Failed to regenerate key: ${res.statusText}`);
  }
  return res.json();
}

// ── API Key Management API ───────────────────────────────────────

export interface ApiKeySummary {
  id: string;
  keyPrefix: string;
  name: string;
  createdAt: string;
  lastUsedAt: string | null;
  expiresAt: string | null;
  isRevoked: boolean;
  revokedAt: string | null;
  scopes: string[];
}

export async function fetchApiKeys(): Promise<ApiKeySummary[]> {
  const res = await fetch(`${BASE}/keys`);
  if (!res.ok) throw new Error(`Failed to fetch API keys: ${res.statusText}`);
  return res.json();
}

export async function createApiKey(name: string): Promise<GenerateKeyResponse & { id: string }> {
  const res = await fetch(`${BASE}/keys`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ name }),
  });
  if (!res.ok) throw new Error(`Failed to create key: ${res.statusText}`);
  return res.json();
}

export async function revokeApiKey(id: string): Promise<void> {
  const res = await fetch(`${BASE}/keys/${id}`, { method: 'DELETE' });
  if (!res.ok) {
    const body = await res.json().catch(() => ({}));
    throw new Error(body.error || `Failed to revoke key: ${res.statusText}`);
  }
}

export async function regenerateApiKey(id: string): Promise<GenerateKeyResponse & { id: string }> {
  const res = await fetch(`${BASE}/keys/${id}/regenerate`, { method: 'POST' });
  if (!res.ok) {
    const body = await res.json().catch(() => ({}));
    throw new Error(body.error || `Failed to regenerate key: ${res.statusText}`);
  }
  return res.json();
}

export async function fetchPromptCacheEntries() {
  const res = await fetch(`${BASE}/prompt-cache`);
  if (!res.ok) throw new Error(`Failed to fetch cache entries: ${res.statusText}`);
  return res.json();
}

export async function fetchPromptCacheStats() {
  const res = await fetch(`${BASE}/prompt-cache/stats`);
  if (!res.ok) throw new Error(`Failed to fetch cache stats: ${res.statusText}`);
  return res.json();
}

export async function evictPromptCacheEntry(cacheKey: string) {
  const res = await fetch(`${BASE}/prompt-cache/entry/${encodeURIComponent(cacheKey)}`, { method: 'DELETE' });
  if (!res.ok) throw new Error(`Failed to evict cache entry: ${res.statusText}`);
  return res.json();
}

export async function evictAllPromptCache() {
  const res = await fetch(`${BASE}/prompt-cache`, { method: 'DELETE' });
  if (!res.ok) throw new Error(`Failed to clear cache: ${res.statusText}`);
  return res.json();
}

// ── Task Management API ──────────────────────────────────────────

export async function fetchActiveTasks(): Promise<ActiveTaskSummary[]> {
  const res = await fetch(`${BASE}/tasks/active`);
  if (!res.ok) throw new Error(`Failed to fetch active tasks: ${res.statusText}`);
  return res.json();
}

export async function fetchArchivedTasks(
  params: Record<string, string>,
): Promise<PagedResult<ArchivedTask>> {
  const qs = new URLSearchParams(params).toString();
  const res = await fetch(`${BASE}/tasks/archived?${qs}`);
  if (!res.ok) throw new Error(`Failed to fetch archived tasks: ${res.statusText}`);
  return res.json();
}

export async function fetchTask(id: string): Promise<ActiveTaskSummary | ArchivedTask> {
  const res = await fetch(`${BASE}/tasks/${id}`);
  if (!res.ok) throw new Error(`Task not found`);
  return res.json();
}

export async function fetchTaskStats(): Promise<CombinedTaskStats> {
  const res = await fetch(`${BASE}/tasks/stats`);
  if (!res.ok) throw new Error(`Failed to fetch task stats: ${res.statusText}`);
  return res.json();
}

export async function cancelTask(id: string): Promise<void> {
  const res = await fetch(`${BASE}/tasks/${id}/cancel`, { method: 'POST' });
  if (!res.ok) throw new Error(`Failed to cancel task: ${res.statusText}`);
}

// ── MCP Servers ──────────────────────────────────────────────────────

export async function fetchMcpServers(): Promise<McpToolServerDefinition[]> {
  const res = await fetch(`${BASE}/mcp-servers`);
  if (!res.ok) throw new Error(`Failed to fetch MCP servers: ${res.statusText}`);
  return res.json();
}

export async function fetchMcpServer(id: string): Promise<McpToolServerDefinition> {
  const res = await fetch(`${BASE}/mcp-servers/${id}`);
  if (!res.ok) throw new Error(`MCP server not found`);
  return res.json();
}

export async function createMcpServer(server: Partial<McpToolServerDefinition>): Promise<McpToolServerDefinition> {
  const res = await fetch(`${BASE}/mcp-servers`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(server),
  });
  if (!res.ok) throw new Error(`Failed to create MCP server: ${res.statusText}`);
  return res.json();
}

export async function updateMcpServer(id: string, server: Partial<McpToolServerDefinition>): Promise<McpToolServerDefinition> {
  const res = await fetch(`${BASE}/mcp-servers/${id}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(server),
  });
  if (!res.ok) throw new Error(`Failed to update MCP server: ${res.statusText}`);
  return res.json();
}

export async function deleteMcpServer(id: string): Promise<void> {
  const res = await fetch(`${BASE}/mcp-servers/${id}`, { method: 'DELETE' });
  if (!res.ok) throw new Error(`Failed to delete MCP server: ${res.statusText}`);
}

export async function discoverMcpTools(serverId: string): Promise<McpToolInfo[]> {
  const res = await fetch(`${BASE}/mcp-servers/${serverId}/tools`, { method: 'POST' });
  if (!res.ok) throw new Error(`Failed to discover tools: ${res.statusText}`);
  return res.json();
}

export async function connectMcpServer(id: string): Promise<void> {
  const res = await fetch(`${BASE}/mcp-servers/${id}/connect`, { method: 'POST' });
  if (!res.ok) throw new Error(`Failed to connect MCP server: ${res.statusText}`);
}

export async function disconnectMcpServer(id: string): Promise<void> {
  const res = await fetch(`${BASE}/mcp-servers/${id}/disconnect`, { method: 'POST' });
  if (!res.ok) throw new Error(`Failed to disconnect MCP server: ${res.statusText}`);
}

export async function fetchMcpServerStatuses(): Promise<Record<string, McpServerStatus>> {
  const res = await fetch(`${BASE}/mcp-servers/status`);
  if (!res.ok) throw new Error(`Failed to fetch statuses: ${res.statusText}`);
  return res.json();
}

// ── Agent Definitions ────────────────────────────────────────────────

export async function fetchAgentDefinitions(): Promise<AgentDefinition[]> {
  const res = await fetch(`${BASE}/agent-definitions`);
  if (!res.ok) throw new Error(`Failed to fetch agent definitions: ${res.statusText}`);
  return res.json();
}

export async function fetchAgentDefinition(id: string): Promise<AgentDefinition> {
  const res = await fetch(`${BASE}/agent-definitions/${id}`);
  if (!res.ok) throw new Error(`Agent definition not found`);
  return res.json();
}

export async function createAgentDefinition(definition: Partial<AgentDefinition>): Promise<AgentDefinition> {
  const res = await fetch(`${BASE}/agent-definitions`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(definition),
  });
  if (!res.ok) {
    const text = await res.text();
    throw new Error(text || `Failed to create agent definition: ${res.statusText}`);
  }
  return res.json();
}

export async function updateAgentDefinition(id: string, definition: Partial<AgentDefinition>): Promise<AgentDefinition> {
  const res = await fetch(`${BASE}/agent-definitions/${id}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(definition),
  });
  if (!res.ok) throw new Error(`Failed to update agent definition: ${res.statusText}`);
  return res.json();
}

export async function deleteAgentDefinition(id: string): Promise<void> {
  const res = await fetch(`${BASE}/agent-definitions/${id}`, { method: 'DELETE' });
  if (!res.ok) throw new Error(`Failed to delete agent definition: ${res.statusText}`);
}

export async function reloadDynamicAgents(): Promise<void> {
  const res = await fetch(`${BASE}/agent-definitions/reload`, { method: 'POST' });
  if (!res.ok) throw new Error(`Failed to reload agents: ${res.statusText}`);
}
