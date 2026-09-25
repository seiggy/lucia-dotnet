using System.Text.Json.Serialization;

namespace lucia.AgentHost.Conversation.Models;

/// <summary>
/// Device and session context from the Home Assistant voice pipeline.
/// Extracted from the request body so the command parser can use it for entity resolution
/// and the LLM fallback can reconstruct it as a system prompt.
/// </summary>
public sealed record ConversationContext
{
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("conversationId")]
    public string? ConversationId { get; init; }

    [JsonPropertyName("deviceId")]
    public string? DeviceId { get; init; }

    [JsonPropertyName("deviceArea")]
    public string? DeviceArea { get; init; }

    [JsonPropertyName("deviceType")]
    public string? DeviceType { get; init; }

    [JsonPropertyName("userId")]
    public string? UserId { get; init; }

    /// <summary>
    /// Speaker name identified by the Wyoming voice platform's speaker verification.
    /// Populated server-side by stripping the <c>&lt;Name /&gt;</c> tag from the transcript.
    /// This display name is not a trusted identity for memory access.
    /// </summary>
    [JsonPropertyName("speakerId")]
    public string? SpeakerId { get; init; }

    [JsonIgnore]
    public string? EnrolledProfileId { get; init; }

    [JsonIgnore]
    public bool IsVoiceRequest { get; init; }

    /// <summary>
    /// Set server-side when the request is authenticated by a dashboard session cookie
    /// rather than an API key, so a simulated Home Assistant device ID is not treated as speech.
    /// </summary>
    [JsonIgnore]
    public bool IsDashboardSession { get; init; }

    [JsonPropertyName("location")]
    public string? Location { get; init; }
}
