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

// MCP Tool Servers
export interface McpToolServerDefinition {
  id: string;
  name: string;
  description: string;
  transportType: string;
  command?: string;
  arguments: string[];
  workingDirectory?: string;
  environmentVariables: Record<string, string>;
  url?: string;
  headers: Record<string, string>;
  enabled: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface McpToolInfo {
  serverId: string;
  serverName: string;
  toolName: string;
  description?: string;
}

export interface McpServerStatus {
  serverId: string;
  serverName: string;
  state: 'Disconnected' | 'Connecting' | 'Connected' | 'Error';
  errorMessage?: string;
  toolCount: number;
  connectedAt?: string;
}

// Agent Definitions
export interface AgentToolReference {
  serverId: string;
  toolName: string;
}

export interface AgentDefinition {
  id: string;
  name: string;
  displayName?: string;
  description?: string;
  instructions?: string;
  tools: AgentToolReference[];
  modelConnectionName?: string;
  embeddingProviderName?: string;
  enabled: boolean;
  isBuiltIn: boolean;
  isRemote: boolean;
  isOrchestrator: boolean;
  createdAt: string;
  updatedAt: string;
}

// Model Providers
export type ProviderType = 'OpenAI' | 'AzureOpenAI' | 'AzureAIInference' | 'Ollama' | 'Anthropic' | 'GoogleGemini' | 'GitHubCopilot';

export type ModelPurpose = 'Chat' | 'Embedding';

/// Provider types that support embedding generation
export const EmbeddingCapableProviders: ProviderType[] = ['OpenAI', 'AzureOpenAI', 'AzureAIInference', 'Ollama', 'GoogleGemini'];

export interface ModelAuthConfig {
  authType: string;
  apiKey?: string;
  useDefaultCredentials: boolean;
}

export interface ModelProvider {
  id: string;
  name: string;
  providerType: ProviderType;
  purpose: ModelPurpose;
  endpoint?: string;
  modelName: string;
  auth: ModelAuthConfig;
  copilotMetadata?: CopilotModelMetadata;
  enabled: boolean;
  isBuiltIn: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface CopilotModelMetadata {
  supportsVision: boolean;
  supportsReasoningEffort: boolean;
  maxPromptTokens?: number;
  maxOutputTokens?: number;
  maxContextWindowTokens: number;
  policyState?: string;
  policyTerms?: string;
  billingMultiplier: number;
  supportedReasoningEfforts: string[];
  defaultReasoningEffort?: string;
}

export interface CopilotModelInfo {
  id: string;
  name: string;
  supportsVision: boolean;
  supportsReasoningEffort: boolean;
  maxPromptTokens?: number;
  maxOutputTokens?: number;
  maxContextWindowTokens: number;
  policyState?: string;
  policyTerms?: string;
  billingMultiplier: number;
  supportedReasoningEfforts: string[];
  defaultReasoningEffort?: string;
}

export interface CopilotConnectResult {
  success: boolean;
  message: string;
  models: CopilotModelInfo[];
}

// ── Activity Dashboard Types ──

export interface LiveEvent {
  type: 'requestStart' | 'routing' | 'agentStart' | 'toolCall' | 'toolResult' | 'agentComplete' | 'requestComplete' | 'error'
  agentName?: string
  toolName?: string
  state?: string
  message?: string
  isRemote?: boolean
  confidence?: number
  durationMs?: number
  timestamp: string
  errorMessage?: string
}

export interface MeshNode {
  id: string
  label: string
  nodeType: 'orchestrator' | 'agent' | 'tool'
  isRemote?: boolean
}

export interface MeshEdge {
  source: string
  target: string
}

export interface MeshTopology {
  nodes: MeshNode[]
  edges: MeshEdge[]
}

export interface ActivitySummary {
  traces: TraceStats
  tasks: { totalTasks: number; completedCount: number; failedCount: number }
  cache: { totalEntries: number; totalHits: number; totalMisses: number; hitRate: number }
}

export interface AgentActivityStatsMap {
  [agentId: string]: { requestCount: number; errorRate: number }
}
