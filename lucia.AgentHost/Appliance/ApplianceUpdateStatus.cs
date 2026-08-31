namespace lucia.AgentHost.Appliance;

public sealed record ApplianceUpdateStatus(
    string CurrentLuciaVersion,
    string CurrentOsVersion,
    string? LatestLuciaVersion,
    string? LatestOsVersion,
    bool ManifestAvailable,
    bool Compatible,
    bool LuciaNewerDiscovered,
    bool OsNewerDiscovered,
    bool LuciaUpdateAvailable,
    bool OsUpdateAvailable,
    string? ReleaseUrl,
    string? Message);
