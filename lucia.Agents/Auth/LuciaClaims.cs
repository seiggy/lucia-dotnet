namespace lucia.Agents.Auth;

/// <summary>
/// Claim types Lucia adds to authenticated principals.
/// </summary>
public static class LuciaClaims
{
    /// <summary>
    /// API key scope, one claim per scope. Session cookies carry <see cref="ApiKeyScopes.Full"/>.
    /// </summary>
    public const string Scope = "lucia:scope";

    public const string MemberId = "lucia:member_id";

    public const string MemberRole = "lucia:member_role";
}
