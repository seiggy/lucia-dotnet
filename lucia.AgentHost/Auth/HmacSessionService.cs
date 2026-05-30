using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using lucia.Agents.Abstractions;
using lucia.Agents.Auth;
using Microsoft.Extensions.Options;

namespace lucia.AgentHost.Auth;

/// <summary>
/// HMAC-SHA256 signed session cookie service.
/// <para>
/// The signing key is loaded from <c>Auth:SessionSigningKey</c> configuration. When absent,
/// <see cref="InitializeAsync"/> generates a 64-byte key and persists it to the durable config
/// store so it survives restarts and is shared across instances.
/// </para>
/// <remarks>
/// <b>Multi-instance note:</b> key generation uses a read-then-write strategy against the shared
/// config store. On first boot with multiple simultaneously-starting instances there is a narrow
/// race where two instances could each write a different key. After the first restart all instances
/// will read the same persisted key. For strict multi-instance safety, pre-provision
/// <c>Auth:SessionSigningKey</c> as an environment variable or secret before deploying.
/// </remarks>
/// </summary>
public sealed partial class HmacSessionService : ISessionService
{
    private readonly IConfiguration _configuration;
    private readonly IConfigStoreWriter _configStoreWriter;
    private readonly AuthOptions _options;
    private readonly ILogger<HmacSessionService> _logger;
    private volatile byte[]? _signingKey;
    private readonly SemaphoreSlim _initSemaphore = new(1, 1);
    private readonly object _fallbackLock = new();

    public HmacSessionService(
        IConfiguration configuration,
        IOptions<AuthOptions> options,
        ILogger<HmacSessionService> logger,
        IConfigStoreWriter configStoreWriter)
    {
        _configuration = configuration;
        _options = options.Value;
        _logger = logger;
        _configStoreWriter = configStoreWriter;
    }

    /// <summary>
    /// Loads the signing key from configuration or the durable config store, generating and
    /// persisting a new one if it does not yet exist. Must be called once at application startup
    /// before the service begins handling requests.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_signingKey is not null)
            return;

        await _initSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Double-check after acquiring the semaphore
            if (_signingKey is not null)
                return;

            // 1. Fast path: key already present in IConfiguration (env var / appsettings / config provider)
            var configValue = _configuration["Auth:SessionSigningKey"];
            if (!string.IsNullOrWhiteSpace(configValue))
            {
                _signingKey = Convert.FromBase64String(configValue);
                LogSigningKeyLoaded(_logger);
                return;
            }

            // 2. Check the durable config store directly (bypasses the provider's 5-second poll lag)
            var storedValue = await _configStoreWriter
                .GetAsync("Auth:SessionSigningKey", cancellationToken)
                .ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(storedValue))
            {
                _signingKey = Convert.FromBase64String(storedValue);
                LogSigningKeyLoadedFromStore(_logger);
                return;
            }

            // 3. Not found anywhere — generate, persist, and use
            var newKey = RandomNumberGenerator.GetBytes(64);
            var newKeyBase64 = Convert.ToBase64String(newKey);

            await _configStoreWriter.SetAsync(
                "Auth:SessionSigningKey",
                newKeyBase64,
                "system-init",
                isSensitive: true,
                cancellationToken).ConfigureAwait(false);

            _signingKey = newKey;
            LogSigningKeyGenerated(_logger);
        }
        finally
        {
            _initSemaphore.Release();
        }
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
        var key = _signingKey;
        if (key is not null)
            return key;

        // Fallback: InitializeAsync was not called before this point.
        // Generate an ephemeral key so the service can still operate, but warn loudly —
        // this key will not survive a restart or be shared across instances.
        lock (_fallbackLock)
        {
            key = _signingKey;
            if (key is not null)
                return key;

            var configValue = _configuration["Auth:SessionSigningKey"];
            if (!string.IsNullOrWhiteSpace(configValue))
            {
                _signingKey = Convert.FromBase64String(configValue);
                return _signingKey;
            }

            LogEphemeralSigningKeyFallback(_logger);
            _signingKey = RandomNumberGenerator.GetBytes(64);
            return _signingKey;
        }
    }

    private static string ComputeSignature(string payload, byte[] key)
    {
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var hash = HMACSHA256.HashData(key, payloadBytes);
        return Convert.ToBase64String(hash).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "HMAC session signing key loaded from configuration.")]
    private static partial void LogSigningKeyLoaded(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "HMAC session signing key loaded from durable config store.")]
    private static partial void LogSigningKeyLoadedFromStore(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Generated new HMAC session signing key and persisted it to the config store.")]
    private static partial void LogSigningKeyGenerated(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "HmacSessionService.InitializeAsync was not called before the service was used. " +
        "An ephemeral signing key will be used — sessions will be invalidated on restart and will not " +
        "validate across multiple instances. Ensure InitializeAsync is called at application startup.")]
    private static partial void LogEphemeralSigningKeyFallback(ILogger logger);

    private sealed class SessionPayload
    {
        public string KeyId { get; set; } = default!;
        public string KeyName { get; set; } = default!;
        public long IssuedAt { get; set; }
        public long ExpiresAt { get; set; }
    }
}
