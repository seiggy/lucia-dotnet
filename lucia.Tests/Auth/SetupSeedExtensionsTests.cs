using FakeItEasy;
using lucia.Agents.Abstractions;
using lucia.Agents.Extensions;
using lucia.Data.Sqlite;
using lucia.Tests.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests.Auth;

/// <summary>
/// Tests for <see cref="SetupSeedExtensions.SeedSetupFromEnvAsync"/>, specifically the
/// Dashboard API key override/reset semantics.
/// Uses a real <see cref="SqliteApiKeyService"/> backed by an in-memory SQLite database
/// so that hash-comparison and insert idempotency are exercised end-to-end.
/// </summary>
public sealed class SetupSeedExtensionsTests : IDisposable
{
    private readonly SqliteTestHelper _db;
    private readonly SqliteApiKeyService _apiKeyService;
    private readonly IConfigStoreWriter _configStore;
    private readonly ILogger _logger;

    // A valid plaintext key long enough to pass the length guard (>= 16 chars)
    private const string EnvKey = "lk_test_envkey_abcdef1234567890";
    private const string OtherKey = "lk_test_other_key_abcdef1234567890";

    public SetupSeedExtensionsTests()
    {
        _db = new SqliteTestHelper();
        _apiKeyService = new SqliteApiKeyService(_db.ConnectionFactory, A.Fake<ILogger<SqliteApiKeyService>>());
        _configStore = A.Fake<IConfigStoreWriter>();
        _logger = NullLogger.Instance;

        // Default: GetAsync returns null so the wizard-skip gate doesn't fire
        A.CallTo(() => _configStore.GetAsync(A<string>._, A<CancellationToken>._))
            .Returns(Task.FromResult<string?>(null));
    }

    [Fact]
    public async Task SeedSetupFromEnvAsync_NoExistingKey_CreatesAndValidates()
    {
        var config = BuildConfig(EnvKey);

        await _apiKeyService.SeedSetupFromEnvAsync(_configStore, config, _logger);

        // The env key must now validate successfully
        var entry = await _apiKeyService.ValidateKeyAsync(EnvKey);
        Assert.NotNull(entry);
        Assert.Equal("Dashboard", entry.Name);
        Assert.False(entry.IsRevoked);
    }

    [Fact]
    public async Task SeedSetupFromEnvAsync_ExistingKeyWithDifferentPlaintext_RevokedAndEnvKeyValidates()
    {
        // Seed an existing Dashboard key using a different plaintext
        await _apiKeyService.CreateKeyFromPlaintextAsync("Dashboard", OtherKey);

        // Confirm the old key validates before the reset
        var oldEntry = await _apiKeyService.ValidateKeyAsync(OtherKey);
        Assert.NotNull(oldEntry);

        // Now seed with a new env value
        var config = BuildConfig(EnvKey);
        await _apiKeyService.SeedSetupFromEnvAsync(_configStore, config, _logger);

        // Old key must no longer validate
        var revokedEntry = await _apiKeyService.ValidateKeyAsync(OtherKey);
        Assert.Null(revokedEntry);

        // New env key must validate
        var newEntry = await _apiKeyService.ValidateKeyAsync(EnvKey);
        Assert.NotNull(newEntry);
        Assert.Equal("Dashboard", newEntry.Name);
    }

    [Fact]
    public async Task SeedSetupFromEnvAsync_ExistingKeyAlreadyMatchingEnv_NoOp()
    {
        // Seed the env key once
        await _apiKeyService.CreateKeyFromPlaintextAsync("Dashboard", EnvKey);

        // Confirm key exists
        var keys = await _apiKeyService.ListKeysAsync();
        var before = keys.Where(k => k.Name == "Dashboard" && !k.IsRevoked).ToList();
        Assert.Single(before);

        // Re-seed with the same env value
        var config = BuildConfig(EnvKey);
        await _apiKeyService.SeedSetupFromEnvAsync(_configStore, config, _logger);

        // Key count must not have changed — still exactly one active Dashboard key
        var keysAfter = await _apiKeyService.ListKeysAsync();
        var active = keysAfter.Where(k => k.Name == "Dashboard" && !k.IsRevoked).ToList();
        Assert.Single(active);

        // The env key still validates
        var entry = await _apiKeyService.ValidateKeyAsync(EnvKey);
        Assert.NotNull(entry);
    }

    [Fact]
    public async Task SeedSetupFromEnvAsync_BlankEnvValue_NoKeyCreated()
    {
        var config = BuildConfig("   "); // whitespace only

        await _apiKeyService.SeedSetupFromEnvAsync(_configStore, config, _logger);

        var keys = await _apiKeyService.ListKeysAsync();
        Assert.DoesNotContain(keys, k => k.Name == "Dashboard");
    }

    [Fact]
    public async Task SeedSetupFromEnvAsync_MissingEnvValue_NoKeyCreated()
    {
        var config = BuildConfig(null); // not provided

        await _apiKeyService.SeedSetupFromEnvAsync(_configStore, config, _logger);

        var keys = await _apiKeyService.ListKeysAsync();
        Assert.DoesNotContain(keys, k => k.Name == "Dashboard");
    }

    [Fact]
    public async Task SeedSetupFromEnvAsync_Override_IsIdempotent_WhenCalledTwice()
    {
        // Seed an old key, then call the env seed twice in sequence (simulates dual-startup)
        await _apiKeyService.CreateKeyFromPlaintextAsync("Dashboard", OtherKey);

        var config = BuildConfig(EnvKey);
        await _apiKeyService.SeedSetupFromEnvAsync(_configStore, config, _logger);
        await _apiKeyService.SeedSetupFromEnvAsync(_configStore, config, _logger); // second call — must not throw or duplicate

        // Exactly one active Dashboard key matching the env value
        var keys = await _apiKeyService.ListKeysAsync();
        var active = keys.Where(k => k.Name == "Dashboard" && !k.IsRevoked).ToList();
        Assert.Single(active);

        var entry = await _apiKeyService.ValidateKeyAsync(EnvKey);
        Assert.NotNull(entry);
    }

    public void Dispose() => _db.Dispose();

    private static IConfiguration BuildConfig(string? dashboardApiKey)
    {
        var values = new Dictionary<string, string?>();
        if (dashboardApiKey is not null)
            values["DASHBOARD_API_KEY"] = dashboardApiKey;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
