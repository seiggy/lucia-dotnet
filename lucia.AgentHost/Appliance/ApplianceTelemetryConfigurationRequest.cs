namespace lucia.AgentHost.Appliance;

public sealed record ApplianceTelemetryConfigurationRequest(
    bool Enabled,
    string Endpoint,
    string? Username,
    string? Password,
    bool ClearAuthorization,
    bool InsecureSkipVerify);
