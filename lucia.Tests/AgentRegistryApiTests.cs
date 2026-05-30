using System.Reflection;
using FakeItEasy;
using lucia.AgentHost.Apis;
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
        var task = (Task)method.Invoke(null, [registry, httpClientFactory, loggerFactory, agentId, CancellationToken.None])!;
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
        var task = (Task)method.Invoke(null, [registry, httpClientFactory, agentId, CancellationToken.None])!;
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
