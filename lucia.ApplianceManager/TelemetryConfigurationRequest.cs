namespace lucia.ApplianceManager;

public sealed record TelemetryConfigurationRequest(
    bool Enabled,
    string Endpoint,
    string? Username,
    string? Password,
    bool ClearAuthorization,
    bool InsecureSkipVerify);
