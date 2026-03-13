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
  ModelProvider,
  ProviderModelsResponse,
  OptimizableSkillInfo,
  SkillDeviceInfo,
  TraceSearchTerm,
  OptimizationTestCase,
  JobStatusResponse,
  ModelAuthConfig,
  ProviderType,
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

export interface WyomingStatus {
  stt: { ready: boolean };
  wakeWord: { ready: boolean };
  diarization: { ready: boolean };
  customWakeWords: { ready: boolean };
  configured: boolean;
}

export async function fetchWyomingStatus(): Promise<WyomingStatus | null> {
  const res = await fetch(`${BASE}/wyoming/status`);
  if (!res.ok) return null;
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
  /** True when a "Home Assistant" API key exists (e.g. seeded via LUCIA_HA_API_KEY headless). */
  hasHaApiKey?: boolean;
  haUrl: string | null;
  hasChatProvider: boolean;
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

export interface AgentStatusResponse {
  phase: 'waiting_for_config' | 'initializing' | 'ready';
  agentCount: number;
  agents: { name: string; description: string }[];
}

export async function fetchAgentStatus(): Promise<AgentStatusResponse> {
  const res = await fetch(`${BASE}/setup/agent-status`);
  if (!res.ok) throw new Error(`Failed to fetch agent status: ${res.statusText}`);
  return res.json();
}

export async function completeSetup(): Promise<void> {
  const res = await fetch(`${BASE}/setup/complete`, { method: 'POST' });
  if (!res.ok) {
    const body = await res.json().catch(() => ({}));
    throw new Error(body.error || `Failed to complete setup: ${res.statusText}`);
  }
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

// ── Chat Cache (agent-level) ──

export async function fetchChatCacheEntries() {
  const res = await fetch(`${BASE}/chat-cache`);
  if (!res.ok) throw new Error(`Failed to fetch chat cache entries: ${res.statusText}`);
  return res.json();
}

export async function fetchChatCacheStats() {
  const res = await fetch(`${BASE}/chat-cache/stats`);
  if (!res.ok) throw new Error(`Failed to fetch chat cache stats: ${res.statusText}`);
  return res.json();
}

export async function evictChatCacheEntry(cacheKey: string) {
  const res = await fetch(`${BASE}/chat-cache/entry/${encodeURIComponent(cacheKey)}`, { method: 'DELETE' });
  if (!res.ok) throw new Error(`Failed to evict chat cache entry: ${res.statusText}`);
  return res.json();
}

export async function evictAllChatCache() {
  const res = await fetch(`${BASE}/chat-cache`, { method: 'DELETE' });
  if (!res.ok) throw new Error(`Failed to clear chat cache: ${res.statusText}`);
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

// ── Shopping List & Todo Lists ────────────────────────────────────────

export interface ShoppingListItem {
  id: string
  name: string
  complete: boolean
}

export interface TodoItem {
  summary: string
  uid: string
  status: string
}

export interface TodoEntitySummary {
  entityId: string
  name: string
}

export async function fetchShoppingList(): Promise<ShoppingListItem[]> {
  const res = await fetch(`${BASE}/shopping-list`)
  if (!res.ok) throw new Error(`Failed to fetch shopping list: ${res.statusText}`)
  return res.json()
}

export async function addShoppingItem(name: string): Promise<void> {
  const res = await fetch(`${BASE}/shopping-list`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ name }),
  })
  if (!res.ok) throw new Error(`Failed to add item: ${res.statusText}`)
}

export async function completeShoppingItem(name: string): Promise<void> {
  const res = await fetch(`${BASE}/shopping-list/complete`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ name }),
  })
  if (!res.ok) throw new Error(`Failed to complete item: ${res.statusText}`)
}

export async function removeShoppingItem(name: string): Promise<void> {
  const res = await fetch(`${BASE}/shopping-list/remove`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ name }),
  })
  if (!res.ok) throw new Error(`Failed to remove item: ${res.statusText}`)
}

export async function fetchTodoEntities(): Promise<TodoEntitySummary[]> {
  const res = await fetch(`${BASE}/todo-lists`)
  if (!res.ok) throw new Error(`Failed to fetch todo lists: ${res.statusText}`)
  return res.json()
}

export async function fetchTodoItems(entityId: string): Promise<TodoItem[]> {
  const res = await fetch(`${BASE}/todo-lists/${encodeURIComponent(entityId)}`)
  if (!res.ok) throw new Error(`Failed to fetch todo items: ${res.statusText}`)
  return res.json()
}

