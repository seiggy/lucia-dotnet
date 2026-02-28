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

export async function fetchEntityLocationSummary() {
  const res = await fetch(`${BASE}/entity-location`);
  if (!res.ok) throw new Error(`Failed to fetch location summary: ${res.statusText}`);
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

export async function fetchEntityLocationEntities(domain?: string) {
  const params = domain ? `?domain=${encodeURIComponent(domain)}` : '';
  const res = await fetch(`${BASE}/entity-location/entities${params}`);
  if (!res.ok) throw new Error(`Failed to fetch entities: ${res.statusText}`);
  return res.json();
}

export async function searchEntityLocation(term: string, domain?: string) {
  const params = domain ? `?domain=${encodeURIComponent(domain)}` : '';
  const res = await fetch(`${BASE}/entity-location/search/${encodeURIComponent(term)}${params}`);
  if (!res.ok) throw new Error(`Failed to search locations: ${res.statusText}`);
  return res.json();
}

export async function invalidateEntityLocationCache() {
  const res = await fetch(`${BASE}/entity-location/invalidate`, { method: 'POST' });
  if (!res.ok) throw new Error(`Failed to invalidate location cache: ${res.statusText}`);
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
