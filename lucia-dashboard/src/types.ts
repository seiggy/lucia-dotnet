export const LabelStatus = {
  Unlabeled: 0,
  Positive: 1,
  Negative: 2,
} as const;
export type LabelStatus = (typeof LabelStatus)[keyof typeof LabelStatus];

export interface TracedMessage {
  role: string;
  content: string | null;
  timestamp: string;
}

export interface TracedToolCall {
  toolName: string;
  arguments: string | null;
  result: string | null;
  timestamp: string;
}

export interface AgentExecutionRecord {
  agentId: string;
  modelDeploymentName: string | null;
  messages: TracedMessage[];
  toolCalls: TracedToolCall[];
  executionDurationMs: number;
  success: boolean;
  errorMessage: string | null;
  responseContent: string | null;
}

export interface AgentInstructionRecord {
  agentId: string;
  instruction: string;
}

export interface RoutingDecision {
  selectedAgentId: string;
  additionalAgentIds: string[];
  confidence: number;
  reasoning: string | null;
  routingDurationMs: number;
  modelDeploymentName: string | null;
  agentInstructions: AgentInstructionRecord[];
}

export interface TraceLabel {
  status: LabelStatus;
  reviewerNotes: string | null;
  correctionText: string | null;
  labeledAt: string | null;
}

export interface ConversationTrace {
  id: string;
  timestamp: string;
  sessionId: string;
  taskId: string | null;
  userInput: string;
  conversationHistory: TracedMessage[];
  systemPrompt: string | null;
  routing: RoutingDecision | null;
  agentExecutions: AgentExecutionRecord[];
  finalResponse: string | null;
  totalDurationMs: number;
  label: TraceLabel;
  isErrored: boolean;
  errorMessage: string | null;
}

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export interface TraceStats {
  totalTraces: number;
  unlabeledCount: number;
  positiveCount: number;
  negativeCount: number;
  erroredCount: number;
  byAgent: Record<string, number>;
}

export interface ExportFilterCriteria {
  labelFilter: LabelStatus | null;
  fromDate: string | null;
  toDate: string | null;
  agentFilter: string | null;
  modelFilter: string | null;
  includeCorrections: boolean;
}

export interface DatasetExportRecord {
  id: string;
  timestamp: string;
  filterCriteria: ExportFilterCriteria;
  recordCount: number;
  fileSizeBytes: number;
  filePath: string | null;
  isComplete: boolean;
}

// ── Task Types ───────────────────────────────────────────────────

export interface ActiveTaskSummary {
  id: string;
  contextId: string | null;
  status: string;
  messageCount: number;
  userInput: string | null;
  lastUpdated: string;
}

export interface ArchivedMessage {
  role: string;
  text: string | null;
  messageId: string | null;
}

export interface ArchivedTask {
  id: string;
  contextId: string | null;
  status: string;
  agentIds: string[];
  userInput: string | null;
  finalResponse: string | null;
  messageCount: number;
  history: ArchivedMessage[];
  createdAt: string;
  archivedAt: string;
}

export interface TaskStats {
  totalTasks: number;
  completedCount: number;
  failedCount: number;
  canceledCount: number;
  byAgent: Record<string, number>;
}

export interface CombinedTaskStats {
  activeCount: number;
  archived: TaskStats;
}
