using System.Security.Claims;

namespace lucia.AgentHost.Auth;

/// <summary>
/// Manages session cookie creation and validation using HMAC-SHA256 signing.
/// </summary>
public interface ISessionService
{
    /// <summary>
    /// Creates a signed session cookie value for the given API key entry.
    /// </summary>
    string CreateSession(string keyId, string keyName);

    /// <summary>
    /// Validates a session cookie value and returns claims if valid, null if invalid/expired.
    /// </summary>
    IEnumerable<Claim>? ValidateSession(string cookieValue);
}
