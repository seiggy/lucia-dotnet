using lucia.Agents.Abstractions;
using lucia.Agents.Services;
using Microsoft.Extensions.AI;

namespace lucia.Agents.Models.HomeAssistant;

/// <summary>
/// Represents a cached light entity with search capabilities
/// </summary>
public sealed class LightEntity : IMatchableEntity
{
    public string EntityId { get; set; } = string.Empty;
    public string FriendlyName { get; set; } = string.Empty;
    public SupportedColorModes SupportedColorModes { get; set; } = SupportedColorModes.None;
    public Embedding<float>? NameEmbedding { get; set; }
    public string? Area { get; set; }

    /// <inheritdoc />
    public string[] PhoneticKeys { get; set; } = [];

    /// <summary>
    /// True if this is a switch entity (switch.* vs light.*)
    /// </summary>
    public bool IsSwitch => EntityId.StartsWith("switch.");

    // ── IMatchableEntity ────────────────────────────────────────

    string IMatchableEntity.MatchableName => FriendlyName;
    Embedding<float>? IMatchableEntity.NameEmbedding => NameEmbedding;
}