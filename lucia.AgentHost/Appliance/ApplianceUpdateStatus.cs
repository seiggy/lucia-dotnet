namespace lucia.AgentHost.Appliance;

public sealed record ApplianceUpdateStatus(
    string CurrentLuciaVersion,
    string CurrentOsVersion,
    string? LatestVersion,
    bool ManifestAvailable,
    bool Compatible,
    bool LuciaUpdateAvailable,
    bool OsUpdateAvailable,
    string? ReleaseUrl,
    string? Message);
