using FakeItEasy;
using lucia.Agents.Abstractions;
using lucia.Agents.Configuration;
using lucia.Agents.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests.Services;

public sealed class PluginToolRegistryTests
{
    [Fact]
    public async Task DiscoveryAndResolution_InitializeOnceAndOnlyResolveSelectedTools()
    {
        var weather = CreateProvider("Flight weather", "Check conditions before answering.", "conditions", "forecast");
        var other = CreateProvider("Other", "Do not include this hint.", "other");
        var registry = new PluginToolRegistry([weather, other], NullLogger<PluginToolRegistry>.Instance);

        var catalogs = await Task.WhenAll(registry.GetProvidersAsync(), registry.GetProvidersAsync());
        var provider = Assert.Single(catalogs[0], p => p.Name == "Flight weather");
        Assert.Equal("plugin:Flight weather", provider.ServerId);
        Assert.Equal(["conditions", "forecast"], provider.Tools.Select(t => t.ToolName));
        Assert.All(provider.Tools, tool => Assert.Equal(provider.ServerId, tool.ServerId));

        var (tools, hints) = await registry.ResolveToolsAsync(
        [
            new AgentToolReference { ServerId = provider.ServerId, ToolName = "conditions" },
            new AgentToolReference { ServerId = provider.ServerId, ToolName = "conditions" },
        ]);

        Assert.Equal("conditions", Assert.Single(tools).Name);
        Assert.Equal("Check conditions before answering.", Assert.Single(hints));
        A.CallTo(() => weather.InitializeAsync(A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => other.InitializeAsync(A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        Assert.Equal("conditions", (await Assert.IsAssignableFrom<AIFunction>(tools[0]).InvokeAsync())?.ToString());
    }

    [Fact]
    public async Task FailedProvider_IsVisibleButHasNoCallableToolsOrHints()
    {
        var broken = CreateProvider("Broken", "Never include.", "broken");
        A.CallTo(() => broken.InitializeAsync(A<CancellationToken>._))
            .ThrowsAsync(new InvalidOperationException("Credentials missing"));
        var registry = new PluginToolRegistry([broken], NullLogger<PluginToolRegistry>.Instance);

        var provider = Assert.Single(await registry.GetProvidersAsync());
        var (tools, hints) = await registry.ResolveToolsAsync();

        Assert.Equal("Credentials missing", provider.ErrorMessage);
        Assert.Empty(provider.Tools);
        Assert.Empty(tools);
        Assert.Empty(hints);
        A.CallTo(() => broken.GetTools()).MustNotHaveHappened();
    }

    [Fact]
    public async Task MissingReferences_AreSkippedWithoutAddingOtherTools()
    {
        var registry = new PluginToolRegistry(
            [CreateProvider("Weather", "Hint", "conditions")], NullLogger<PluginToolRegistry>.Instance);

        var (tools, hints) = await registry.ResolveToolsAsync(
        [
            new AgentToolReference { ServerId = "plugin:Uninstalled", ToolName = "conditions" },
            new AgentToolReference { ServerId = "plugin:Weather", ToolName = "removed_tool" },
        ]);

        Assert.Empty(tools);
        Assert.Empty(hints);
    }

    [Fact]
    public async Task InitializationCancellation_IsPropagatedAndCanBeRetried()
    {
        var provider = CreateProvider("Weather", null, "conditions");
        A.CallTo(() => provider.InitializeAsync(A<CancellationToken>._))
            .ThrowsAsync(new OperationCanceledException());
        var registry = new PluginToolRegistry([provider], NullLogger<PluginToolRegistry>.Instance);

        await Assert.ThrowsAsync<OperationCanceledException>(() => registry.GetProvidersAsync());

        A.CallTo(() => provider.InitializeAsync(A<CancellationToken>._)).Returns(Task.CompletedTask);
        Assert.Single((await registry.GetProvidersAsync())[0].Tools);
    }

    private static IAgentToolProvider CreateProvider(string name, string? hint, params string[] toolNames)
    {
        var provider = A.Fake<IAgentToolProvider>();
        A.CallTo(() => provider.Name).Returns(name);
        A.CallTo(() => provider.Description).Returns($"Tools from {name}");
        A.CallTo(() => provider.InstructionHint).Returns(hint);
        A.CallTo(() => provider.GetTools()).Returns(
            toolNames.Select(name => (AITool)AIFunctionFactory.Create(() => name, name)).ToList());
        return provider;
    }
}
