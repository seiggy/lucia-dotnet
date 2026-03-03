using System.IO.Compression;
using System.Text;
using System.Text.Json;
using FakeItEasy;
using lucia.Agents.Abstractions;
using lucia.Agents.Configuration;
using lucia.Agents.Extensions;
using lucia.Agents.PluginFramework;
using lucia.Agents.Services;
using Microsoft.Extensions.Logging;

namespace lucia.Tests.Plugins;

/// <summary>
/// Verifies the plugin system: script evaluation, loader, management service, and repository sources.
/// </summary>
public sealed class PluginSystemTests : IDisposable
{
    private readonly List<string> _tempDirs = [];

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
    }

    private string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lucia-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    private static ILogger CreateLogger()
    {
        using var factory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        return factory.CreateLogger("PluginSystemTests");
    }

    private static ILogger<T> CreateLogger<T>()
    {
        using var factory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        return factory.CreateLogger<T>();
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "lucia-dotnet.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not find repo root (lucia-dotnet.slnx).");
    }

    private const string MinimalPluginCs = """
        using lucia.Agents.Abstractions;

        public class TestPlugin : ILuciaPlugin
        {
            public string PluginId => "testplugin";
        }

        new TestPlugin()
        """;

    /// <summary>
    /// Creates a PluginManagementService with faked dependencies and supplied sources.
    /// </summary>
    private PluginManagementService CreateService(
        IPluginManagementRepository? repo = null,
        PluginChangeTracker? changeTracker = null,
        IEnumerable<IPluginRepositorySource>? sources = null,
        string? pluginDirectory = null)
    {
        return new PluginManagementService(
            repo ?? A.Fake<IPluginManagementRepository>(),
            changeTracker ?? new PluginChangeTracker(),
            sources ?? [],
            CreateLogger<PluginManagementService>(),
            pluginDirectory ?? CreateTempDir());
    }

    // ── Test 1: PluginScriptHost evaluates MetaMCP plugin.cs ────────

    [Fact]
    public async Task PluginScriptHost_EvaluatesMetaMcpPlugin_Successfully()
    {
        var repoRoot = FindRepoRoot();
        var metamcpSource = Path.Combine(repoRoot, "plugins", "metamcp");

        // Copy plugin files to a temp directory so the test is side-effect-free
        var tempDir = CreateTempDir();
        foreach (var file in Directory.EnumerateFiles(metamcpSource))
        {
            File.Copy(file, Path.Combine(tempDir, Path.GetFileName(file)));
        }

        var logger = CreateLogger();

        var plugin = await PluginScriptHost.EvaluatePluginAsync(tempDir, logger);

        Assert.NotNull(plugin);
        Assert.Equal("metamcp", plugin.PluginId);
    }

    // ── Test 2: PluginLoader loads all present plugins ──────────────

    [Fact]
    public async Task PluginLoader_LoadsAllPresentPlugins()
    {
        var tempDir = CreateTempDir();
        var pluginDir = Path.Combine(tempDir, "plugins");
        var testPluginDir = Path.Combine(pluginDir, "testplugin");
        Directory.CreateDirectory(testPluginDir);
        await File.WriteAllTextAsync(Path.Combine(testPluginDir, "plugin.cs"), MinimalPluginCs);

        var logger = CreateLogger();
        var loaded = await PluginLoader.LoadPluginsAsync(pluginDir, logger);

        Assert.Single(loaded);
        Assert.Equal("testplugin", loaded[0].PluginId);
    }

    // ── Test 3: PluginLoader skips folders without plugin.cs ────────

    [Fact]
    public async Task PluginLoader_SkipsFoldersWithoutPluginCs()
    {
        var tempDir = CreateTempDir();
        var pluginDir = Path.Combine(tempDir, "plugins");
        Directory.CreateDirectory(Path.Combine(pluginDir, "empty-folder"));

        var logger = CreateLogger();
        var loaded = await PluginLoader.LoadPluginsAsync(pluginDir, logger);

        Assert.Empty(loaded);
    }

    // ── Test 4: SyncRepositoryAsync fetches and caches manifest ─────

    [Fact]
    public async Task SyncRepositoryAsync_FetchesAndCachesManifest()
    {
        var repo = new PluginRepositoryDefinition
        {
            Id = "test-repo",
            Name = "Test Repo",
            Type = "local",
            Url = "/fake/path",
            Enabled = true,
        };

        var fakeDbRepo = A.Fake<IPluginManagementRepository>();
        A.CallTo(() => fakeDbRepo.GetRepositoryAsync("test-repo", A<CancellationToken>._))
            .Returns(repo);

        var fakeSource = A.Fake<IPluginRepositorySource>();
        A.CallTo(() => fakeSource.Type).Returns("local");
        A.CallTo(() => fakeSource.FetchManifestAsync(repo, A<CancellationToken>._))
            .Returns(new PluginManifest
            {
                Id = "test-repo",
                Name = "Test Repo",
                Plugins =
                [
                    new PluginManifestEntry
                    {
                        Id = "testplugin",
                        Name = "Test Plugin",
                        Description = "A test plugin",
                        Version = "1.0.0",
                        Author = "Test Author",
                        Tags = ["test"],
                        Path = "plugins/testplugin",
                    },
                ],
            });

        var svc = CreateService(
            repo: fakeDbRepo,
            sources: [fakeSource]);

        await svc.SyncRepositoryAsync("test-repo");

        A.CallTo(() => fakeDbRepo.UpsertRepositoryAsync(
                A<PluginRepositoryDefinition>.That.Matches(r => r.CachedPlugins.Count == 1),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    // ── Test 5: SetPluginEnabledAsync updates MongoDB and signals restart ─

    [Fact]
    public async Task SetPluginEnabledAsync_UpdatesMongoAndSignalsRestart()
    {
        var fakeDbRepo = A.Fake<IPluginManagementRepository>();
        A.CallTo(() => fakeDbRepo.GetInstalledPluginAsync("testplugin", A<CancellationToken>._))
            .Returns(new InstalledPluginRecord
            {
                Id = "testplugin",
                Name = "Test Plugin",
                Enabled = true,
                PluginPath = "/tmp/plugins/testplugin",
            });

        var changeTracker = new PluginChangeTracker();
        var svc = CreateService(repo: fakeDbRepo, changeTracker: changeTracker);

        await svc.SetPluginEnabledAsync("testplugin", false);

        A.CallTo(() => fakeDbRepo.UpsertInstalledPluginAsync(
                A<InstalledPluginRecord>.That.Matches(r => !r.Enabled),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();

        Assert.True(changeTracker.IsRestartRequired);
    }

    // ── Test 6: InstallPluginAsync delegates to source and records in MongoDB ───

    [Fact]
    public async Task InstallPluginAsync_DelegatesToSourceAndRecordsInMongo()
    {
        var tempDir = CreateTempDir();
        var pluginDir = Path.Combine(tempDir, "plugins");
        Directory.CreateDirectory(pluginDir);

        var repo = new PluginRepositoryDefinition
        {
            Id = "test-repo",
            Name = "Test Repo",
            Type = "local",
            Url = "/fake/path",
            Enabled = true,
        };

        var fakeDbRepo = A.Fake<IPluginManagementRepository>();
        A.CallTo(() => fakeDbRepo.GetRepositoryAsync("test-repo", A<CancellationToken>._))
            .Returns(repo);

        var fakeSource = A.Fake<IPluginRepositorySource>();
        A.CallTo(() => fakeSource.Type).Returns("local");

        var manifestEntry = new PluginManifestEntry
        {
            Id = "testplugin",
            Name = "Test Plugin",
            Version = "1.0.0",
            Path = "plugins/testplugin",
        };

        var changeTracker = new PluginChangeTracker();
        var svc = CreateService(
            repo: fakeDbRepo,
            changeTracker: changeTracker,
            sources: [fakeSource],
            pluginDirectory: pluginDir);

        await svc.InstallPluginAsync("testplugin", "test-repo", manifestEntry);

        // Source was asked to install
        A.CallTo(() => fakeSource.InstallPluginAsync(
                repo, manifestEntry,
                Path.Combine(pluginDir, "testplugin"),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();

        // MongoDB record was saved
        A.CallTo(() => fakeDbRepo.UpsertInstalledPluginAsync(
                A<InstalledPluginRecord>.That.Matches(r => r.Id == "testplugin" && r.Enabled),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();

        Assert.True(changeTracker.IsRestartRequired);
    }

    // ── Test 7: UninstallPluginAsync removes folder and signals restart ─

    [Fact]
    public async Task UninstallPluginAsync_RemovesFolderAndSignalsRestart()
    {
        var tempDir = CreateTempDir();
        var pluginDir = Path.Combine(tempDir, "plugins");
        var pluginFolder = Path.Combine(pluginDir, "testplugin");
        Directory.CreateDirectory(pluginFolder);
        await File.WriteAllTextAsync(Path.Combine(pluginFolder, "plugin.cs"), MinimalPluginCs);

        var fakeDbRepo = A.Fake<IPluginManagementRepository>();
        var changeTracker = new PluginChangeTracker();

        var svc = CreateService(
            repo: fakeDbRepo,
            changeTracker: changeTracker,
            pluginDirectory: pluginDir);

        await svc.UninstallPluginAsync("testplugin");

        // Verify folder was deleted
        Assert.False(Directory.Exists(pluginFolder));

        // Verify MongoDB record was deleted
        A.CallTo(() => fakeDbRepo.DeleteInstalledPluginAsync("testplugin", A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();

        Assert.True(changeTracker.IsRestartRequired);
    }

    // ── Test 8: lucia-plugins.json in repo root is valid manifest ────

    [Fact]
    public void RepoRoot_LuciaPluginsJson_DeserializesAsManifest()
    {
        var repoRoot = FindRepoRoot();
        var manifestPath = Path.Combine(repoRoot, "lucia-plugins.json");
        Assert.True(File.Exists(manifestPath), "lucia-plugins.json must exist at repo root");

        var json = File.ReadAllText(manifestPath);
        var manifest = JsonSerializer.Deserialize<PluginManifest>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(manifest);
        Assert.Equal("lucia-official", manifest.Id);
        Assert.NotEmpty(manifest.Plugins);
        Assert.Contains(manifest.Plugins, p => p.Id == "metamcp");
        Assert.Contains(manifest.Plugins, p => p.Id == "searxng");

        // Each entry must have a relative path
        foreach (var plugin in manifest.Plugins)
        {
            Assert.False(string.IsNullOrWhiteSpace(plugin.Path),
                $"Plugin '{plugin.Id}' must have a path.");
        }
    }

    // ── Test 9: MetaMCP plugin loads end-to-end via PluginLoader ────

    [Fact]
    public async Task PluginLoader_LoadsMetaMcpPlugin_FromRepoFiles()
    {
        var repoRoot = FindRepoRoot();
        var pluginDir = Path.Combine(repoRoot, "plugins");
        var logger = CreateLogger();

        var loaded = await PluginLoader.LoadPluginsAsync(pluginDir, logger);

        Assert.Contains(loaded, p => p.PluginId == "metamcp");
    }

    // ── Test 10: LocalPluginRepositorySource discovers plugins from manifest ──

    [Fact]
    public async Task LocalSource_FetchManifest_ReadsFromFilesystem()
    {
        var tempDir = CreateTempDir();

        // Create a local directory with a manifest and plugin folders
        var alphaDir = Path.Combine(tempDir, "plugins", "alpha");
        Directory.CreateDirectory(alphaDir);
        await File.WriteAllTextAsync(Path.Combine(alphaDir, "plugin.cs"), MinimalPluginCs);

        var manifest = new PluginManifest
        {
            Id = "test-local",
            Name = "Test Local",
            Plugins =
            [
                new PluginManifestEntry
                {
                    Id = "alpha",
                    Name = "Alpha Plugin",
                    Version = "1.0.0",
                    Path = "plugins/alpha",
                },
            ],
        };
        await File.WriteAllTextAsync(
            Path.Combine(tempDir, "lucia-plugins.json"),
            JsonSerializer.Serialize(manifest));

        var source = new LocalPluginRepositorySource(CreateLogger<LocalPluginRepositorySource>());
        var repo = new PluginRepositoryDefinition
        {
            Id = "test-local",
            Name = "Test Local",
            Type = "local",
            Url = tempDir,
            ManifestPath = "lucia-plugins.json",
        };

        var result = await source.FetchManifestAsync(repo, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Single(result.Plugins);
        Assert.Equal("alpha", result.Plugins[0].Id);
    }

    // ── Test 11: LocalPluginRepositorySource install copies files ──

    [Fact]
    public async Task LocalSource_InstallPlugin_CopiesFilesFromSourceDir()
    {
        var tempDir = CreateTempDir();

        // Create source plugin folder
        var sourcePluginDir = Path.Combine(tempDir, "plugins", "myplugin");
        Directory.CreateDirectory(sourcePluginDir);
        await File.WriteAllTextAsync(Path.Combine(sourcePluginDir, "plugin.cs"), MinimalPluginCs);
        await File.WriteAllTextAsync(Path.Combine(sourcePluginDir, "helpers.cs"), "// helper code");

        var source = new LocalPluginRepositorySource(CreateLogger<LocalPluginRepositorySource>());
        var repo = new PluginRepositoryDefinition
        {
            Id = "test-local",
            Name = "Test Local",
            Type = "local",
            Url = tempDir,
        };

        var entry = new PluginManifestEntry
        {
            Id = "myplugin",
            Name = "My Plugin",
            Version = "1.0.0",
            Path = "plugins/myplugin",
        };

        var targetDir = Path.Combine(CreateTempDir(), "myplugin");

        await source.InstallPluginAsync(repo, entry, targetDir, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(targetDir, "plugin.cs")));
        Assert.True(File.Exists(Path.Combine(targetDir, "helpers.cs")));
    }

    // ── Test 12: ParseGitHubOwnerRepo extracts owner and repo ───────

    [Theory]
    [InlineData("https://github.com/seiggy/lucia-dotnet", "seiggy", "lucia-dotnet")]
    [InlineData("https://github.com/octocat/Hello-World/", "octocat", "Hello-World")]
    public void ParseGitHubOwnerRepo_ExtractsCorrectly(string url, string expectedOwner, string expectedRepo)
    {
        var (owner, repo) = GitPluginRepositorySource.ParseGitHubOwnerRepo(url);
        Assert.Equal(expectedOwner, owner);
        Assert.Equal(expectedRepo, repo);
    }

    // ── Test 13: Release strategy uses per-plugin asset when available ─

    [Fact]
    public async Task GitSource_ReleaseStrategy_UsesPerPluginAsset()
    {
        var tempDir = CreateTempDir();
        var targetPath = Path.Combine(tempDir, "myplugin");

        // Build a plugin asset zip (flat — no top-level folder)
        var assetZipBytes = BuildFlatPluginZip("plugin.cs", MinimalPluginCs);

        var releaseJson = JsonSerializer.Serialize(new
        {
            tag_name = "v1.0.0",
            name = "v1.0.0",
            zipball_url = "https://github.com/test/repo/archive/v1.0.0.zip",
            assets = new[]
            {
                new
                {
                    name = "myplugin.zip",
                    browser_download_url = "https://github.com/test/repo/releases/download/v1.0.0/myplugin.zip",
                    content_type = "application/zip",
                    size = assetZipBytes.Length,
                },
            },
        });

        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("/releases/latest"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(releaseJson, Encoding.UTF8, "application/json"),
                };
            }

            if (req.RequestUri.AbsolutePath.Contains("/releases/download/"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(assetZipBytes),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var httpFactory = CreateFakeHttpFactory(handler);
        var source = new GitPluginRepositorySource(httpFactory, CreateLogger<GitPluginRepositorySource>());

        var repo = new PluginRepositoryDefinition
        {
            Id = "test",
            Name = "Test",
            Type = "git",
            Url = "https://github.com/test/repo",
            BlobSource = "release",
        };

        var entry = new PluginManifestEntry { Id = "myplugin", Name = "My Plugin", Path = "plugins/myplugin" };

        await source.InstallPluginAsync(repo, entry, targetPath, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(targetPath, "plugin.cs")));
    }

    // ── Test 14: Release strategy falls back to zipball when no asset ──

    [Fact]
    public async Task GitSource_ReleaseStrategy_FallsBackToZipball()
    {
        var tempDir = CreateTempDir();
        var targetPath = Path.Combine(tempDir, "myplugin");

        // Build a repo-style zipball with top-level folder
        var zipballBytes = BuildRepoZipball("test-repo-abc123", "plugins/myplugin/plugin.cs", MinimalPluginCs);

        var releaseJson = JsonSerializer.Serialize(new
        {
            tag_name = "v1.0.0",
            name = "v1.0.0",
            zipball_url = "https://api.github.com/repos/test/repo/zipball/v1.0.0",
            assets = Array.Empty<object>(), // no per-plugin assets
        });

        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("/releases/latest"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(releaseJson, Encoding.UTF8, "application/json"),
                };
            }

            if (req.RequestUri.AbsolutePath.Contains("/zipball/"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(zipballBytes),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var httpFactory = CreateFakeHttpFactory(handler);
        var source = new GitPluginRepositorySource(httpFactory, CreateLogger<GitPluginRepositorySource>());

        var repo = new PluginRepositoryDefinition
        {
            Id = "test",
            Name = "Test",
            Type = "git",
            Url = "https://github.com/test/repo",
            BlobSource = "release",
        };

        var entry = new PluginManifestEntry { Id = "myplugin", Name = "My Plugin", Path = "plugins/myplugin" };

        await source.InstallPluginAsync(repo, entry, targetPath, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(targetPath, "plugin.cs")));
    }

    // ── Test 15: Release strategy falls back to branch when no release ──

    [Fact]
    public async Task GitSource_ReleaseStrategy_FallsToBranchWhenNoRelease()
    {
        var tempDir = CreateTempDir();
        var targetPath = Path.Combine(tempDir, "myplugin");

        var zipballBytes = BuildRepoZipball("test-repo-abc123", "plugins/myplugin/plugin.cs", MinimalPluginCs);

        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("/releases/latest"))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            // Branch archive fallback
            if (req.RequestUri.AbsolutePath.Contains("/archive/"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(zipballBytes),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var httpFactory = CreateFakeHttpFactory(handler);
        var source = new GitPluginRepositorySource(httpFactory, CreateLogger<GitPluginRepositorySource>());

        var repo = new PluginRepositoryDefinition
        {
            Id = "test",
            Name = "Test",
            Type = "git",
            Url = "https://github.com/test/repo",
            Branch = "main",
            BlobSource = "release",
        };

        var entry = new PluginManifestEntry { Id = "myplugin", Name = "My Plugin", Path = "plugins/myplugin" };

        await source.InstallPluginAsync(repo, entry, targetPath, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(targetPath, "plugin.cs")));
    }

    // ── Test 16: Branch strategy downloads archive at refs/heads ─────

    [Fact]
    public async Task GitSource_BranchStrategy_DownloadsCorrectArchive()
    {
        var tempDir = CreateTempDir();
        var targetPath = Path.Combine(tempDir, "myplugin");

        var zipballBytes = BuildRepoZipball("test-repo-abc123", "plugins/myplugin/plugin.cs", MinimalPluginCs);
        string? capturedUrl = null;

        var handler = new FakeHttpMessageHandler(req =>
        {
            capturedUrl = req.RequestUri!.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(zipballBytes),
            };
        });

        var httpFactory = CreateFakeHttpFactory(handler);
        var source = new GitPluginRepositorySource(httpFactory, CreateLogger<GitPluginRepositorySource>());

        var repo = new PluginRepositoryDefinition
        {
            Id = "test",
            Name = "Test",
            Type = "git",
            Url = "https://github.com/test/repo",
            Branch = "develop",
            BlobSource = "branch",
        };

        var entry = new PluginManifestEntry { Id = "myplugin", Name = "My Plugin", Path = "plugins/myplugin" };

        await source.InstallPluginAsync(repo, entry, targetPath, CancellationToken.None);

        Assert.NotNull(capturedUrl);
        Assert.Contains("refs/heads/develop", capturedUrl);
        Assert.True(File.Exists(Path.Combine(targetPath, "plugin.cs")));
    }

    // ── Test 17: Tag strategy downloads archive at refs/tags ─────────

    [Fact]
    public async Task GitSource_TagStrategy_DownloadsCorrectArchive()
    {
        var tempDir = CreateTempDir();
        var targetPath = Path.Combine(tempDir, "myplugin");

        var zipballBytes = BuildRepoZipball("test-repo-v1.0.0", "plugins/myplugin/plugin.cs", MinimalPluginCs);
        string? capturedUrl = null;

        var handler = new FakeHttpMessageHandler(req =>
        {
            capturedUrl = req.RequestUri!.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(zipballBytes),
            };
        });

        var httpFactory = CreateFakeHttpFactory(handler);
        var source = new GitPluginRepositorySource(httpFactory, CreateLogger<GitPluginRepositorySource>());

        var repo = new PluginRepositoryDefinition
        {
            Id = "test",
            Name = "Test",
            Type = "git",
            Url = "https://github.com/test/repo",
            Branch = "v1.0.0",
            BlobSource = "tag",
        };

        var entry = new PluginManifestEntry { Id = "myplugin", Name = "My Plugin", Path = "plugins/myplugin" };

        await source.InstallPluginAsync(repo, entry, targetPath, CancellationToken.None);

        Assert.NotNull(capturedUrl);
        Assert.Contains("refs/tags/v1.0.0", capturedUrl);
        Assert.True(File.Exists(Path.Combine(targetPath, "plugin.cs")));
    }

    // ── Zip Helpers ─────────────────────────────────────────────────

    /// <summary>
    /// Builds a flat plugin zip (no top-level folder) suitable for a release asset.
    /// </summary>
    private static byte[] BuildFlatPluginZip(string fileName, string content)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry(fileName);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }
        return ms.ToArray();
    }

    /// <summary>
    /// Builds a GitHub-style repo zipball with a top-level wrapper directory.
    /// </summary>
    private static byte[] BuildRepoZipball(string topLevelDir, string filePath, string content)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry($"{topLevelDir}/{filePath}");
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }
        return ms.ToArray();
    }

    private static IHttpClientFactory CreateFakeHttpFactory(FakeHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var factory = A.Fake<IHttpClientFactory>();
        A.CallTo(() => factory.CreateClient("PluginRepos")).Returns(httpClient);
        return factory;
    }
}