export async function addTodoItem(entityId: string, name: string): Promise<void> {
  const res = await fetch(`${BASE}/todo-lists/${encodeURIComponent(entityId)}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ name }),
  })
  if (!res.ok) throw new Error(`Failed to add item: ${res.statusText}`)
}

export async function completeTodoItem(entityId: string, item: string): Promise<void> {
  const res = await fetch(`${BASE}/todo-lists/${encodeURIComponent(entityId)}/complete`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ item }),
  })
  if (!res.ok) throw new Error(`Failed to complete item: ${res.statusText}`)
}

export async function removeTodoItem(entityId: string, item: string): Promise<void> {
  const res = await fetch(`${BASE}/todo-lists/${encodeURIComponent(entityId)}/remove`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ item }),
  })
  if (!res.ok) throw new Error(`Failed to remove item: ${res.statusText}`)
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

async function parseApiError(res: Response, fallback: string): Promise<string> {
  try {
    const body = await res.json();
    if (body && typeof body.detail === 'string') return body.detail;
    if (body && typeof body.title === 'string') return body.title;
  } catch {
    /* ignore */
  }
  return fallback;
}

const MCP_CONNECT_TIMEOUT_MS = 45_000; // Slightly longer than backend 30s timeout

export async function connectMcpServer(id: string): Promise<void> {
  const controller = new AbortController();
  const timeoutId = setTimeout(() => controller.abort(), MCP_CONNECT_TIMEOUT_MS);
  try {
    const res = await fetch(`${BASE}/mcp-servers/${id}/connect`, {
      method: 'POST',
      signal: controller.signal,
    });
    if (!res.ok) {
      const msg = await parseApiError(res, `Failed to connect: ${res.statusText}`);
      throw new Error(msg);
    }
  } catch (err) {
    if (err instanceof Error && err.name === 'AbortError') {
      throw new Error('Connection timed out. The MCP server may be unreachable.');
    }
    throw err;
  } finally {
    clearTimeout(timeoutId);
  }
}

export async function disconnectMcpServer(id: string): Promise<void> {
  const res = await fetch(`${BASE}/mcp-servers/${id}/disconnect`, { method: 'POST' });
  if (!res.ok) {
    const msg = await parseApiError(res, `Failed to disconnect: ${res.statusText}`);
    throw new Error(msg);
  }
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

export interface SkillConfigProperty {
  name: string
  type: 'string' | 'number' | 'integer' | 'boolean' | 'string[]'
  defaultValue: unknown
}

export interface SkillConfigSectionData {
  sectionName: string
  displayName: string
  schema: SkillConfigProperty[]
  values: Record<string, unknown>
}

export async function fetchSkillConfig(agentId: string): Promise<SkillConfigSectionData[]> {
  const res = await fetch(`${BASE}/agent-definitions/${agentId}/skill-config`);
  if (res.status === 404) return [];
  if (!res.ok) throw new Error(`Failed to fetch skill config: ${res.statusText}`);
  return res.json();
}

export async function updateSkillConfig(
  agentId: string,
  section: string,
  values: Record<string, unknown>
): Promise<void> {
  const res = await fetch(`${BASE}/agent-definitions/${agentId}/skill-config/${section}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(values),
  });
  if (!res.ok) throw new Error(`Failed to update skill config: ${res.statusText}`);
}

export async function reloadDynamicAgents(): Promise<void> {
  const res = await fetch(`${BASE}/agent-definitions/reload`, { method: 'POST' });
  if (!res.ok) throw new Error(`Failed to reload agents: ${res.statusText}`);
}

/** Re-seed built-in agent definitions (e.g. lists-agent). Use when a new built-in agent was added but your instance was deployed before it existed. */
export async function seedBuiltInAgentDefinitions(): Promise<string> {
  const res = await fetch(`${BASE}/agent-definitions/seed`, { method: 'POST' });
  if (!res.ok) throw new Error(`Failed to seed agents: ${res.statusText}`);
  const text = await res.text();
  return text.replace(/^"|"$/g, '');
}

// ── Model Providers ──────────────────────────────────────────────

export async function fetchModelProviders(purpose?: import('./types').ModelPurpose): Promise<ModelProvider[]> {
  const params = purpose ? `?purpose=${purpose}` : '';
  const res = await fetch(`${BASE}/model-providers${params}`);
  if (!res.ok) throw new Error(`Failed to fetch model providers: ${res.statusText}`);
  return res.json();
}

export async function fetchModelProvider(id: string): Promise<ModelProvider> {
  const res = await fetch(`${BASE}/model-providers/${encodeURIComponent(id)}`);
  if (!res.ok) throw new Error(`Failed to fetch model provider: ${res.statusText}`);
  return res.json();
}

export async function createModelProvider(provider: Partial<ModelProvider>): Promise<ModelProvider> {
  const res = await fetch(`${BASE}/model-providers`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(provider),
  });
  if (!res.ok) {
    const text = await res.text();
    throw new Error(text || `Failed to create model provider: ${res.statusText}`);
  }
  return res.json();
}

