/**
 * Fine-tuning label status for conversation traces.
 * Used by the training pipeline to filter exported JSONL datasets.
 */
export const LabelStatus = {
  Unlabeled: 0,
  Positive: 1,
  Negative: 2,
} as const;
export type LabelStatus = (typeof LabelStatus)[keyof typeof LabelStatus];

/** A single message (user, assistant, or system) within a traced conversation. */
export interface TracedMessage {
  role: string;
  content: string | null;
  timestamp: string;
}

/** A tool invocation recorded during agent execution (arguments and result JSON). */
export interface TracedToolCall {
  toolName: string;
  arguments: string | null;
  result: string | null;
  timestamp: string;
}

/** Result of a single agent's execution within an orchestration request. */
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

/** System instruction snapshot sent to an agent during routing. */
export interface AgentInstructionRecord {
  agentId: string;
  instruction: string;
}

/** The router's decision about which agent(s) should handle a user request. */
export interface RoutingDecision {
  selectedAgentId: string;
  additionalAgentIds: string[];
  confidence: number;
  reasoning: string | null;
  routingDurationMs: number;
  modelDeploymentName: string | null;
  agentInstructions: AgentInstructionRecord[];
}

/** Human-applied label for a conversation trace (used for LLM fine-tuning). */
export interface TraceLabel {
  status: LabelStatus;
  reviewerNotes: string | null;
  correctionText: string | null;
  labeledAt: string | null;
}

/** An OpenTelemetry span captured during an orchestration request. */
export interface TracedSpan {
  spanId: string;
  parentSpanId: string | null;
  operationName: string;
  source: string;
  startTimeUtc: string;
  durationMs: number;
  tags: Record<string, string>;
}

/**
 * A complete conversation trace from the orchestration pipeline.
 * Captures user input, routing decision, per-agent execution records,
 * tool calls, spans, and the aggregated final response.
 */
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
  spans: TracedSpan[];
}

/** Generic paginated response wrapper used by all list endpoints. */
export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

/** Aggregate statistics for conversation traces (shown on the Activity page). */
export interface TraceStats {
  totalTraces: number;
  unlabeledCount: number;
  positiveCount: number;
  negativeCount: number;
  erroredCount: number;
  byAgent: Record<string, number>;
}

/** Filter criteria for JSONL dataset exports (fine-tuning pipeline). */
export interface ExportFilterCriteria {
  labelFilter: LabelStatus | null;
  fromDate: string | null;
  toDate: string | null;
  agentFilter: string | null;
  modelFilter: string | null;
  includeCorrections: boolean;
}

/** A generated JSONL dataset export record with download metadata. */
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

/** Summary of an active (in-progress) task stored in Redis. */
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

/** A completed task archived to MongoDB for long-term storage. */
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
/** Configuration for an MCP (Model Context Protocol) tool server. */
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
/** Reference to an MCP tool assigned to an agent (server ID + tool name). */
export interface AgentToolReference {
  serverId: string;
  toolName: string;
}

/**
 * A user-defined or built-in agent definition stored in MongoDB.
 * Defines the agent's identity, system instructions, tool assignments, and model connections.
 */
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
/** LLM provider type identifier. */
export type ProviderType = 'OpenAI' | 'OpenRouter' | 'AzureOpenAI' | 'AzureAIInference' | 'Ollama' | 'Anthropic' | 'GoogleGemini' | 'GitHubCopilot';

/** Whether a provider is configured for chat completion or embedding generation. */
export type ModelPurpose = 'Chat' | 'Embedding';

/** Provider types that support embedding generation. */
export const EmbeddingCapableProviders: ProviderType[] = ['OpenAI', 'OpenRouter', 'AzureOpenAI', 'AzureAIInference', 'Ollama', 'GoogleGemini'];

export interface ModelAuthConfig {
  authType: string;
  apiKey?: string;
  useDefaultCredentials: boolean;
}

/**
 * An LLM model provider configuration stored in MongoDB.
 * Supports OpenAI, Azure OpenAI, Ollama, Anthropic, Google Gemini, and more.
 */
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

