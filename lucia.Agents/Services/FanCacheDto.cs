using System.Collections.Generic;

namespace lucia.Agents.Services;

internal sealed record FanCacheDto(
    string EntityId,
    string FriendlyName,
    string? Area,
    int PercentageStep,
    List<string> PresetModes,
    string? ModeSelectEntityId,
    int SupportedFeatures);
