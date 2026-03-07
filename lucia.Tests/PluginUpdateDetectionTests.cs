using FakeItEasy;
using lucia.Agents.Abstractions;
using lucia.Agents.PluginFramework;
using Microsoft.Extensions.Logging;

namespace lucia.Tests;

/// <summary>
/// Regression tests for plugin update detection (GitHub Issue #74).
/// Validates that version comparison, update checking, and update application
/// work correctly in <see cref="PluginManagementService"/>.
/// </summary>
public sealed class PluginUpdateDetectionTests : IDisposable
{
    private readonly IPluginManagementRepository _repository = A.Fake<IPluginManagementRepository>();
    private readonly IPluginRepositorySource _gitSource = A.Fake<IPluginRepositorySource>();
    private readonly PluginChangeTracker _changeTracker = new();
    private readonly ILogger<PluginManagementService> _logger = A.Fake<ILogger<PluginManagementService>>();
    private readonly string _pluginDirectory;
    private readonly PluginManagementService _service;

    public PluginUpdateDetectionTests()
    {
        _pluginDirectory = Path.Combine(Path.GetTempPath(), "lucia-test-plugins-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_pluginDirectory);

        A.CallTo(() => _gitSource.Type).Returns("git");

        _service = new PluginManagementService(
            _repository,
            _changeTracker,
            [_gitSource],
            _logger,
            _pluginDirectory);
    }

    public void Dispose()
    {
        try { Directory.Delete(_pluginDirectory, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    // ── Test 1: Detects newer version ────────────────────────────

    [Fact]
    public async Task CheckForUpdates_DetectsNewerVersion()
    {
        // Arrange: installed at v1.0.0, repo has v1.1.0
        SetupInstalledPlugins(
            CreateInstalledPlugin("test-plugin", "1.0.0", "repo-1"));

        SetupRepositories(
            CreateRepository("repo-1", CreateManifestEntry("test-plugin", "1.1.0")));

        // Act
        var updates = await _service.CheckForUpdatesAsync();

        // Assert
        Assert.Single(updates);
        Assert.Equal("test-plugin", updates[0].PluginId);
        Assert.Equal("1.0.0", updates[0].InstalledVersion);
        Assert.Equal("1.1.0", updates[0].AvailableVersion);
    }

    // ── Test 2: No update when versions match ────────────────────

    [Fact]
    public async Task CheckForUpdates_NoUpdateWhenVersionsMatch()
    {
        SetupInstalledPlugins(
            CreateInstalledPlugin("test-plugin", "1.1.0", "repo-1"));

        SetupRepositories(
            CreateRepository("repo-1", CreateManifestEntry("test-plugin", "1.1.0")));

        var updates = await _service.CheckForUpdatesAsync();

        Assert.Empty(updates);
    }

    // ── Test 3: No update when installed is newer ────────────────

    [Fact]
    public async Task CheckForUpdates_NoUpdateWhenInstalledVersionNewer()
    {
        SetupInstalledPlugins(
            CreateInstalledPlugin("test-plugin", "1.2.0", "repo-1"));

        SetupRepositories(
            CreateRepository("repo-1", CreateManifestEntry("test-plugin", "1.1.0")));

        var updates = await _service.CheckForUpdatesAsync();

        Assert.Empty(updates);
    }

    // ── Test 4: Handles null versions gracefully ─────────────────

    [Theory]
    [InlineData(null, "1.0.0")]
    [InlineData("1.0.0", null)]
    [InlineData(null, null)]
    public async Task CheckForUpdates_HandlesNullVersions(string? installedVersion, string? manifestVersion)
    {
        SetupInstalledPlugins(
            CreateInstalledPlugin("test-plugin", installedVersion, "repo-1"));

        SetupRepositories(
            CreateRepository("repo-1", CreateManifestEntry("test-plugin", manifestVersion)));

        // Should not throw
        var updates = await _service.CheckForUpdatesAsync();

        Assert.NotNull(updates);
    }

    // ── Test 5: Update downloads and replaces files ──────────────

    [Fact]
    public async Task UpdatePlugin_DownloadsAndReplacesFiles()
    {
        // Arrange
        var installed = CreateInstalledPlugin("test-plugin", "1.0.0", "repo-1");
        var repo = CreateRepository("repo-1", CreateManifestEntry("test-plugin", "1.1.0"));

        SetupInstalledPlugins(installed);
        SetupRepositories(repo);
        A.CallTo(() => _repository.GetInstalledPluginAsync("test-plugin", A<CancellationToken>._))
            .Returns(installed);

        // Act
        var result = await _service.UpdatePluginAsync("test-plugin");

        // Assert — source was called to install the new version
        A.CallTo(() => _gitSource.InstallPluginAsync(
            A<PluginRepositoryDefinition>.That.Matches(r => r.Id == "repo-1"),
            A<PluginManifestEntry>.That.Matches(m => m.Version == "1.1.0"),
            A<string>._,
            A<CancellationToken>._)).MustHaveHappenedOnceExactly();

        // Assert — installed record version was updated
        A.CallTo(() => _repository.UpsertInstalledPluginAsync(
            A<InstalledPluginRecord>.That.Matches(r => r.Id == "test-plugin" && r.Version == "1.1.0"),
            A<CancellationToken>._)).MustHaveHappenedOnceExactly();

        Assert.Equal(PluginUpdateResult.Updated, result);
    }

    // ── Test 6: Update marks restart required ────────────────────

    [Fact]
    public async Task UpdatePlugin_MarksRestartRequired()
    {
        var installed = CreateInstalledPlugin("test-plugin", "1.0.0", "repo-1");
        var repo = CreateRepository("repo-1", CreateManifestEntry("test-plugin", "1.1.0"));

        SetupInstalledPlugins(installed);
        SetupRepositories(repo);
        A.CallTo(() => _repository.GetInstalledPluginAsync("test-plugin", A<CancellationToken>._))
            .Returns(installed);

        _changeTracker.ClearRestartRequired();

        await _service.UpdatePluginAsync("test-plugin");

        Assert.True(_changeTracker.IsRestartRequired);
    }

    // ── Test 7: Installed plugins include update-available flag ──

    [Fact]
    public async Task GetInstalledPluginsWithUpdateInfo_IncludesUpdateAvailableFlag()
    {
        SetupInstalledPlugins(
            CreateInstalledPlugin("plugin-a", "1.0.0", "repo-1"),
            CreateInstalledPlugin("plugin-b", "2.0.0", "repo-1"));

        SetupRepositories(
            CreateRepository("repo-1",
                CreateManifestEntry("plugin-a", "1.1.0"),
                CreateManifestEntry("plugin-b", "2.0.0")));

        var result = await _service.GetInstalledPluginsWithUpdateInfoAsync();

        Assert.Equal(2, result.Count);

        var pluginA = result.First(r => r.Plugin.Id == "plugin-a");
        Assert.True(pluginA.UpdateAvailable);
        Assert.Equal("1.1.0", pluginA.AvailableVersion);

        var pluginB = result.First(r => r.Plugin.Id == "plugin-b");
        Assert.False(pluginB.UpdateAvailable);
        Assert.Null(pluginB.AvailableVersion);
    }

    // ── Test 8: Returns PluginNotInstalled when plugin does not exist ─

    [Fact]
    public async Task UpdatePlugin_ReturnsNotInstalled_WhenPluginDoesNotExist()
    {
        A.CallTo(() => _repository.GetInstalledPluginAsync("nonexistent", A<CancellationToken>._))
            .Returns((InstalledPluginRecord?)null);

        var result = await _service.UpdatePluginAsync("nonexistent");

        Assert.Equal(PluginUpdateResult.PluginNotInstalled, result);
    }

    // ── Test 9: Returns PluginNotInRepository when not in any manifest ─

    [Fact]
    public async Task UpdatePlugin_ReturnsNotInRepository_WhenNotInAnyManifest()
    {
        var installed = CreateInstalledPlugin("orphan-plugin", "1.0.0", "repo-1");
        A.CallTo(() => _repository.GetInstalledPluginAsync("orphan-plugin", A<CancellationToken>._))
            .Returns(installed);

        // Repository exists but has no matching plugin in its manifest
        SetupRepositories(
            CreateRepository("repo-1", CreateManifestEntry("other-plugin", "2.0.0")));

        var result = await _service.UpdatePluginAsync("orphan-plugin");

        Assert.Equal(PluginUpdateResult.PluginNotInRepository, result);
    }

    // ── Test 10: Returns AlreadyUpToDate when versions match ─────

    [Fact]
    public async Task UpdatePlugin_ReturnsAlreadyUpToDate_WhenVersionsMatch()
    {
        var installed = CreateInstalledPlugin("test-plugin", "1.0.0", "repo-1");
        A.CallTo(() => _repository.GetInstalledPluginAsync("test-plugin", A<CancellationToken>._))
            .Returns(installed);

        SetupRepositories(
            CreateRepository("repo-1", CreateManifestEntry("test-plugin", "1.0.0")));

        var result = await _service.UpdatePluginAsync("test-plugin");

        Assert.Equal(PluginUpdateResult.AlreadyUpToDate, result);
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static InstalledPluginRecord CreateInstalledPlugin(string id, string? version, string repoId) =>
        new()
        {
            Id = id,
            Name = id,
            Version = version,
            Source = repoId,
            RepositoryId = repoId,
            PluginPath = $"/plugins/{id}",
            Enabled = true,
            InstalledAt = DateTime.UtcNow,
        };

    private static PluginManifestEntry CreateManifestEntry(string id, string? version) =>
        new()
        {
            Id = id,
            Name = id,
            Version = version,
            Path = $"plugins/{id}",
        };

    private static PluginRepositoryDefinition CreateRepository(string id, params PluginManifestEntry[] plugins) =>
        new()
        {
            Id = id,
            Name = id,
            Type = "git",
            Enabled = true,
            CachedPlugins = [.. plugins],
        };

    private void SetupInstalledPlugins(params InstalledPluginRecord[] plugins)
    {
        A.CallTo(() => _repository.GetInstalledPluginsAsync(A<CancellationToken>._))
            .Returns(new List<InstalledPluginRecord>(plugins));
    }

    private void SetupRepositories(params PluginRepositoryDefinition[] repos)
    {
        A.CallTo(() => _repository.GetRepositoriesAsync(A<CancellationToken>._))
            .Returns(new List<PluginRepositoryDefinition>(repos));

        foreach (var repo in repos)
        {
            A.CallTo(() => _repository.GetRepositoryAsync(repo.Id, A<CancellationToken>._))
                .Returns(repo);
        }
    }
}
