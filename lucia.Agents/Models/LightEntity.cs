using Microsoft.Extensions.AI;

namespace lucia.Agents.Models;

/// <summary>
/// Represents a cached light entity with search capabilities
/// </summary>
public sealed class LightEntity
{
    public string EntityId { get; set; } = string.Empty;
    public string FriendlyName { get; set; } = string.Empty;
    public SupportedColorModes SupportedColorModes { get; set; } = SupportedColorModes.None;
    public Embedding<float> NameEmbedding { get; set; } = null!;
    public string? Area { get; set; }

    /// <summary>
    /// True if this is a switch entity (switch.* vs light.*)
    /// </summary>
    public bool IsSwitch => EntityId.StartsWith("switch.");
}

/// <summary>
/// Supported color modes for light entities
/// </summary>
[Flags]
public enum SupportedColorModes
{
    None = 0,
    Brightness = 1,
    ColorTemp = 2,
    Hs = 4  // Hue/Saturation
}