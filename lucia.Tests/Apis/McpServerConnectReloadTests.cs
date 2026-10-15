using FakeItEasy;
using lucia.Agents.Abstractions;
using lucia.AgentHost.Apis;
using Microsoft.AspNetCore.Http.HttpResults;

namespace lucia.Tests.Apis;

/// <summary>
/// Regression tests for the MCP reconnect bug: a manual /api/mcp-servers/{id}/connect disposes the
/// cached McpClient, but already-built dynamic agents keep tool wrappers bound to that disposed
/// client until the agent set is rebuilt. A successful connect must therefore trigger a dynamic
/// agent reload so tools are re-resolved against the fresh client.
/// </summary>
public sealed class McpServerConnectReloadTests
{
    [Fact]
    public async Task Connect_Succeeds_ReloadsDynamicAgents()
    {
        var registry = A.Fake<IMcpToolRegistry>();
        var loader = A.Fake<IDynamicAgentLoader>();

        var result = await McpServerApi.ConnectCoreAsync("srv-1", registry, loader, CancellationToken.None);

        var response = Assert.IsType<Ok<string>>(Unwrap(result));
        Assert.Equal("Connected", response.Value);
        A.CallTo(() => loader.ReloadAsync(A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Connect_Succeeds_PassesRequestTokenToReload()
    {
        var registry = A.Fake<IMcpToolRegistry>();
        var loader = A.Fake<IDynamicAgentLoader>();
        using var cts = new CancellationTokenSource();

        await McpServerApi.ConnectCoreAsync("srv-1", registry, loader, cts.Token);

        A.CallTo(() => loader.ReloadAsync(cts.Token)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Connect_Fails_DoesNotReload()
    {
        var registry = A.Fake<IMcpToolRegistry>();
        A.CallTo(() => registry.ConnectServerAsync("srv-1", A<CancellationToken>._))
            .Throws(new InvalidOperationException("boom"));
        var loader = A.Fake<IDynamicAgentLoader>();

        var result = await McpServerApi.ConnectCoreAsync("srv-1", registry, loader, CancellationToken.None);

        Assert.IsType<ProblemHttpResult>(Unwrap(result));
        A.CallTo(() => loader.ReloadAsync(A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task Connect_Succeeds_ButReloadFails_ReturnsProblem()
    {
        var registry = A.Fake<IMcpToolRegistry>();
        var loader = A.Fake<IDynamicAgentLoader>();
        A.CallTo(() => loader.ReloadAsync(A<CancellationToken>._))
            .Throws(new InvalidOperationException("db down"));

        var result = await McpServerApi.ConnectCoreAsync("srv-1", registry, loader, CancellationToken.None);

        Assert.IsType<ProblemHttpResult>(Unwrap(result));
    }

    private static object Unwrap(object result) =>
        result.GetType().GetProperty("Result")?.GetValue(result) ?? result;
}
