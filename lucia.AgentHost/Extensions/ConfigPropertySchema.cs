namespace lucia.AgentHost.Extensions;

/// <summary>
/// Schema for a single configuration property.
/// </summary>
public sealed record ConfigPropertySchema(
    string Name,
    string Type,
    string Description,
    string DefaultValue,
    bool IsSensitive = false);
