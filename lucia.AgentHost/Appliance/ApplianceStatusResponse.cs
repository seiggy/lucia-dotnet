namespace lucia.AgentHost.Appliance;

public sealed record ApplianceStatusResponse(
    string Hostname,
    string Architecture,
    string Board,
    string LuciaVersion,
    bool RebootRequired,
    ApplianceNetworkStatus Network,
    ApplianceOsStatus Os,
    IReadOnlyList<ApplianceServiceStatus> Services);
