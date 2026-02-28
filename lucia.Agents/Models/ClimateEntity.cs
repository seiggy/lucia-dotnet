using lucia.Agents.Services;
using Microsoft.Extensions.AI;

namespace lucia.Agents.Models;

/// <summary>
/// Represents a cached climate (HVAC) entity with search capabilities.
/// Mirrors Home Assistant climate entity attributes.
/// </summary>
public sealed class ClimateEntity : IMatchableEntity
{
    public string EntityId { get; set; } = string.Empty;
    public string FriendlyName { get; set; } = string.Empty;
    public Embedding<float>? NameEmbedding { get; set; }
    public string? Area { get; set; }

    /// <inheritdoc />
    public string[] PhoneticKeys { get; set; } = [];

    // HVAC capabilities
    public List<string> HvacModes { get; set; } = [];
    public List<string> FanModes { get; set; } = [];
    public List<string> SwingModes { get; set; } = [];
    public List<string> PresetModes { get; set; } = [];

    // Temperature range
    public double? MinTemp { get; set; }
    public double? MaxTemp { get; set; }

    // Humidity range
    public double? MinHumidity { get; set; }
    public double? MaxHumidity { get; set; }

    /// <summary>
    /// Bitmask of supported features from Home Assistant.
    /// 1=TargetTemperature, 2=TargetTemperatureRange, 4=TargetHumidity,
    /// 8=FanMode, 16=PresetMode, 32=SwingMode, 128=TurnOff, 256=TurnOn
    /// </summary>
    public int SupportedFeatures { get; set; }

    public bool SupportsTargetTemperature => (SupportedFeatures & 1) != 0;
    public bool SupportsTemperatureRange => (SupportedFeatures & 2) != 0;
    public bool SupportsHumidity => (SupportedFeatures & 4) != 0;
    public bool SupportsFanMode => (SupportedFeatures & 8) != 0;

    // ── IMatchableEntity ────────────────────────────────────────

    string IMatchableEntity.MatchableName => FriendlyName;
    Embedding<float>? IMatchableEntity.NameEmbedding => NameEmbedding;
}