export async function updateModelProvider(id: string, provider: Partial<ModelProvider>): Promise<ModelProvider> {
  const res = await fetch(`${BASE}/model-providers/${encodeURIComponent(id)}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(provider),
  });
  if (!res.ok) throw new Error(`Failed to update model provider: ${res.statusText}`);
  return res.json();
}

export async function deleteModelProvider(id: string): Promise<void> {
  const res = await fetch(`${BASE}/model-providers/${encodeURIComponent(id)}`, { method: 'DELETE' });
  if (!res.ok) throw new Error(`Failed to delete model provider: ${res.statusText}`);
}

export async function testModelProvider(id: string): Promise<{ success: boolean; message: string }> {
  const res = await fetch(`${BASE}/model-providers/${encodeURIComponent(id)}/test`, { method: 'POST' });
  return res.json();
}

export async function testEmbeddingProvider(id: string): Promise<{ success: boolean; message: string }> {
  const res = await fetch(`${BASE}/model-providers/${encodeURIComponent(id)}/test-embedding`, { method: 'POST' });
  return res.json();
}

export async function fetchProviderModels(id: string): Promise<ProviderModelsResponse> {
  const res = await fetch(`${BASE}/model-providers/${encodeURIComponent(id)}/models`, { method: 'POST' });
  if (!res.ok) throw new Error(`Failed to fetch provider models: ${res.statusText}`);
  return res.json();
}

export interface ProviderModelDiscoveryRequest {
  providerType: ProviderType;
  endpoint?: string | null;
  auth: ModelAuthConfig;
}

export async function discoverProviderModels(request: ProviderModelDiscoveryRequest): Promise<ProviderModelsResponse> {
  const res = await fetch(`${BASE}/model-providers/models/discover`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(request),
  });
  if (!res.ok) throw new Error(`Failed to discover provider models: ${res.statusText}`);
  return res.json();
}

export async function setProviderModel(id: string, modelName: string): Promise<ModelProvider> {
  const res = await fetch(`${BASE}/model-providers/${encodeURIComponent(id)}/model`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ modelName }),
  });
  if (!res.ok) {
    const text = await res.text();
    throw new Error(text || `Failed to set provider model: ${res.statusText}`);
  }
  return res.json();
}

export interface OllamaModelsResponse {
  models: string[];
  error?: string;
}

export async function fetchOllamaModels(endpoint?: string): Promise<OllamaModelsResponse> {
  const res = await fetch(`${BASE}/model-providers/ollama/models`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ endpoint: endpoint || null }),
  });
  if (!res.ok) throw new Error(`Failed to fetch Ollama models: ${res.statusText}`);
  return res.json();
}

export async function connectCopilotCli(githubToken?: string): Promise<import('./types').CopilotConnectResult> {
  const res = await fetch(`${BASE}/model-providers/copilot/connect`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ githubToken: githubToken || null }),
  });
  if (!res.ok) {
    const text = await res.text();
    throw new Error(text || `Failed to connect to Copilot CLI: ${res.statusText}`);
  }
  return res.json();
}

// ── Activity Dashboard ──

export async function fetchActivitySummary(): Promise<import('./types').ActivitySummary> {
  const res = await fetch(`${BASE}/activity/summary`);
  if (!res.ok) throw new Error(`Failed to fetch activity summary: ${res.statusText}`);
  return res.json();
}

export async function fetchAgentMesh(): Promise<import('./types').MeshTopology> {
  const res = await fetch(`${BASE}/activity/mesh`);
  if (!res.ok) throw new Error(`Failed to fetch agent mesh: ${res.statusText}`);
  return res.json();
}

export async function fetchAgentActivityStats(): Promise<import('./types').AgentActivityStatsMap> {
  const res = await fetch(`${BASE}/activity/agent-stats`);
  if (!res.ok) throw new Error(`Failed to fetch agent stats: ${res.statusText}`);
  return res.json();
}

export function connectActivityStream(
  onEvent: (event: import('./types').LiveEvent) => void,
  onError?: (err: Event) => void,
): EventSource {
  const source = new EventSource(`${BASE}/activity/live`);
  source.onmessage = (e) => {
    try {
      onEvent(JSON.parse(e.data));
    } catch { /* ignore parse errors */ }
  };
  if (onError) source.onerror = onError;
  return source;
}

// ── Entity Location Cache ──────────────────────────────────────────

export interface EntityLocationEmbeddingProgress {
  floorTotalCount: number
  floorGeneratedCount: number
  areaTotalCount: number
  areaGeneratedCount: number
  entityTotalCount: number
  entityGeneratedCount: number
  entityMissingCount: number
  isGenerationRunning: boolean
  lastLoadedAt: string | null
}

export async function fetchEntityLocationSummary() {
  const res = await fetch(`${BASE}/entity-location`);
  if (!res.ok) throw new Error(`Failed to fetch location summary: ${res.statusText}`);
  return res.json();
}

export async function fetchAvailableDomains(): Promise<string[]> {
  const res = await fetch(`${BASE}/entity-location/domains`);
  if (!res.ok) throw new Error(`Failed to fetch domains: ${res.statusText}`);
  return res.json();
}

export async function fetchEntityLocationFloors() {
  const res = await fetch(`${BASE}/entity-location/floors`);
  if (!res.ok) throw new Error(`Failed to fetch floors: ${res.statusText}`);
  return res.json();
}

export async function fetchEntityLocationAreas() {
  const res = await fetch(`${BASE}/entity-location/areas`);
  if (!res.ok) throw new Error(`Failed to fetch areas: ${res.statusText}`);
  return res.json();
}

export async function fetchEntityLocationEntities(domain?: string, agent?: string) {
  const params = new URLSearchParams();
  if (domain) params.set('domain', domain);
  if (agent) params.set('agent', agent);
  const qs = params.toString();
  const res = await fetch(`${BASE}/entity-location/entities${qs ? `?${qs}` : ''}`);
  if (!res.ok) throw new Error(`Failed to fetch entities: ${res.statusText}`);
  return res.json();
}

export async function searchEntityLocation(term: string, domain?: string, agent?: string) {
  const params = new URLSearchParams();
  if (domain) params.set('domain', domain);
  if (agent) params.set('agent', agent);
  const qs = params.toString();
  const res = await fetch(`${BASE}/entity-location/search/${encodeURIComponent(term)}${qs ? `?${qs}` : ''}`);
  if (!res.ok) throw new Error(`Failed to search locations: ${res.statusText}`);
  return res.json();
}

export async function invalidateEntityLocationCache() {
  const res = await fetch(`${BASE}/entity-location/invalidate`, { method: 'POST' });
  if (!res.ok) throw new Error(`Failed to invalidate location cache: ${res.statusText}`);
  return res.json();
}

export async function fetchEntityLocationEmbeddingProgress(): Promise<EntityLocationEmbeddingProgress> {
  const res = await fetch(`${BASE}/entity-location/embedding-progress`);
  if (!res.ok) throw new Error(`Failed to fetch embedding progress: ${res.statusText}`);
  return res.json();
}

export function connectEntityLocationEmbeddingProgressStream(
  onProgress: (progress: EntityLocationEmbeddingProgress) => void,
  onError?: (err: Event) => void,
): EventSource {
  const source = new EventSource(`${BASE}/entity-location/embedding-progress/live`);
  source.onmessage = (e) => {
    try {
      onProgress(JSON.parse(e.data));
    } catch { /* ignore parse errors */ }
  };
  if (onError) source.onerror = onError;
  return source;
}

export async function evictEntityLocationEmbedding(itemType: 'floor' | 'area' | 'entity', itemId: string) {
  const res = await fetch(`${BASE}/entity-location/embeddings/${encodeURIComponent(itemType)}/${encodeURIComponent(itemId)}`, {
    method: 'DELETE',
  });
  if (!res.ok) throw new Error(`Failed to evict embedding: ${res.statusText}`);
  return res.json();
}

export async function regenerateEntityLocationEmbedding(itemType: 'floor' | 'area' | 'entity', itemId: string) {
  const res = await fetch(`${BASE}/entity-location/embeddings/${encodeURIComponent(itemType)}/${encodeURIComponent(itemId)}/regenerate`, {
    method: 'POST',
  });
  if (!res.ok) throw new Error(`Failed to regenerate embedding: ${res.statusText}`);
  return res.json();
}

export async function removeEntityFromCache(entityId: string) {
  const res = await fetch(`${BASE}/entity-location/entities/${encodeURIComponent(entityId)}`, {
    method: 'DELETE',
  });
  if (!res.ok) throw new Error(`Failed to remove entity: ${res.statusText}`);
  return res.json();
}

// ── Entity Visibility ──────────────────────────────────────────────

export async function fetchEntityVisibility(): Promise<{
  useExposedEntitiesOnly: boolean
  entityAgentMap: Record<string, string[]>
}> {
  const res = await fetch(`${BASE}/entity-location/visibility`);
  if (!res.ok) throw new Error(`Failed to fetch visibility config: ${res.statusText}`);
  return res.json();
}

export async function updateVisibilitySettings(useExposedEntitiesOnly: boolean) {
  const res = await fetch(`${BASE}/entity-location/visibility/settings`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ useExposedEntitiesOnly }),
  });
  if (!res.ok) throw new Error(`Failed to update visibility settings: ${res.statusText}`);
  return res.json();
}

export async function updateEntityAgents(updates: Record<string, string[] | null>) {
  const res = await fetch(`${BASE}/entity-location/visibility/agents`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(updates),
  });
  if (!res.ok) throw new Error(`Failed to update entity agents: ${res.statusText}`);
  return res.json();
}

export async function clearAllAgentFilters() {
  const res = await fetch(`${BASE}/entity-location/visibility/agents`, { method: 'DELETE' });
  if (!res.ok) throw new Error(`Failed to clear agent filters: ${res.statusText}`);
  return res.json();
}

export async function fetchAvailableAgents(): Promise<{ name: string; domains: string[] }[]> {
  const res = await fetch(`${BASE}/entity-location/visibility/available-agents`);
  if (!res.ok) throw new Error(`Failed to fetch available agents: ${res.statusText}`);
  return res.json();
}

// ── Matcher Debug API ──────────────────────────────────────────────

export async function searchMatcherDebug(
  term: string,
  options?: { threshold?: number; embeddingWeight?: number; dropoff?: number; disagreementPenalty?: number; embeddingResolutionMargin?: number; domains?: string[] }
): Promise<unknown> {
  const params = new URLSearchParams();
  if (options?.threshold !== undefined) params.set('threshold', String(options.threshold));
  if (options?.embeddingWeight !== undefined) params.set('embeddingWeight', String(options.embeddingWeight));
  if (options?.dropoff !== undefined) params.set('dropoff', String(options.dropoff));
  if (options?.disagreementPenalty !== undefined) params.set('disagreementPenalty', String(options.disagreementPenalty));
  if (options?.embeddingResolutionMargin !== undefined) params.set('embeddingResolutionMargin', String(options.embeddingResolutionMargin));
  if (options?.domains?.length) params.set('domains', options.domains.join(','));
  const qs = params.toString();
  const res = await fetch(`${BASE}/matcher-debug/search/${encodeURIComponent(term)}${qs ? `?${qs}` : ''}`);
  if (!res.ok) throw new Error(`Failed to search matcher debug: ${res.statusText}`);
  return res.json();
}

// ── Alarm Clocks API ───────────────────────────────────────────────

export async function fetchAlarms(): Promise<import('./types').AlarmClock[]> {
  const res = await fetch(`${BASE}/alarms`);
  if (!res.ok) throw new Error(`Failed to fetch alarms: ${res.statusText}`);
  return res.json();
}

export async function fetchAlarm(id: string): Promise<import('./types').AlarmClock> {
  const res = await fetch(`${BASE}/alarms/${id}`);
  if (!res.ok) throw new Error(`Alarm not found`);
  return res.json();
}

export async function createAlarm(alarm: {
  name: string;
  targetEntity: string;
  alarmSoundId?: string | null;
  cronSchedule?: string | null;
  nextFireAt?: string | null;
  playbackInterval?: string;
  autoDismissAfter?: string;
  isEnabled?: boolean;
}): Promise<import('./types').AlarmClock> {
  const res = await fetch(`${BASE}/alarms`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(alarm),
  });
  if (!res.ok) {
    const text = await res.text();
    throw new Error(text || `Failed to create alarm: ${res.statusText}`);
  }
  return res.json();
}

export async function updateAlarm(id: string, alarm: {
  name?: string;
  targetEntity?: string;
  alarmSoundId?: string | null;
  cronSchedule?: string | null;
  nextFireAt?: string | null;
  playbackInterval?: string;
  autoDismissAfter?: string;
  isEnabled?: boolean;
}): Promise<import('./types').AlarmClock> {
  const res = await fetch(`${BASE}/alarms/${id}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(alarm),
  });
  if (!res.ok) throw new Error(`Failed to update alarm: ${res.statusText}`);
  return res.json();
}

