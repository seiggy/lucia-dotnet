using lucia.Agents.Abstractions;
using lucia.Agents.PluginFramework;

namespace lucia.Tests.Data.Providers;

/// <summary>
/// Behaviour every <see cref="IPluginManagementRepository"/> provider must share.
/// </summary>
public abstract class PluginManagementRepositoryContract
{
    protected abstract Task<StoreScope<IPluginManagementRepository>> CreateAsync();

    [Fact]
    public async Task GetRepositoriesAsync_EmptyStore_ReturnsEmpty()
    {
        await using var scope = await CreateAsync();

        Assert.Empty(await scope.Store.GetRepositoriesAsync());
    }

    [Fact]
    public async Task GetRepositoryAsync_UnknownId_ReturnsNull()
    {
        await using var scope = await CreateAsync();

        Assert.Null(await scope.Store.GetRepositoryAsync("missing"));
    }

    [Fact]
    public async Task UpsertRepositoryAsync_NewRepository_RoundTripsEveryMeaningfulField()
    {
        await using var scope = await CreateAsync();
        var syncedAt = new DateTime(2024, 09, 10, 11, 12, 13, DateTimeKind.Utc);
        var repository = new PluginRepositoryDefinition
        {
            Id = "repo-alpha",
            Name = "Alpha repo",
            Type = "git",
            Url = "https://example.com/repo.git",
            Branch = "main",
            BlobSource = "branch",
            ManifestPath = "lucia-plugins.json",
            Enabled = true,
            LastSyncedAt = syncedAt,
            CachedPlugins =
            [
                new PluginManifestEntry
                {
                    Id = "plugin-one",
                    Name = "Plugin One",
                    Description = "First plugin",
                    Version = "1.2.3",
                    Author = "Lucia",
                    Tags = ["ai", "demo"],
                    Path = "plugins/plugin-one",
                    Homepage = "https://example.com/plugin-one",
                    MinLuciaVersion = "1.0.0",
                },
            ],
        };

        await scope.Store.UpsertRepositoryAsync(repository);

        var saved = Assert.Single(await scope.Store.GetRepositoriesAsync());
        Assert.Equal(repository.Id, saved.Id);
        Assert.Equal(repository.Name, saved.Name);
        Assert.Equal(repository.Type, saved.Type);
        Assert.Equal(repository.Url, saved.Url);
        Assert.Equal(repository.Branch, saved.Branch);
        Assert.Equal(repository.BlobSource, saved.BlobSource);
        Assert.Equal(repository.ManifestPath, saved.ManifestPath);
        Assert.Equal(repository.Enabled, saved.Enabled);
        Assert.Equal(repository.LastSyncedAt, saved.LastSyncedAt);
        var plugin = Assert.Single(saved.CachedPlugins);
        Assert.Equal("plugin-one", plugin.Id);
        Assert.Equal("Plugin One", plugin.Name);
        Assert.Equal("First plugin", plugin.Description);
        Assert.Equal("1.2.3", plugin.Version);
        Assert.Equal("Lucia", plugin.Author);
        Assert.Equal(["ai", "demo"], plugin.Tags);
        Assert.Equal("plugins/plugin-one", plugin.Path);
        Assert.Equal("https://example.com/plugin-one", plugin.Homepage);
        Assert.Equal("1.0.0", plugin.MinLuciaVersion);
    }

    [Fact]
    public async Task UpsertRepositoryAsync_SameId_ReplacesInsteadOfDuplicating()
    {
        await using var scope = await CreateAsync();
        await scope.Store.UpsertRepositoryAsync(new PluginRepositoryDefinition { Id = "repo-a", Name = "Repo A", Type = "git", Url = "https://example.com/old.git", Enabled = true });

        await scope.Store.UpsertRepositoryAsync(new PluginRepositoryDefinition { Id = "repo-a", Name = "Repo A Updated", Type = "local", Url = "/tmp/plugins", Enabled = false });

        var repositories = await scope.Store.GetRepositoriesAsync();
        var repo = Assert.Single(repositories);
        Assert.Equal("Repo A Updated", repo.Name);
        Assert.Equal("local", repo.Type);
        Assert.False(repo.Enabled);
    }

