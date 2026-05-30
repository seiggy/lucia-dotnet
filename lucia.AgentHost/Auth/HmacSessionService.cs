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
/// </summary>
/// <remarks>
/// <b>Multi-instance note:</b> key generation uses a read-then-write strategy against the shared
/// config store. On first boot with multiple simultaneously-starting instances there is a narrow
/// race where two instances could each write a different key. After the first restart all instances
/// will read the same persisted key. For strict multi-instance safety, pre-provision
/// <c>Auth:SessionSigningKey</c> as an environment variable or secret before deploying.
/// </remarks>
public sealed partial class HmacSessionService : ISessionService, IAsyncInitializable
{
    private readonly IConfiguration _configuration;
    private readonly IConfigStoreWriter _configStoreWriter;
    private readonly AuthOptions _options;
    private readonly ILogger<HmacSessionService> _logger;
    private volatile byte[]? _signingKey;
    private readonly SemaphoreSlim _initSemaphore = new(1, 1);

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
                _signingKey = DecodeSigningKey(configValue, "IConfiguration (Auth:SessionSigningKey)");
                LogSigningKeyLoaded(_logger);
                return;
            }

            // 2. Check the durable config store directly (bypasses the provider's 5-second poll lag)
            var storedValue = await _configStoreWriter
                .GetAsync("Auth:SessionSigningKey", cancellationToken)
                .ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(storedValue))
            {
                _signingKey = DecodeSigningKey(storedValue, "durable config store (Auth:SessionSigningKey)");
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

    /// <summary>
    /// Returns the signing key.
    /// <para>
    /// Contract: <see cref="InitializeAsync"/> MUST be called at application startup before any
    /// sessions are created or validated. This service does not generate ephemeral fallback keys —
    /// a missing initialization is a programming error that surfaces as an
    /// <see cref="InvalidOperationException"/> rather than silently issuing unrecoverable sessions.
    /// </para>
    /// </summary>
    private byte[] GetOrCreateSigningKey() =>
        _signingKey ?? throw new InvalidOperationException(
            "HmacSessionService has not been initialized. Call InitializeAsync at application " +
            "startup before any sessions are created or validated.");

    /// <summary>
    /// Decodes a base64 signing key from the given source, validating that it is well-formed
    /// base64 and exactly 64 bytes. Throws <see cref="InvalidOperationException"/> with an
    /// actionable message on failure.
    /// </summary>
    private byte[] DecodeSigningKey(string base64Value, string source)
    {
        byte[] key;
        try
        {
            key = Convert.FromBase64String(base64Value);
        }
        catch (FormatException)
        {
            LogInvalidBase64SigningKey(_logger, source);
            throw new InvalidOperationException(
                $"The HMAC session signing key from '{source}' is not valid base64. " +
                "Remove or correct the 'Auth:SessionSigningKey' entry and restart the host.");
        }

        if (key.Length != 64)
        {
            LogInvalidSigningKeyLength(_logger, source, key.Length);
            throw new InvalidOperationException(
                $"The HMAC session signing key from '{source}' decoded to {key.Length} bytes " +
                "but exactly 64 bytes are required. Remove or correct the 'Auth:SessionSigningKey' " +
                "entry and restart the host.");
        }

        return key;
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

    [LoggerMessage(Level = LogLevel.Error,
        Message = "HMAC session signing key from {Source} is not valid base64. " +
            "Remove or correct the 'Auth:SessionSigningKey' entry in {Source} and restart the host.")]
    private static partial void LogInvalidBase64SigningKey(ILogger logger, string source);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "HMAC session signing key from {Source} decoded to {ActualLength} bytes but " +
            "exactly 64 bytes are required. Remove or correct the 'Auth:SessionSigningKey' entry " +
            "in {Source} and restart the host.")]
    private static partial void LogInvalidSigningKeyLength(ILogger logger, string source, int actualLength);

    private sealed class SessionPayload
    {
        public string KeyId { get; set; } = default!;
        public string KeyName { get; set; } = default!;
        public long IssuedAt { get; set; }
        public long ExpiresAt { get; set; }
    }
}
