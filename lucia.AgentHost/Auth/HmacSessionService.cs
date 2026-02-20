using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using lucia.Agents.Auth;
using Microsoft.Extensions.Options;

namespace lucia.AgentHost.Auth;

/// <summary>
/// HMAC-SHA256 signed session cookie service. The signing key is stored in MongoDB config store
/// and auto-generated on first use.
/// </summary>
public sealed class HmacSessionService : ISessionService
{
    private readonly IConfiguration _configuration;
    private readonly AuthOptions _options;
    private readonly ILogger<HmacSessionService> _logger;
    private byte[]? _signingKey;

    public HmacSessionService(
        IConfiguration configuration,
        IOptions<AuthOptions> options,
        ILogger<HmacSessionService> logger)
    {
        _configuration = configuration;
        _options = options.Value;
        _logger = logger;
    }

    public string CreateSession(string keyId, string keyName)
    {
        var signingKey = GetOrCreateSigningKey();

        var payload = new SessionPayload
        {
            KeyId = keyId,
            KeyName = keyName,
            IssuedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ExpiresAt = DateTimeOffset.UtcNow.Add(_options.SessionLifetime).ToUnixTimeSeconds(),
        };

        var payloadJson = JsonSerializer.Serialize(payload);
        var payloadBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(payloadJson));
        var signature = ComputeSignature(payloadBase64, signingKey);

        return $"{payloadBase64}.{signature}";
    }

    public IEnumerable<Claim>? ValidateSession(string cookieValue)
    {
        try
        {
            var parts = cookieValue.Split('.');
            if (parts.Length != 2)
            {
                return null;
            }

            var payloadBase64 = parts[0];
            var signature = parts[1];

            var signingKey = GetOrCreateSigningKey();
            var expectedSignature = ComputeSignature(payloadBase64, signingKey);

            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(signature),
                    Encoding.UTF8.GetBytes(expectedSignature)))
            {
                return null;
            }

            var payloadJson = Encoding.UTF8.GetString(Convert.FromBase64String(payloadBase64));
            var payload = JsonSerializer.Deserialize<SessionPayload>(payloadJson);

            if (payload is null)
            {
                return null;
            }

            // Check expiration
            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > payload.ExpiresAt)
            {
                return null;
            }

            return
            [
                new Claim(ClaimTypes.NameIdentifier, payload.KeyId),
                new Claim(ClaimTypes.Name, payload.KeyName),
                new Claim("auth_method", "session"),
            ];
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Session cookie validation failed");
            return null;
        }
    }

    private byte[] GetOrCreateSigningKey()
    {
        if (_signingKey is not null)
        {
            return _signingKey;
        }

        var storedKey = _configuration["Auth:SessionSigningKey"];
        if (!string.IsNullOrWhiteSpace(storedKey))
        {
            _signingKey = Convert.FromBase64String(storedKey);
            return _signingKey;
        }

        // Auto-generate and store (ConfigSeeder or first-run will persist this)
        _signingKey = RandomNumberGenerator.GetBytes(64);
        _logger.LogInformation("Generated new session signing key — it will be persisted to config store during setup");
        return _signingKey;
    }

    private static string ComputeSignature(string payload, byte[] key)
    {
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var hash = HMACSHA256.HashData(key, payloadBytes);
        return Convert.ToBase64String(hash).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    private sealed class SessionPayload
    {
        public string KeyId { get; set; } = default!;
        public string KeyName { get; set; } = default!;
        public long IssuedAt { get; set; }
        public long ExpiresAt { get; set; }
    }
}
