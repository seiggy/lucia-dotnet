using System.Security.Claims;
using System.Text.Json;
using FakeItEasy;
using lucia.Agents.Abstractions;
using lucia.Agents.PluginFramework;
using lucia.AgentHost.PluginFramework;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests;

/// <summary>
/// Tests for the installed-plugins listing endpoint: a plugin may optionally
/// implement <see cref="IPluginStatusProvider"/> to surface a live, read-only
/// status line for the dashboard. Plugins without the interface (or with a
/// null status) must report no status.
/// </summary>
public sealed class InstalledPluginApiTests : IDisposable
{
    private readonly string _pluginDirectory =
        Path.Combine(Path.GetTempPath(), "lucia-installed-api-" + Guid.NewGuid().ToString("N")[..8]);

    public InstalledPluginApiTests() => Directory.CreateDirectory(_pluginDirectory);

    public void Dispose()
    {
        try { Directory.Delete(_pluginDirectory, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    [Fact]
    public async Task ListInstalled_ExposesStatusLineOnlyForIPluginStatusProvider()
    {
        var repository = A.Fake<IPluginManagementRepository>();
        A.CallTo(() => repository.GetInstalledPluginsAsync(A<CancellationToken>._)).Returns(new List<InstalledPluginRecord>
        {
            new() { Id = "status-plugin", Name = "Status Plugin", PluginPath = "/plugins/status-plugin" },
            new() { Id = "silent-plugin", Name = "Silent Plugin", PluginPath = "/plugins/silent-plugin" },
            new() { Id = "plain-plugin", Name = "Plain Plugin", PluginPath = "/plugins/plain-plugin" },
        });
        A.CallTo(() => repository.GetRepositoriesAsync(A<CancellationToken>._)).Returns(new List<PluginRepositoryDefinition>());

        var service = new PluginManagementService(
            repository,
            new PluginChangeTracker(),
            [],
            NullLogger<PluginManagementService>.Instance,
            _pluginDirectory);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton(service);
        builder.Services.AddSingleton<ILuciaPlugin>(new StatusPlugin("status-plugin", "All systems nominal"));
        builder.Services.AddSingleton<ILuciaPlugin>(new StatusPlugin("silent-plugin", null));
        builder.Services.AddSingleton<ILuciaPlugin>(new PlainPlugin("plain-plugin"));

        await using var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity([], "test"));
            await next();
        });
        app.UseAuthorization();
        app.MapInstalledPluginApi();
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };

        using var response = await client.GetAsync("/api/plugins/installed");
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var byId = doc.RootElement.EnumerateArray()
            .ToDictionary(e => e.GetProperty("id").GetString()!, StringComparer.Ordinal);

        Assert.Equal("All systems nominal", byId["status-plugin"].GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, byId["silent-plugin"].GetProperty("status").ValueKind);
        Assert.True(
            !byId["plain-plugin"].TryGetProperty("status", out var plain) || plain.ValueKind == JsonValueKind.Null,
            "A plugin without IPluginStatusProvider must not report a status");
    }

    private sealed class StatusPlugin(string pluginId, string? statusLine) : ILuciaPlugin, IPluginStatusProvider
    {
        public string PluginId => pluginId;
        public string? StatusLine => statusLine;
    }

    private sealed class PlainPlugin(string pluginId) : ILuciaPlugin
    {
        public string PluginId => pluginId;
    }
}
