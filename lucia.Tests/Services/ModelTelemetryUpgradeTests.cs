using FakeItEasy;
using lucia.Agents.Configuration;
using lucia.Data.Sqlite;
using lucia.Tests.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace lucia.Tests.Services;

/// <summary>
/// Existing installs keep model content telemetry through an upgrade, because
/// <see cref="ConfigSeeder"/> writes the shipped default into the config store
/// only when the key is missing.
/// </summary>
public sealed class ModelTelemetryUpgradeTests : IDisposable
{
    private const string ContentKey = "ModelTelemetry:IncludeModelContent";

    private readonly SqliteTestHelper _db = new();
    private readonly SqliteConfigStoreWriter _store;

    public ModelTelemetryUpgradeTests()
    {
        _store = new SqliteConfigStoreWriter(_db.ConnectionFactory);
    }

    [Fact]
    public async Task ExistingInstall_UpgradeSeedsContentOn_AndLeavesObservabilityOutOfTheStore()
    {
        await _store.SetAsync("HomeAssistant:BaseUrl", "http://ha.local:8123", "setup-wizard");

        await SeedAsync(new Dictionary<string, string?>
        {
            [ContentKey] = "true",
            ["Observability:Mode"] = "Trace",
            ["Observability:OtlpHeaders"] = "Authorization=Bearer synthetic"
        });

        Assert.Equal("true", await _store.GetAsync(ContentKey));
        // Mode and OTLP settings come from the environment and must stay changeable there.
        Assert.DoesNotContain(await _store.GetAllKeysAsync(), key => key.StartsWith("Observability:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExistingInstall_KeepsAnExplicitChoiceAcrossUpgrades()
    {
        await _store.SetAsync(ContentKey, "false", "setup-wizard");

        await SeedAsync(new Dictionary<string, string?> { [ContentKey] = "true" });

        Assert.Equal("false", await _store.GetAsync(ContentKey));
    }

    [Fact]
    public async Task FreshInstall_SeedsTheConfiguredDefault()
    {
        await SeedAsync(new Dictionary<string, string?> { [ContentKey] = "true" });

        Assert.Equal("true", await _store.GetAsync(ContentKey));
    }

    private async Task SeedAsync(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var seeder = new ConfigSeeder(_store, configuration, A.Fake<ILogger<ConfigSeeder>>());
        await seeder.StartAsync(CancellationToken.None);
    }

    public void Dispose() => _db.Dispose();
}
