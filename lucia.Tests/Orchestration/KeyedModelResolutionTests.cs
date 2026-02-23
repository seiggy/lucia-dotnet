#pragma warning disable AIEVAL001 // Microsoft.Extensions.AI.Evaluation is experimental

using FakeItEasy;
using lucia.Agents.Configuration;
using lucia.Agents.Orchestration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;

namespace lucia.Tests.Orchestration;

/// <summary>
/// Diagnostic tests for the keyed <see cref="IChatClient"/> DI registration
/// pattern used by the multi-agent orchestrator. Verifies that:
/// <list type="bullet">
///   <item>Default forwardings route all agent keys to the unkeyed <c>IChatClient</c>.</item>
///   <item>Per-agent overrides replace the default forwarding for a specific key.</item>
///   <item>Config binding for <see cref="AgentConfiguration.ModelConnectionName"/> works.</item>
///   <item>The <see cref="OrchestratorServiceKeys"/> mapping resolves correctly.</item>
/// </list>
/// </summary>
[Trait("Category", "Unit")]
[Trait("Component", "ModelResolution")]
public sealed class KeyedModelResolutionTests
{
    // ─── Default Forwarding ───────────────────────────────────────────

    [Fact]
    public void DefaultForwarding_AllAgentKeys_ResolveToUnkeyedClient()
    {
        // Arrange: register an unkeyed IChatClient and default forwardings
        var defaultClient = A.Fake<IChatClient>();
        var services = new ServiceCollection();
        services.AddSingleton(defaultClient);

        foreach (var key in OrchestratorServiceKeys.AllAgentModelKeys)
        {
            services.AddKeyedSingleton<IChatClient>(
                key,
                (sp, _) => sp.GetRequiredService<IChatClient>());
        }

        var provider = services.BuildServiceProvider();

        // Act & Assert: each keyed resolution should return the same default client
        foreach (var key in OrchestratorServiceKeys.AllAgentModelKeys)
        {
            var resolved = provider.GetRequiredKeyedService<IChatClient>(key);
            Assert.Same(defaultClient, resolved);
        }
    }

    // ─── Per-Agent Override ───────────────────────────────────────────

    [Fact]
    public void PerAgentOverride_LightModel_ResolvesToOverrideClient()
    {
        // Arrange: default forwarding + a dedicated override for light-model
        var defaultClient = A.Fake<IChatClient>();
        var lightClient = A.Fake<IChatClient>();
        var services = new ServiceCollection();
        services.AddSingleton(defaultClient);

        // Step 1: default forwardings (same as AddLuciaAgents)
        foreach (var key in OrchestratorServiceKeys.AllAgentModelKeys)
        {
            services.AddKeyedSingleton<IChatClient>(
                key,
                (sp, _) => sp.GetRequiredService<IChatClient>());
        }

        // Step 2: override for light-model (simulates AddKeyedChatClient + re-register)
        services.AddKeyedSingleton<IChatClient>("light-connection", lightClient);
        services.AddKeyedSingleton<IChatClient>(
            OrchestratorServiceKeys.LightModel,
            (sp, _) => sp.GetRequiredKeyedService<IChatClient>("light-connection"));

        var provider = services.BuildServiceProvider();

        // Act
        var resolvedLight = provider.GetRequiredKeyedService<IChatClient>(OrchestratorServiceKeys.LightModel);
        var resolvedMusic = provider.GetRequiredKeyedService<IChatClient>(OrchestratorServiceKeys.MusicModel);
        var resolvedGeneral = provider.GetRequiredKeyedService<IChatClient>(OrchestratorServiceKeys.GeneralModel);
        var resolvedRouter = provider.GetRequiredKeyedService<IChatClient>(OrchestratorServiceKeys.RouterModel);

        // Assert: light gets override, others get default
        Assert.Same(lightClient, resolvedLight);
        Assert.Same(defaultClient, resolvedMusic);
        Assert.Same(defaultClient, resolvedGeneral);
        Assert.Same(defaultClient, resolvedRouter);
    }

