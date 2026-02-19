import type {
  ConversationTrace,
  PagedResult,
  TraceStats,
  DatasetExportRecord,
  ExportFilterCriteria,
  LabelStatus,
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