export async function deleteAlarm(id: string): Promise<void> {
  const res = await fetch(`${BASE}/alarms/${id}`, { method: 'DELETE' });
  if (!res.ok) throw new Error(`Failed to delete alarm: ${res.statusText}`);
}

export async function enableAlarm(id: string): Promise<import('./types').AlarmClock> {
  const res = await fetch(`${BASE}/alarms/${id}/enable`, { method: 'PUT' });
  if (!res.ok) throw new Error(`Failed to enable alarm: ${res.statusText}`);
  return res.json();
}

export async function disableAlarm(id: string): Promise<import('./types').AlarmClock> {
  const res = await fetch(`${BASE}/alarms/${id}/disable`, { method: 'PUT' });
  if (!res.ok) throw new Error(`Failed to disable alarm: ${res.statusText}`);
  return res.json();
}

export async function dismissAlarm(id: string): Promise<void> {
  const res = await fetch(`${BASE}/alarms/${id}/dismiss`, { method: 'POST' });
  if (!res.ok) throw new Error(`Failed to dismiss alarm: ${res.statusText}`);
}

export async function snoozeAlarm(id: string, duration?: string): Promise<import('./types').AlarmClock> {
  const res = await fetch(`${BASE}/alarms/${id}/snooze`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(duration ? { duration } : {}),
  });
  if (!res.ok) throw new Error(`Failed to snooze alarm: ${res.statusText}`);
  return res.json();
}