    [Fact]
    public void MultipleOverrides_EachAgent_ResolvesToDedicatedClient()
    {
        // Arrange: separate clients for each agent
        var defaultClient = A.Fake<IChatClient>();
        var lightClient = A.Fake<IChatClient>();
        var musicClient = A.Fake<IChatClient>();
        var generalClient = A.Fake<IChatClient>();
        var routerClient = A.Fake<IChatClient>();

        var services = new ServiceCollection();
        services.AddSingleton(defaultClient);

        // Step 1: default forwardings
        foreach (var key in OrchestratorServiceKeys.AllAgentModelKeys)
        {
            services.AddKeyedSingleton<IChatClient>(
                key,
                (sp, _) => sp.GetRequiredService<IChatClient>());
        }

        // Step 2: per-agent overrides (keyed by connection name, then re-mapped)
        services.AddKeyedSingleton<IChatClient>("conn-light", lightClient);
        services.AddKeyedSingleton<IChatClient>(
            OrchestratorServiceKeys.LightModel,
            (sp, _) => sp.GetRequiredKeyedService<IChatClient>("conn-light"));

        services.AddKeyedSingleton<IChatClient>("conn-music", musicClient);
        services.AddKeyedSingleton<IChatClient>(
            OrchestratorServiceKeys.MusicModel,
            (sp, _) => sp.GetRequiredKeyedService<IChatClient>("conn-music"));

        services.AddKeyedSingleton<IChatClient>("conn-general", generalClient);
        services.AddKeyedSingleton<IChatClient>(
            OrchestratorServiceKeys.GeneralModel,
            (sp, _) => sp.GetRequiredKeyedService<IChatClient>("conn-general"));

        services.AddKeyedSingleton<IChatClient>("conn-router", routerClient);
        services.AddKeyedSingleton<IChatClient>(
            OrchestratorServiceKeys.RouterModel,
            (sp, _) => sp.GetRequiredKeyedService<IChatClient>("conn-router"));

        var provider = services.BuildServiceProvider();

        // Act & Assert: each agent key resolves to its dedicated client
        Assert.Same(lightClient, provider.GetRequiredKeyedService<IChatClient>(OrchestratorServiceKeys.LightModel));
        Assert.Same(musicClient, provider.GetRequiredKeyedService<IChatClient>(OrchestratorServiceKeys.MusicModel));
        Assert.Same(generalClient, provider.GetRequiredKeyedService<IChatClient>(OrchestratorServiceKeys.GeneralModel));
        Assert.Same(routerClient, provider.GetRequiredKeyedService<IChatClient>(OrchestratorServiceKeys.RouterModel));
    }

    // ─── Config Binding ───────────────────────────────────────────────

