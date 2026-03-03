using System.Collections.Immutable;
using lucia.Agents.Abstractions;
using lucia.HomeAssistant.Models;
using Microsoft.Extensions.AI;

namespace lucia.Agents.Models.HomeAssistant;

/// <summary>
/// Unified entity model for all Home Assistant entity types. Replaces the per-domain
/// entity types (LightEntity, FanEntity, ClimateEntity, MusicPlayerEntity) and
/// EntityLocationInfo with a single cached representation.
/// Domain-specific capabilities are exposed as nullable properties — only populated
/// for entities of the relevant domain.
/// </summary>
public sealed class HomeAssistantEntity : IMatchableEntity
{
    // ── Core identity (from EntityRegistryEntry) ────────────────

    public required string EntityId { get; init; }
    public required string FriendlyName { get; init; }
    public string Domain => EntityId.Split('.')[0];
    public IReadOnlyList<string> Aliases { get; init; } = [];
    public string? AreaId { get; init; }
    public string? Platform { get; init; }
    public SupportedFeaturesFlags SupportedFeatures { get; init; }

    // ── Agent visibility ────────────────────────────────────────

    /// <summary>
    /// Agent IDs this entity is visible to.
    /// <c>null</c> = visible to all agents (default).
    /// Empty set = excluded from all agents.
    /// Non-empty set = visible only to listed agents.
    /// Configured per-entity in the Lucia dashboard, persisted in Redis.
    /// </summary>
    public HashSet<string>? IncludeForAgent { get; set; }

    // ── IMatchableEntity ────────────────────────────────────────

    public Embedding<float>? NameEmbedding { get; set; }
    public string[] PhoneticKeys { get; set; } = [];
    public IReadOnlyList<string[]> AliasPhoneticKeys { get; set; } = [];
    string IMatchableEntity.MatchableName => FriendlyName;
    IReadOnlyList<string> IMatchableEntity.MatchableAliases => Aliases;

    // ── Light-specific ──────────────────────────────────────────

    public SupportedColorModes? ColorModes { get; init; }
    public bool IsSwitch => EntityId.StartsWith("switch.");

    // ── Fan-specific ────────────────────────────────────────────

    public int? PercentageStep { get; init; }
    public List<string>? FanPresetModes { get; init; }
    public string? ModeSelectEntityId { get; init; }

    public bool SupportsSpeed => SupportedFeatures.HasFlag((SupportedFeaturesFlags)1);
    public bool SupportsOscillate => SupportedFeatures.HasFlag((SupportedFeaturesFlags)2);
    public bool SupportsDirection => SupportedFeatures.HasFlag((SupportedFeaturesFlags)4);
    public bool SupportsFanPresetMode => SupportedFeatures.HasFlag((SupportedFeaturesFlags)8) || ModeSelectEntityId is not null;

    // ── Climate-specific ────────────────────────────────────────

    public List<string>? HvacModes { get; init; }
    public List<string>? ClimateFanModes { get; init; }
    public List<string>? SwingModes { get; init; }
    public List<string>? ClimatePresetModes { get; init; }
    public double? MinTemp { get; init; }
    public double? MaxTemp { get; init; }
    public double? MinHumidity { get; init; }
    public double? MaxHumidity { get; init; }

    public bool SupportsTargetTemperature => SupportedFeatures.HasFlag((SupportedFeaturesFlags)1);
    public bool SupportsTemperatureRange => SupportedFeatures.HasFlag((SupportedFeaturesFlags)2);
    public bool SupportsHumidity => SupportedFeatures.HasFlag((SupportedFeaturesFlags)4);
    public bool SupportsClimateFanMode => SupportedFeatures.HasFlag((SupportedFeaturesFlags)8);

    // ── Music Player-specific ───────────────────────────────────

    public string? ConfigEntryId { get; init; }
    public bool? IsSatellite { get; init; }
}
