using System.Text.Json.Serialization;

namespace lucia.HomeAssistant.Models;

/// <summary>
/// Home Assistant system configuration.
/// </summary>
public sealed record ConfigResponse
{
    [JsonPropertyName("components")]
    public required IReadOnlyList<string> Components { get; init; }

    [JsonPropertyName("config_dir")]
    public required string ConfigDir { get; init; }

    [JsonPropertyName("elevation")]
    public required double Elevation { get; init; }

    [JsonPropertyName("latitude")]
    public required double Latitude { get; init; }

    [JsonPropertyName("longitude")]
    public required double Longitude { get; init; }

    [JsonPropertyName("location_name")]
    public required string LocationName { get; init; }

    [JsonPropertyName("time_zone")]
    public required string TimeZone { get; init; }

    [JsonPropertyName("unit_system")]
    public required UnitSystemInfo UnitSystem { get; init; }

    [JsonPropertyName("version")]
    public required string Version { get; init; }

    [JsonPropertyName("whitelist_external_dirs")]
    public IReadOnlyList<string>? WhitelistExternalDirs { get; init; }
}