// ── Alarm Sounds API ─────────────────────────────────────────────

export async function fetchAlarmSounds(): Promise<import('./types').AlarmSound[]> {
  const res = await fetch(`${BASE}/alarms/sounds`);
  if (!res.ok) throw new Error(`Failed to fetch alarm sounds: ${res.statusText}`);
  return res.json();
}

export async function createAlarmSound(sound: {
  name: string;
  mediaSourceUri: string;
  uploadedViaLucia?: boolean;
  isDefault?: boolean;
}): Promise<import('./types').AlarmSound> {
  const res = await fetch(`${BASE}/alarms/sounds`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(sound),
  });
  if (!res.ok) {
    const text = await res.text();
    throw new Error(text || `Failed to create alarm sound: ${res.statusText}`);
  }
  return res.json();
}

export async function uploadAlarmSound(file: File, name: string, isDefault: boolean): Promise<import('./types').AlarmSound> {
  const form = new FormData();
  form.append('file', file);
  form.append('name', name);
  form.append('isDefault', String(isDefault));
  const res = await fetch(`${BASE}/alarms/sounds/upload`, {
    method: 'POST',
    body: form,
  });
  if (!res.ok) {
    const text = await res.text();
    throw new Error(text || `Failed to upload alarm sound: ${res.statusText}`);
  }
  return res.json();
}

