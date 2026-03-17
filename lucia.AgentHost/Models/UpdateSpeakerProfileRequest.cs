namespace lucia.AgentHost.Models;

public sealed record UpdateSpeakerProfileRequest
{
    public string? Name { get; init; }
    public bool? IsAuthorized { get; init; }
    public bool? IsProvisional { get; init; }
}
