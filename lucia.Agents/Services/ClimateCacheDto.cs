using System.Collections.Generic;

namespace lucia.Agents.Services;

internal sealed record ClimateCacheDto(
    string EntityId,
    string FriendlyName,
    string? Area,
    List<string> HvacModes,
    List<string> FanModes,
    List<string> SwingModes,
    List<string> PresetModes,
    double? MinTemp,
    double? MaxTemp,
    double? MinHumidity,
    double? MaxHumidity,
    int SupportedFeatures);
