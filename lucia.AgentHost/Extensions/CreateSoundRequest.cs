namespace lucia.AgentHost.Extensions;

/// <summary>
/// Request body for creating a new alarm sound mapping.
/// </summary>
public sealed class CreateSoundRequest
{
    public string? Name { get; set; }
    public string? MediaSourceUri { get; set; }
    public bool? UploadedViaLucia { get; set; }
    public bool? IsDefault { get; set; }
}
