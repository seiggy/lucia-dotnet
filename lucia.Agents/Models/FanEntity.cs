using Microsoft.Extensions.AI;

namespace lucia.Agents.Models;

/// <summary>
/// Represents a cached fan entity with search capabilities.
/// Mirrors Home Assistant fan entity attributes.
/// </summary>
public sealed class FanEntity
{
    public string EntityId { get; set; } = string.Empty;
    public string FriendlyName { get; set; } = string.Empty;
    public Embedding<float> NameEmbedding { get; set; } = null!;
    public string? Area { get; set; }

    /// <summary>
    /// Step size for speed percentage changes (e.g., 1 = 1% increments, 10 = 10% increments)
    /// </summary>
    public int PercentageStep { get; set; }

    /// <summary>
    /// Available preset modes (e.g., "auto", "nature", "sleep")
    /// </summary>
    public List<string> PresetModes { get; set; } = [];

    /// <summary>
    /// Bitmask of supported features from Home Assistant.
    /// 1=SetSpeed, 2=Oscillate, 4=Direction, 8=PresetMode, 16=TurnOff, 32=TurnOn
    /// </summary>
    public int SupportedFeatures { get; set; }

    public bool SupportsSpeed => (SupportedFeatures & 1) != 0;
    public bool SupportsOscillate => (SupportedFeatures & 2) != 0;
    public bool SupportsDirection => (SupportedFeatures & 4) != 0;
    public bool SupportsPresetMode => (SupportedFeatures & 8) != 0;
}