    [Fact]
    public void ConfigBinding_AgentWithModelConnectionName_BindsCorrectly()
    {
        // Arrange: JSON config with ModelConnectionName set
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Agents:0:AgentName"] = "light-agent",
                ["Agents:0:AgentType"] = "lucia.Agents.Agents.LightAgent",
                ["Agents:0:ModelConnectionName"] = "chat-mini",
                ["Agents:1:AgentName"] = "music-agent",
                ["Agents:1:AgentType"] = "lucia.MusicAgent.MusicAgent",
                ["Agents:1:ModelConnectionName"] = "phi4",
                ["Agents:2:AgentName"] = "general-assistant",
                ["Agents:2:AgentType"] = "lucia.Agents.Agents.GeneralAgent",
                ["Agents:2:ModelConnectionName"] = null, // uses default
            })
            .Build();

        // Act
        var agents = config.GetSection("Agents").Get<List<AgentConfiguration>>();

        // Assert
        Assert.NotNull(agents);
        Assert.Equal(3, agents.Count);

        Assert.Equal("light-agent", agents[0].AgentName);
        Assert.Equal("chat-mini", agents[0].ModelConnectionName);

        Assert.Equal("music-agent", agents[1].AgentName);
        Assert.Equal("phi4", agents[1].ModelConnectionName);

        Assert.Equal("general-assistant", agents[2].AgentName);
        Assert.Null(agents[2].ModelConnectionName);
    }

    [Fact]
    public void ConfigBinding_NullModelConnectionName_SkipsOverride()
    {
        // This simulates what happens in AddLuciaAgents when ModelConnectionName is null
        var agents = new List<AgentConfiguration>
        {
            new() { AgentName = "light-agent", AgentType = "LightAgent", ModelConnectionName = null },
            new() { AgentName = "music-agent", AgentType = "MusicAgent", ModelConnectionName = null },
        };

        // Count how many overrides would be applied
        var overridesApplied = agents
            .Where(a => !string.IsNullOrWhiteSpace(a.ModelConnectionName))
            .Count();

        Assert.Equal(0, overridesApplied);
    }

    // ─── Agent Name → Service Key Mapping ─────────────────────────────

    [Theory]
    [InlineData("light-agent", OrchestratorServiceKeys.LightModel)]
    [InlineData("lightagent", OrchestratorServiceKeys.LightModel)]
    [InlineData("music-agent", OrchestratorServiceKeys.MusicModel)]
    [InlineData("musicagent", OrchestratorServiceKeys.MusicModel)]
    [InlineData("general-assistant", OrchestratorServiceKeys.GeneralModel)]
    [InlineData("generalagent", OrchestratorServiceKeys.GeneralModel)]
    [InlineData("router", OrchestratorServiceKeys.RouterModel)]
    [InlineData("orchestrator", OrchestratorServiceKeys.RouterModel)]
    [InlineData("unknown-agent", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ResolveAgentServiceKey_MapsAgentNameToCorrectKey(string? agentName, string? expectedKey)
    {
        // ResolveAgentServiceKey is private; test the mapping logic directly
        var actualKey = ResolveAgentServiceKeyPublic(agentName);

        Assert.Equal(expectedKey, actualKey);
    }

    // ─── Connection String Availability ───────────────────────────────

    [Fact]
    public void ConnectionStringMissing_OverrideSkipped_FallsBackToDefault()
    {
        // This test simulates the silent failure path in AddLuciaAgents
        // where ModelConnectionName is set but the connection string doesn't exist.
        var defaultClient = A.Fake<IChatClient>();
        var services = new ServiceCollection();
        services.AddSingleton(defaultClient);

        // Default forwardings
        foreach (var key in OrchestratorServiceKeys.AllAgentModelKeys)
        {
            services.AddKeyedSingleton<IChatClient>(
                key,
                (sp, _) => sp.GetRequiredService<IChatClient>());
        }

        // Simulate: ModelConnectionName = "chat-mini" but connection string is missing
        // In AddLuciaAgents this causes `continue` — no override registered
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Agents:0:AgentName"] = "light-agent",
                ["Agents:0:ModelConnectionName"] = "chat-mini",
                // Note: NO ConnectionStrings:chat-mini entry!
            })
            .Build();

        var connectionString = config.GetConnectionString("chat-mini");
        Assert.Null(connectionString); // This is the silent failure

        var provider = services.BuildServiceProvider();

        // Without the override, light-model resolves to the default
        var resolved = provider.GetRequiredKeyedService<IChatClient>(OrchestratorServiceKeys.LightModel);
        Assert.Same(defaultClient, resolved);
    }

    [Fact]
    public void ConnectionStringPresent_OverrideApplied_ResolvesToDedicatedClient()
    {
        // This test verifies the happy path: connection string exists → override works
        var defaultClient = A.Fake<IChatClient>();
        var lightClient = A.Fake<IChatClient>();
        var services = new ServiceCollection();
        services.AddSingleton(defaultClient);

        // Default forwardings
        foreach (var key in OrchestratorServiceKeys.AllAgentModelKeys)
        {
            services.AddKeyedSingleton<IChatClient>(
                key,
                (sp, _) => sp.GetRequiredService<IChatClient>());
        }

        // Simulate: ModelConnectionName = "chat-mini" WITH connection string available
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Agents:0:AgentName"] = "light-agent",
                ["Agents:0:ModelConnectionName"] = "chat-mini",
                ["ConnectionStrings:chat-mini"] = "Endpoint=https://test.openai.azure.com/;Deployment=gpt-4o-mini",
            })
            .Build();

        var connectionString = config.GetConnectionString("chat-mini");
        Assert.NotNull(connectionString);

        // In AddLuciaAgents, this would call builder.AddKeyedChatClient("chat-mini")
        // then re-register. We simulate the final result:
        services.AddKeyedSingleton<IChatClient>("chat-mini", lightClient);
        services.AddKeyedSingleton<IChatClient>(
            OrchestratorServiceKeys.LightModel,
            (sp, _) => sp.GetRequiredKeyedService<IChatClient>("chat-mini"));

        var provider = services.BuildServiceProvider();

        // Light-model should resolve to the dedicated client
        var resolved = provider.GetRequiredKeyedService<IChatClient>(OrchestratorServiceKeys.LightModel);
        Assert.Same(lightClient, resolved);

        // Other agents still get the default
        Assert.Same(defaultClient, provider.GetRequiredKeyedService<IChatClient>(OrchestratorServiceKeys.MusicModel));
    }

    // ─── Diagnostic: Current Config Audit ─────────────────────────────

    [Fact]
    public void DiagnoseCurrentConfig_ReportsModelAssignments()
    {
        // Load the actual test appsettings.json and report what models are configured.
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var evalConfig = new EvalConfiguration();
        config.GetSection("EvalConfiguration").Bind(evalConfig);

        // Pass when at least one model is configured
        Assert.True(
            evalConfig.Models.Count > 0,
            "No models configured — add at least one model to EvalConfiguration:Models.");

        // Each configured model will be used for ALL agents during its test iteration.
        foreach (var model in evalConfig.Models)
        {
            Assert.False(
                string.IsNullOrWhiteSpace(model.DeploymentName),
                "A model entry has an empty DeploymentName.");
        }
    }

    // ─── Private helper: mirrors ResolveAgentServiceKey logic ─────────

    /// <summary>
    /// Public mirror of <c>ServiceCollectionExtensions.ResolveAgentServiceKey</c>
    /// for testing. This must be kept in sync with the production code.
    /// </summary>
    private static string? ResolveAgentServiceKeyPublic(string? agentName)
    {
        if (string.IsNullOrWhiteSpace(agentName))
            return null;

        return agentName.ToLowerInvariant() switch
        {
            "light-agent" or "lightagent" => OrchestratorServiceKeys.LightModel,
            "music-agent" or "musicagent" => OrchestratorServiceKeys.MusicModel,
            "general-assistant" or "generalagent" => OrchestratorServiceKeys.GeneralModel,
            "router" or "orchestrator" => OrchestratorServiceKeys.RouterModel,
            _ => null
        };
    }
}
