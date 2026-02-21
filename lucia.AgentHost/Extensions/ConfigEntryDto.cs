namespace lucia.AgentHost.Extensions;

/// <summary>
/// Configuration entry as returned by the API (with sensitive value masking).
/// </summary>
public sealed class ConfigEntryDto
{
    public string Key { get; set; } = default!;
    public string? Value { get; set; }
    public bool IsSensitive { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = default!;
}
