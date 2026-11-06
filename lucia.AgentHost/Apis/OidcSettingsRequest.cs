namespace lucia.AgentHost.Apis;

public sealed record OidcSettingsRequest(bool Enabled, string? Authority, string? Audience, bool RequireHttpsMetadata = true);
