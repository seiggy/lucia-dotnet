namespace lucia.Agents.Orchestration.Models;

/// <summary>
/// Lightweight speaker and device context passed through the orchestrator pipeline
/// so the personality prompt engine can reference the speaker's identity and location.
/// </summary>
public sealed record SpeakerContext
{
    /// <summary>
    /// Speaker name identified by voice verification (e.g., "Zack").
    /// </summary>
    public string? SpeakerId { get; init; }

    /// <summary>
    /// The area where the request originated (e.g., "Kitchen").
    /// </summary>
    public string? DeviceArea { get; init; }

    /// <summary>
    /// The physical location context (e.g., "Home").
    /// </summary>
    public string? Location { get; init; }
}
