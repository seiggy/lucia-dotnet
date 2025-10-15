namespace lucia.Agents.Orchestration;

/// <summary>
/// Configuration for <see cref="RouterExecutor"/> prompt construction, retry behavior, and fallback policies.
/// </summary>
public sealed class RouterExecutorOptions
{
    public const string DefaultSystemPrompt = """
You are Lucia's RouterExecutor. Analyze smart home requests and select the most appropriate specialized agent. Always reply using JSON matching the schema with properties: agentId (string), reasoning (string), confidence (number between 0 and 1), and optional additionalAgents (array of strings).
Consider capabilities and descriptions carefully. Prefer clarifying questions when the intent is ambiguous.
""";

    public const string DefaultAgentCatalogHeader = "Available agents:";

    public const string DefaultUserPromptTemplate = """
Analyze the user's request and choose the best agent. Use the catalog below for reference.

Catalog:
{1}

User request:
{0}

Return JSON only.
""";

    public const string DefaultClarificationPromptTemplate = "I'm not sure which agent should help. Ask the user to clarify between: {0}.";
    public const string DefaultFallbackReasonTemplate = "Router fallback engaged: {0}";
    public const string DefaultClarificationAgentId = "clarification";
    public const string DefaultFallbackAgentId = "general-assistant";

    public double ConfidenceThreshold { get; set; } = 0.7;

    public int MaxAttempts { get; set; } = 2;

    public double Temperature { get; set; } = 0.1;

    public int MaxOutputTokens { get; set; } = 512;

    public string? SystemPrompt { get; set; } = DefaultSystemPrompt;

    public string? AgentCatalogHeader { get; set; } = DefaultAgentCatalogHeader;

    public string? UserPromptTemplate { get; set; } = DefaultUserPromptTemplate;

    public string? ClarificationPromptTemplate { get; set; } = DefaultClarificationPromptTemplate;

    public string? FallbackReasonTemplate { get; set; } = DefaultFallbackReasonTemplate;

    public string? ClarificationAgentId { get; set; } = DefaultClarificationAgentId;

    public string? FallbackAgentId { get; set; } = DefaultFallbackAgentId;

    public bool IncludeAgentCapabilities { get; set; } = true;

    public bool IncludeSkillExamples { get; set; } = false;
}
