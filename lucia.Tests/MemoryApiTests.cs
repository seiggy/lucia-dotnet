using System.Reflection;
using System.Security.Claims;
using System.Text.Json;

using FakeItEasy;

using lucia.AgentHost.Apis;
using lucia.Agents.Abstractions;
using lucia.Agents.Models;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace lucia.Tests;

public sealed class MemoryApiTests
{
    [Fact]
    public void IsAuthorizedForUser_AllowsWhenAuthenticationIsNotConfigured()
    {
        var httpContext = new DefaultHttpContext();

        var isAuthorized = InvokeIsAuthorizedForUser(httpContext, "user-1");

        Assert.True(isAuthorized);
    }

    [Theory]
    [InlineData("GetAllAsync")]
    [InlineData("GetAsync")]
    [InlineData("PutAsync")]
    [InlineData("DeleteAsync")]
    public async Task UserScopedHandlers_ForbidWhenClaimDoesNotMatchRouteUser(string methodName)
    {
        var memoryStore = A.Fake<IMemoryStore>();
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("sub", "user-a"),
            ], "test")),
        };

        var result = await InvokeHandlerAsync(methodName, httpContext, memoryStore);
        var innerResult = result.GetType().GetProperty("Result")?.GetValue(result) ?? result;

        Assert.IsType<ForbidHttpResult>(innerResult);
    }

    private static bool InvokeIsAuthorizedForUser(HttpContext context, string userId)
    {
        var method = typeof(MemoryApi).GetMethod("IsAuthorizedForUser", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var result = method.Invoke(null, [context, userId]);
        Assert.IsType<bool>(result);
        return (bool)result;
    }

    private static async Task<object> InvokeHandlerAsync(string methodName, HttpContext context, IMemoryStore memoryStore)
    {
        var method = typeof(MemoryApi).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        object?[] arguments = methodName switch
        {
            "GetAllAsync" => [context, "user-b", memoryStore, CancellationToken.None],
            "GetAsync" => [context, "user-b", "favorite-color", memoryStore, CancellationToken.None],
            "PutAsync" => [context, "user-b", "favorite-color", JsonDocument.Parse("{\"value\":\"blue\"}").RootElement, memoryStore, CancellationToken.None],
            "DeleteAsync" => [context, "user-b", "favorite-color", memoryStore, CancellationToken.None],
            _ => throw new InvalidOperationException($"Unsupported method '{methodName}'."),
        };

        var task = method.Invoke(null, arguments) as Task;
        Assert.NotNull(task);

        await task.ConfigureAwait(false);

        var result = task.GetType().GetProperty("Result")?.GetValue(task);
        Assert.NotNull(result);
        return result;
    }
}
