using lucia.Agents.Configuration;

namespace lucia.AgentHost.Apis;

/// <summary>
/// Partial update payload for agent definitions.
/// Null properties indicate the client did not send that field.
/// </summary>
public sealed record PatchAgentDefinitionRequest
{
    public string? Name { get; init; }

    public string? DisplayName { get; init; }

    public string? Description { get; init; }

    public string? Instructions { get; init; }

    public List<AgentToolReference>? Tools { get; init; }

    public string? ModelConnectionName { get; init; }

    public string? EmbeddingProviderName { get; init; }

    public bool? Enabled { get; init; }
}
