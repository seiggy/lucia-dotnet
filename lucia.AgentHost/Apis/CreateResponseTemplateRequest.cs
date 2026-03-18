namespace lucia.AgentHost.Apis;

/// <summary>Request body for creating a new response template.</summary>
public sealed class CreateResponseTemplateRequest
{
    public required string SkillId { get; init; }
    public required string Action { get; init; }
    public required string[] Templates { get; init; }
}
