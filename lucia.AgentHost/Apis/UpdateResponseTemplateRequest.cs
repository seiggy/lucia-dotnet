namespace lucia.AgentHost.Apis;

/// <summary>Request body for updating an existing response template.</summary>
public sealed class UpdateResponseTemplateRequest
{
    public string? SkillId { get; init; }
    public string? Action { get; init; }
    public string[]? Templates { get; init; }
}