export async function deleteAlarmSound(id: string): Promise<void> {
  const res = await fetch(`${BASE}/alarms/sounds/${id}`, { method: 'DELETE' });
  if (!res.ok) throw new Error(`Failed to delete alarm sound: ${res.statusText}`);
}

export async function setDefaultAlarmSound(id: string): Promise<import('./types').AlarmSound> {
  const res = await fetch(`${BASE}/alarms/sounds/${id}/default`, { method: 'PUT' });
  if (!res.ok) throw new Error(`Failed to set default sound: ${res.statusText}`);
  return res.json();
}

// ────────────────────────── Presence Detection ──────────────────────────

export async function fetchOccupiedAreas(): Promise<import('./types').OccupiedArea[]> {
  const res = await fetch(`${BASE}/presence/occupied`);
  if (!res.ok) throw new Error(`Failed to fetch occupied areas: ${res.statusText}`);
  return res.json();
}

export async function fetchAreaOccupancy(areaId: string): Promise<{ areaId: string; isOccupied: boolean | null; occupantCount: number | null }> {
  const res = await fetch(`${BASE}/presence/occupied/${areaId}`);
  if (!res.ok) throw new Error(`Failed to fetch area occupancy: ${res.statusText}`);
  return res.json();
}

export async function fetchPresenceSensors(): Promise<import('./types').PresenceSensorMapping[]> {
  const res = await fetch(`${BASE}/presence/sensors`);
  if (!res.ok) throw new Error(`Failed to fetch presence sensors: ${res.statusText}`);
  return res.json();
}

export async function updatePresenceSensor(entityId: string, body: Record<string, unknown>): Promise<import('./types').PresenceSensorMapping> {
  const res = await fetch(`${BASE}/presence/sensors/${encodeURIComponent(entityId)}`, {
    method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body),
  });
  if (!res.ok) throw new Error(`Failed to update sensor: ${res.statusText}`);
  return res.json();
}

export async function deletePresenceSensor(entityId: string): Promise<void> {
  const res = await fetch(`${BASE}/presence/sensors/${encodeURIComponent(entityId)}`, { method: 'DELETE' });
  if (!res.ok) throw new Error(`Failed to delete sensor: ${res.statusText}`);
}

export async function refreshPresenceSensors(): Promise<import('./types').PresenceSensorMapping[]> {
  const res = await fetch(`${BASE}/presence/sensors/refresh`, { method: 'POST' });
  if (!res.ok) throw new Error(`Failed to refresh sensors: ${res.statusText}`);
  return res.json();
}

export async function fetchPresenceConfig(): Promise<{ enabled: boolean }> {
  const res = await fetch(`${BASE}/presence/config`);
  if (!res.ok) throw new Error(`Failed to fetch presence config: ${res.statusText}`);
  return res.json();
}

export async function updatePresenceConfig(body: { enabled?: boolean }): Promise<{ enabled: boolean }> {
  const res = await fetch(`${BASE}/presence/config`, {
    method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body),
  });
  if (!res.ok) throw new Error(`Failed to update presence config: ${res.statusText}`);
  return res.json();
}

// ── Skill Optimizer ─────────────────────────────────────────────

export async function fetchOptimizableSkills(): Promise<OptimizableSkillInfo[]> {
  const res = await fetch(`${BASE}/skill-optimizer/skills`);
  if (!res.ok) throw new Error(`Failed to fetch skills: ${res.statusText}`);
  return res.json();
}

export async function fetchSkillDevices(skillId: string): Promise<SkillDeviceInfo[]> {
  const res = await fetch(`${BASE}/skill-optimizer/skills/${skillId}/devices`);
  if (!res.ok) throw new Error(`Failed to fetch devices: ${res.statusText}`);
  return res.json();
}

