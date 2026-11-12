using System.Reflection;
using FakeItEasy;
using lucia.AgentHost.Apis;
using lucia.Agents.Abstractions;
using lucia.Agents.Registry;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace lucia.Tests;

public sealed class AgentRegistryApiTests
{
    [Theory]
    [InlineData("mailto:foo@bar.com")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com/resource")]
    public async Task RegisterAgentAsync_NonHttpScheme_ReturnsBadRequest(string agentId)
    {
        var registry = A.Fake<IAgentRegistry>();
        var httpClientFactory = A.Fake<IHttpClientFactory>();
        var loggerFactory = A.Fake<ILoggerFactory>();
        A.CallTo(() => loggerFactory.CreateLogger(A<string>._)).Returns(A.Fake<ILogger>());

        var result = await InvokeRegisterAsync(agentId, registry, httpClientFactory, loggerFactory);

        Assert.Equal(StatusCodes.Status400BadRequest, ExtractStatusCode(result));
    }

    [Theory]
    [InlineData("mailto:foo@bar.com")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com/resource")]
    public async Task UpdateAgentAsync_NonHttpScheme_ReturnsBadRequest(string agentId)
    {
        var registry = A.Fake<IAgentRegistry>();
        var httpClientFactory = A.Fake<IHttpClientFactory>();

        var result = await InvokeUpdateAsync(agentId, registry, httpClientFactory);

        Assert.Equal(StatusCodes.Status400BadRequest, ExtractStatusCode(result));
    }

    [Fact]
    public async Task UnregisterAgentAsync_EvictsPromptCaches()
    {
        var registry = A.Fake<IAgentRegistry>();
        var promptCache = A.Fake<IPromptCacheService>();

        await InvokeUnregisterAsync("https://agents.example.com/custom", registry, promptCache);

        A.CallTo(() => promptCache.EvictAllAsync(A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => promptCache.EvictAllChatEntriesAsync(A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task UpdateAgentAsync_EncodedSlashesInTheRoute_LooksUpTheDecodedAddress()
    {
        // ASP.NET Core leaves %2F encoded in route values, so the dashboard's encoded URL arrives like this.
        var registry = A.Fake<IAgentRegistry>();
        var httpClientFactory = A.Fake<IHttpClientFactory>();
        A.CallTo(() => registry.GetAgentAsync(A<string>._, A<CancellationToken>._)).Returns(Task.FromResult<A2A.AgentCard?>(null));

        var result = await InvokeUpdateAsync("http:%2F%2Fagent-host%3A8080%2Fa2a%2Flight-agent", registry, httpClientFactory);

        Assert.Equal(StatusCodes.Status404NotFound, ExtractStatusCode(result));
        A.CallTo(() => registry.GetAgentAsync("http://agent-host:8080/a2a/light-agent", A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task UnregisterAgentAsync_AnAddressWithALiteralPercentEscape_SurvivesTheDoubleEncoding()
    {
        // The dashboard encodes the address twice. Kestrel decodes one layer, so a literal %2F in the address arrives as %252F.
        var registry = A.Fake<IAgentRegistry>();

        await AgentRegistryApi.UnregisterAgentAsync(registry, "http%3A%2F%2Fagent-host%2Fa2a%2Fx%252Fy", A.Fake<IPromptCacheService>(), CancellationToken.None);

        A.CallTo(() => registry.UnregisterAgentAsync("http://agent-host/a2a/x%2Fy", A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task UnregisterAgentAsync_EncodedSlashesInTheRoute_RemovesTheDecodedAddress()
    {
        var registry = A.Fake<IAgentRegistry>();

        await AgentRegistryApi.UnregisterAgentAsync(registry, "http:%2F%2Fagent-host%3A8080%2Fa2a%2Flight-agent", A.Fake<IPromptCacheService>(), CancellationToken.None);

        A.CallTo(() => registry.UnregisterAgentAsync("http://agent-host:8080/a2a/light-agent", A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }
    private static async Task<IResult> InvokeRegisterAsync(
        string agentId,
        IAgentRegistry registry,
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory)
    {
        var method = typeof(AgentRegistryApi).GetMethod(
            "RegisterAgentAsync",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var task = (Task)method.Invoke(null, [registry, httpClientFactory, loggerFactory, agentId, A.Fake<IPromptCacheService>(), CancellationToken.None])!;
        await task.ConfigureAwait(false);
        var resultProperty = task.GetType().GetProperty("Result");
        Assert.NotNull(resultProperty);
        return (IResult)resultProperty.GetValue(task)!;
    }

    private static async Task<IResult> InvokeUpdateAsync(
        string agentId,
        IAgentRegistry registry,
        IHttpClientFactory httpClientFactory)
    {
        var method = typeof(AgentRegistryApi).GetMethod(
            "UpdateAgentAsync",
            BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        var task = (Task)method.Invoke(null, [registry, httpClientFactory, agentId, A.Fake<IPromptCacheService>(), CancellationToken.None])!;
        await task.ConfigureAwait(false);
        var resultProperty = task.GetType().GetProperty("Result");
        Assert.NotNull(resultProperty);
        return (IResult)resultProperty.GetValue(task)!;
    }

    private static async Task<IResult> InvokeUnregisterAsync(
        string agentId,
        IAgentRegistry registry,
        IPromptCacheService promptCache)
    {
        var method = typeof(AgentRegistryApi).GetMethod(
            "UnregisterAgentAsync",
            BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        var task = (Task)method.Invoke(null, [registry, agentId, promptCache, CancellationToken.None])!;
        await task.ConfigureAwait(false);
        var resultProperty = task.GetType().GetProperty("Result");
        Assert.NotNull(resultProperty);
        return (IResult)resultProperty.GetValue(task)!;
    }

    // Results<T1,T2,T3> exposes the active IResult via a public Result property.
    // We read it directly to check the status code without needing to execute the result
    // (which would require a fully wired HttpContext + service provider for JSON serialization).
    private static int ExtractStatusCode(IResult result)
    {
        var resultProperty = result.GetType()
            .GetProperty("Result", BindingFlags.Public | BindingFlags.Instance);
        var inner = resultProperty?.GetValue(result) as IStatusCodeHttpResult;
        Assert.NotNull(inner);
        return inner.StatusCode ?? 0;
    }
}
