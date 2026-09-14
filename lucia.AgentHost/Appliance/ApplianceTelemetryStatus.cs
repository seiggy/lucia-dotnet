namespace lucia.AgentHost.Appliance;

public sealed record ApplianceTelemetryStatus(
    bool Configured,
    bool Enabled,
    string Endpoint,
    bool InsecureSkipVerify,
    bool HasAuthorization);