export async function fetchSkillTraces(skillId: string, limit?: number): Promise<TraceSearchTerm[]> {
  const qs = limit ? `?limit=${limit}` : '';
  const res = await fetch(`${BASE}/skill-optimizer/skills/${skillId}/traces${qs}`);
  if (!res.ok) throw new Error(`Failed to fetch traces: ${res.statusText}`);
  return res.json();
}

export async function startOptimization(
  skillId: string,
  embeddingModel: string,
  testCases: OptimizationTestCase[],
): Promise<{ jobId: string }> {
  const res = await fetch(`${BASE}/skill-optimizer/skills/${skillId}/optimize`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ embeddingModel, testCases }),
  });
  if (!res.ok) throw new Error(`Failed to start optimization: ${res.statusText}`);
  return res.json();
}

export async function fetchOptimizerJob(jobId: string): Promise<JobStatusResponse> {
  const res = await fetch(`${BASE}/skill-optimizer/jobs/${jobId}`);
  if (!res.ok) throw new Error(`Failed to fetch job: ${res.statusText}`);
  return res.json();
}

export async function cancelOptimizerJob(jobId: string): Promise<void> {
  const res = await fetch(`${BASE}/skill-optimizer/jobs/${jobId}/cancel`, { method: 'POST' });
  if (!res.ok) throw new Error(`Failed to cancel job: ${res.statusText}`);
}

// ─── Plugin Repositories ───

export async function fetchPluginRepos(): Promise<import('./types').PluginRepository[]> {
  const res = await fetch(`${BASE}/plugin-repos`);
  if (!res.ok) throw new Error(`Failed to fetch plugin repos`);
  return res.json();
}

export async function addPluginRepo(body: { url: string; branch?: string; manifestPath?: string }): Promise<import('./types').PluginRepository> {
  const res = await fetch(`${BASE}/plugin-repos`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
  if (!res.ok) throw new Error(`Failed to add plugin repo`);
  return res.json();
}

export async function deletePluginRepo(id: string): Promise<void> {
  const res = await fetch(`${BASE}/plugin-repos/${id}`, { method: 'DELETE' });
  if (!res.ok) throw new Error(`Failed to delete plugin repo`);
}

export async function syncPluginRepo(id: string): Promise<void> {
  const res = await fetch(`${BASE}/plugin-repos/${id}/sync`, { method: 'POST' });
  if (!res.ok) throw new Error(`Failed to sync plugin repo`);
}

// ─── Plugin Store ───

export async function fetchAvailablePlugins(query?: string): Promise<import('./types').AvailablePlugin[]> {
  const qs = query ? `?q=${encodeURIComponent(query)}` : '';
  const res = await fetch(`${BASE}/plugins/available${qs}`);
  if (!res.ok) throw new Error(`Failed to fetch available plugins`);
  return res.json();
}

export async function installPlugin(id: string): Promise<void> {
  const res = await fetch(`${BASE}/plugins/${id}/install`, { method: 'POST' });
  if (!res.ok) throw new Error(`Failed to install plugin`);
}

// ─── Installed Plugins ───

export async function fetchInstalledPlugins(): Promise<import('./types').InstalledPlugin[]> {
  const res = await fetch(`${BASE}/plugins/installed`);
  if (!res.ok) throw new Error(`Failed to fetch installed plugins`);
  return res.json();
}

export async function checkPluginUpdates(): Promise<import('./types').PluginUpdateInfo[]> {
  const res = await fetch(`${BASE}/plugins/updates`);
  if (!res.ok) throw new Error(`Failed to check plugin updates`);
  return res.json();
}

export async function updatePlugin(id: string): Promise<void> {
  const res = await fetch(`${BASE}/plugins/${id}/update`, { method: 'POST' });
  if (!res.ok) throw new Error(`Failed to update plugin`);
}

export async function enablePlugin(id: string): Promise<void> {
  const res = await fetch(`${BASE}/plugins/${id}/enable`, { method: 'POST' });
  if (!res.ok) throw new Error(`Failed to enable plugin`);
}

export async function disablePlugin(id: string): Promise<void> {
  const res = await fetch(`${BASE}/plugins/${id}/disable`, { method: 'POST' });
  if (!res.ok) throw new Error(`Failed to disable plugin`);
}

export async function uninstallPlugin(id: string): Promise<void> {
  const res = await fetch(`${BASE}/plugins/${id}`, { method: 'DELETE' });
  if (!res.ok) throw new Error(`Failed to uninstall plugin`);
}

export async function fetchPluginConfigSchemas(): Promise<import('./types').PluginConfigSchema[]> {
  const res = await fetch(`${BASE}/plugins/config/schemas`);
  if (!res.ok) throw new Error(`Failed to fetch plugin config schemas`);
  return res.json();
}

// ─── System ───

export async function fetchRestartRequired(): Promise<{ restartRequired: boolean }> {
  const res = await fetch(`${BASE}/system/restart-required`);
  if (!res.ok) throw new Error(`Failed to check restart status`);
  return res.json();
}

export async function triggerRestart(): Promise<void> {
  const res = await fetch(`${BASE}/system/restart`, { method: 'POST' });
  if (!res.ok) throw new Error(`Failed to trigger restart`);
}

// ── Voice Onboarding ──────────────────────────────────────────────

export async function startOnboarding(speakerName: string, wakeWordPhrase?: string) {
  const res = await fetch(`${BASE}/onboarding/start`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ speakerName, wakeWordPhrase }),
  })
  if (!res.ok) throw new Error(`Failed to start onboarding: ${res.statusText}`)
  return res.json()
}