export interface ProviderModelsResponse {
  models: string[];
  error?: string;
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

/**
 * A real-time event from the orchestration pipeline, delivered via Server-Sent Events.
 * The {@link useActivityStream} hook consumes these to update the mesh graph and timeline.
 */
export interface LiveEvent {
  type: 'connected' | 'requestStart' | 'routing' | 'agentStart' | 'toolCall' | 'toolResult' | 'agentComplete' | 'requestComplete' | 'error'
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

/** A node in the agent mesh graph (orchestrator, agent, or tool). */
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

/** Agent mesh topology returned by `/api/activity/mesh`. */
export interface MeshTopology {
  nodes: MeshNode[]
  edges: MeshEdge[]
}

/** Combined activity summary for the dashboard overview cards. */
export interface ActivitySummary {
  traces: TraceStats
  tasks: { totalTasks: number; completedCount: number; failedCount: number }
  cache: { totalEntries: number; totalHits: number; totalMisses: number; hitRate: number }
  chatCache: { totalEntries: number; totalHits: number; totalMisses: number; hitRate: number }
  conversation: { commandParsed: number; llmFallback: number; errors: number; total: number; commandRate: number }
}

export interface AgentActivityStatsMap {
  [agentId: string]: { requestCount: number; errorRate: number }
}

// ── Alarm Clock Types ──

/** An alarm clock definition with CRON schedule, sound, and volume ramp settings. */
export interface AlarmClock {
  id: string
  name: string
  targetEntity: string
  alarmSoundId: string | null
  cronSchedule: string | null
  nextFireAt: string | null
  playbackInterval: string
  autoDismissAfter: string
  lastDismissedAt: string | null
  isEnabled: boolean
  createdAt: string
  volumeStart: number | null
  volumeEnd: number | null
  volumeRampDuration: string
}

export interface AlarmSound {
  id: string
  name: string
  mediaSourceUri: string
  uploadedViaLucia: boolean
  isDefault: boolean
  createdAt: string
}

// --- Presence Detection ---

export type PresenceConfidence = 'None' | 'Low' | 'Medium' | 'High' | 'Highest'

export interface PresenceSensorMapping {
  entityId: string
  areaId: string
  areaName: string | null
  confidence: PresenceConfidence
  isUserOverride: boolean
  isDisabled: boolean
}

export interface OccupiedArea {
  areaId: string
  areaName: string
  isOccupied: boolean
  occupantCount: number | null
  confidence: PresenceConfidence
}

// ── Skill Optimizer ─────────────────────────────────────────────

/** Tunable parameters for the HybridEntityMatcher used by agent skills. */
export interface HybridMatchOptions {
  threshold: number
  embeddingWeight: number
  scoreDropoffRatio: number
  disagreementPenalty: number
  embeddingResolutionMargin: number
}

export interface OptimizableSkillInfo {
  skillId: string
  displayName: string
  configSection: string
  currentParams: HybridMatchOptions
}

export interface SkillDeviceInfo {
  entityId: string
  friendlyName: string
}

export interface TraceSearchTerm {
  searchTerm: string
  occurrenceCount: number
  lastSeen: string
  traceId: string | null
}

export interface OptimizationTestCase {
  searchTerm: string
  expectedEntityIds: string[]
  maxResults: number
  variant: string | null
}

export interface SkillTestDataset {
  skillId: string
  skillDisplayName: string
  currentParams: HybridMatchOptions
  exportedAt: string
  testCases: OptimizationTestCase[]
  entities: SkillDeviceInfo[]
}

export interface OptimizationProgress {
  iteration: number
  currentScore: number
  maxScore: number
  bestParams: HybridMatchOptions
  step: number
  evaluatedPoints: number
  isComplete: boolean
  message: string | null
}

export interface OptimizationCaseResult {
  testCase: OptimizationTestCase
  found: boolean
  foundEntityIds: string[]
  missedEntityIds: string[]
  matchCount: number
  countWithinLimit: boolean
  caseScore: number
}

export interface OptimizationResult {
  bestParams: HybridMatchOptions
  score: number
  maxScore: number
  caseResults: OptimizationCaseResult[]
  totalEvaluatedPoints: number
  totalIterations: number
  isPerfect: boolean
}

export interface JobStatusResponse {
  jobId: string
  skillId: string
  embeddingModel: string
  testCaseCount: number
  status: string
  startedAt: string
  completedAt: string | null
  progress: OptimizationProgress | null
  result: OptimizationResult | null
  error: string | null
}

// ─── Plugin System ───

/** A plugin repository source (Git-based) for discovering available plugins. */
export interface PluginRepository {
  id: string
  name: string
  url: string
  branch: string
  manifestPath: string
  lastSyncedAt: string | null
  enabled: boolean
}

/** A plugin available for installation from a repository. */
export interface AvailablePlugin {
  id: string
  name: string
  description: string
  version: string
  author: string
  tags: string[]
  pluginPath: string
  homepage: string | null
  repositoryId: string
  repositoryName: string
}

/** A plugin currently installed on the system. */
export interface InstalledPlugin {
  id: string
  name: string
  version: string
  source: string
  repositoryId: string | null
  description: string | null
  installedAt: string
  pluginPath: string
  enabled: boolean
  updateAvailable?: boolean
  availableVersion?: string | null
}

export interface PluginUpdateInfo {
  pluginId: string
  pluginName: string
  installedVersion: string | null
  availableVersion: string | null
  repositoryId: string
}

export interface PluginConfigSchema {
  pluginId: string
  section: string
  description: string
  properties: PluginConfigPropertySchema[]
}

export interface PluginConfigPropertySchema {
  name: string
  type: string
  description: string
  defaultValue: string
  isSensitive: boolean
}