    [Fact]
    public async Task DeleteRepositoryAsync_RemovesOnlyNamedRepository()
    {
        await using var scope = await CreateAsync();
        await scope.Store.UpsertRepositoryAsync(CreateRepository("repo-a", "A"));
        await scope.Store.UpsertRepositoryAsync(CreateRepository("repo-b", "B"));

        await scope.Store.DeleteRepositoryAsync("repo-a");

        var repositories = await scope.Store.GetRepositoriesAsync();
        var repo = Assert.Single(repositories);
        Assert.Equal("repo-b", repo.Id);
    }

    [Fact]
    public async Task GetInstalledPluginsAsync_EmptyStore_ReturnsEmpty()
    {
        await using var scope = await CreateAsync();

        Assert.Empty(await scope.Store.GetInstalledPluginsAsync());
    }

    [Fact]
    public async Task GetInstalledPluginAsync_UnknownId_ReturnsNull()
    {
        await using var scope = await CreateAsync();

        Assert.Null(await scope.Store.GetInstalledPluginAsync("missing"));
    }

    [Fact]
    public async Task UpsertInstalledPluginAsync_NewRecord_RoundTripsEveryMeaningfulField()
    {
        await using var scope = await CreateAsync();
        var installedAt = new DateTime(2024, 11, 12, 13, 14, 15, DateTimeKind.Utc);
        var record = new InstalledPluginRecord
        {
            Id = "plugin-a",
            Name = "Plugin A",
            Version = "2.0.0",
            Source = "bundled",
            RepositoryId = "repo-a",
            Description = "A plugin for the dashboard",
            Author = "Lucia Team",
            PluginPath = "/plugins/plugin-a",
            Enabled = true,
            InstalledAt = installedAt,
        };

        await scope.Store.UpsertInstalledPluginAsync(record);

        var saved = Assert.Single(await scope.Store.GetInstalledPluginsAsync());
        Assert.Equal(record.Id, saved.Id);
        Assert.Equal(record.Name, saved.Name);
        Assert.Equal(record.Version, saved.Version);
        Assert.Equal(record.Source, saved.Source);
        Assert.Equal(record.RepositoryId, saved.RepositoryId);
        Assert.Equal(record.Description, saved.Description);
        Assert.Equal(record.Author, saved.Author);
        Assert.Equal(record.PluginPath, saved.PluginPath);
        Assert.Equal(record.Enabled, saved.Enabled);
        Assert.Equal(record.InstalledAt, saved.InstalledAt);
    }

    [Fact]
    public async Task UpsertInstalledPluginAsync_SameId_ReplacesInsteadOfDuplicating()
    {
        await using var scope = await CreateAsync();
        await scope.Store.UpsertInstalledPluginAsync(new InstalledPluginRecord { Id = "plugin-a", Name = "Plugin A", Enabled = true, PluginPath = "/plugins/one" });

        await scope.Store.UpsertInstalledPluginAsync(new InstalledPluginRecord { Id = "plugin-a", Name = "Plugin A Updated", Enabled = false, PluginPath = "/plugins/two" });

        var plugins = await scope.Store.GetInstalledPluginsAsync();
        var plugin = Assert.Single(plugins);
        Assert.Equal("Plugin A Updated", plugin.Name);
        Assert.False(plugin.Enabled);
        Assert.Equal("/plugins/two", plugin.PluginPath);
    }

    [Fact]
    public async Task DeleteInstalledPluginAsync_RemovesOnlyNamedPlugin()
    {
        await using var scope = await CreateAsync();
        await scope.Store.UpsertInstalledPluginAsync(CreateInstalledPlugin("plugin-a", "A"));
        await scope.Store.UpsertInstalledPluginAsync(CreateInstalledPlugin("plugin-b", "B"));

        await scope.Store.DeleteInstalledPluginAsync("plugin-a");

        var plugins = await scope.Store.GetInstalledPluginsAsync();
        var plugin = Assert.Single(plugins);
        Assert.Equal("plugin-b", plugin.Id);
    }

    private static PluginRepositoryDefinition CreateRepository(string id, string name)
        => new()
        {
            Id = id,
            Name = name,
            Type = "git",
            Url = $"https://example.com/{id}.git",
            Branch = "main",
            BlobSource = "release",
            ManifestPath = "lucia-plugins.json",
            Enabled = true,
        };

    private static InstalledPluginRecord CreateInstalledPlugin(string id, string name)
        => new()
        {
            Id = id,
            Name = name,
            Version = "1.0.0",
            Source = "bundled",
            PluginPath = $"/plugins/{id}",
            Enabled = true,
            InstalledAt = DateTime.UtcNow,
        };
}