export async function uploadVoiceSample(sessionId: string, audioBlob: Blob) {
  const formData = new FormData()
  formData.append('audio', audioBlob, 'sample.wav')
  const res = await fetch(`${BASE}/onboarding/${sessionId}/sample`, {
    method: 'POST',
    body: formData,
  })
  if (!res.ok) throw new Error(`Failed to upload voice sample: ${res.statusText}`)
  return res.json()
}

export async function getOnboardingStatus(sessionId: string) {
  const res = await fetch(`${BASE}/onboarding/${sessionId}`)
  if (!res.ok) throw new Error(`Failed to fetch onboarding status: ${res.statusText}`)
  return res.json()
}

export async function listSpeakerProfiles() {
  const res = await fetch(`${BASE}/speakers`)
  if (!res.ok) throw new Error(`Failed to fetch speaker profiles: ${res.statusText}`)
  return res.json()
}

export async function deleteSpeakerProfile(id: string) {
  const res = await fetch(`${BASE}/speakers/${id}`, { method: 'DELETE' })
  if (!res.ok) throw new Error(`Failed to delete speaker profile: ${res.statusText}`)
}

export async function listWakeWords() {
  const res = await fetch(`${BASE}/wake-words`)
  if (!res.ok) throw new Error(`Failed to fetch wake words: ${res.statusText}`)
  return res.json()
}

export async function deleteWakeWord(id: string) {
  const res = await fetch(`${BASE}/wake-words/${id}`, { method: 'DELETE' })
  if (!res.ok) throw new Error(`Failed to delete wake word: ${res.statusText}`)
}

// Wyoming Model Management
export interface AsrModel {
  id: string
  name: string
  architecture: string
  isStreaming: boolean
  languages: string[]
  sizeBytes: number
  description: string
  downloadUrl: string
  isDefault: boolean
  minMemoryMb: number
}

export interface InstalledModel extends AsrModel {
  localPath: string
  isActive: boolean
}

export interface BackgroundTask {
  id: string
  description: string
  status: 'Queued' | 'Running' | 'Complete' | 'Failed' | 'Cancelled'
  progressPercent: number
  progressMessage?: string
  error?: string
  createdAt: string
  completedAt?: string
}

export interface ModelDownloadResult {
  taskId: string
}

export async function fetchBackgroundTasks(): Promise<BackgroundTask[]> {
  const res = await fetch(`${BASE}/tasks/background`)
  if (!res.ok) return []
  return res.json()
}

export async function fetchBackgroundTask(taskId: string): Promise<BackgroundTask | null> {
  const res = await fetch(`${BASE}/tasks/background/${encodeURIComponent(taskId)}`)
  if (!res.ok) return null
  return res.json()
}

export async function fetchAvailableModels(): Promise<AsrModel[]> {
  const res = await fetch(`${BASE}/wyoming/models`)
  if (!res.ok) return []
  return res.json()
}

export async function fetchInstalledModels(): Promise<AsrModel[]> {
  const res = await fetch(`${BASE}/wyoming/models/installed`)
  if (!res.ok) return []
  return res.json()
}

export async function fetchActiveModel(): Promise<{ activeModel: string }> {
  const res = await fetch(`${BASE}/wyoming/models/active`)
  if (!res.ok) return { activeModel: '' }
  return res.json()
}

export async function downloadModel(modelId: string): Promise<ModelDownloadResult> {
  const res = await fetch(`${BASE}/wyoming/models/${encodeURIComponent(modelId)}/download`, {
    method: 'POST',
  })
  if (!res.ok) throw new Error(`Failed to start model download: ${res.statusText}`)
  return res.json()
}

export async function activateModel(modelId: string): Promise<void> {
  await fetch(`${BASE}/wyoming/models/${encodeURIComponent(modelId)}/activate`, {
    method: 'POST',
  })
}

export async function deleteModel(modelId: string): Promise<void> {
  await fetch(`${BASE}/wyoming/models/${encodeURIComponent(modelId)}`, {
    method: 'DELETE',
  })
}
