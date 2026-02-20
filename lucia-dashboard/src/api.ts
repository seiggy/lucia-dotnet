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
}

export async function fetchConfigSections(): Promise<ConfigSectionSummary[]> {
  const res = await fetch(`${BASE}/config/sections`);
  if (!res.ok) throw new Error(`Failed to fetch config sections: ${res.statusText}`);
  return res.json();
}

export async function fetchConfigSection(section: string, showSecrets = false): Promise<ConfigEntryDto[]> {
  const qs = showSecrets ? '?showSecrets=true' : '';
  const res = await fetch(`${BASE}/config/sections/${encodeURIComponent(section)}${qs}`);
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

// ── Prompt Cache API ─────────────────────────────────────────────

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
