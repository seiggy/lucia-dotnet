namespace lucia.AgentHost.Auth;

/// <summary>
/// Optional OpenID Connect bearer authentication for family-member apps. Tokens are accepted
/// only for identities an administrator has linked to a family member.
/// </summary>
public sealed class OidcAuthOptions
{
    public const string SectionName = "Auth:Oidc";

    public bool Enabled { get; set; }

    /// <summary>
    /// Issuer URL used for discovery, e.g. https://login.microsoftonline.com/{tenant}/v2.0.
    /// </summary>
    public string? Authority { get; set; }

    /// <summary>
    /// Expected <c>aud</c> claim of access tokens sent by the apps.
    /// </summary>
    public string? Audience { get; set; }

    public bool RequireHttpsMetadata { get; set; } = true;

    public bool IsConfigured =>
        Enabled && !string.IsNullOrWhiteSpace(Authority) && !string.IsNullOrWhiteSpace(Audience);
}
