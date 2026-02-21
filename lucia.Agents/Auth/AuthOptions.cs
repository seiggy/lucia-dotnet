namespace lucia.Agents.Auth;

/// <summary>
/// Configuration options for API key authentication and session management.
/// </summary>
public sealed class AuthOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "Auth";

    /// <summary>
    /// Session cookie name.
    /// </summary>
    public string CookieName { get; set; } = ".lucia.session";

    /// <summary>
    /// Session cookie lifetime. Default 7 days.
    /// </summary>
    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// API key prefix used to identify Lucia keys.
    /// </summary>
    public const string KeyPrefix = "lk_";

    /// <summary>
    /// Length of the random portion of the key in bytes (before base64url encoding).
    /// 32 bytes = 256 bits of entropy.
    /// </summary>
    public const int KeyLengthBytes = 32;

    /// <summary>
    /// HTTP header name for API key authentication.
    /// </summary>
    public const string ApiKeyHeaderName = "X-API-Key";

    /// <summary>
    /// Authentication scheme name.
    /// </summary>
    public const string AuthenticationScheme = "ApiKey";
}
