using System.Reflection;
using System.Security.Claims;
using System.Text.Json;

using FakeItEasy;

using lucia.AgentHost.Apis;
using lucia.Agents.Abstractions;
using lucia.Agents.Auth;
using lucia.Agents.Models;
using lucia.Data.InMemory;

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

    [Fact]
    public void AdministratorSession_CanManageAnEnrolledUsersMemories()
    {
        Assert.True(InvokeIsAuthorizedForUser(CreateAdministratorContext(), "voice-profile-id"));
    }

    [Fact]
    public async Task PersonalMemoryList_FiltersHistoryInTheStoreBeforeApplyingTheLimit()
    {
        var store = A.Fake<IMemoryStore>();
        A.CallTo(() => store.SearchPersonalAsync("user-b", "coffee", 200, A<CancellationToken>._))
            .Returns([new MemoryEntry("drink", "coffee", DateTime.UtcNow, null)]);

        var result = await InvokeHandlerAsync(
            "GetAllAsync", CreateAdministratorContext(), store, personalOnly: true, query: "coffee");
        var response = Assert.IsType<Ok<IReadOnlyList<MemoryEntry>>>(Unwrap(result));

        Assert.Equal("drink", Assert.Single(response.Value!).Key);
        A.CallTo(() => store.GetAllAsync(A<string>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task EditingMemory_PreservesTheExistingExpiration()
    {
        var store = new InMemoryMemoryStore();
        await store.StoreAsync("user-b", "favorite-color", "red", TimeSpan.FromHours(2));
        var before = Assert.Single(await store.GetAllAsync("user-b"));
        var body = JsonSerializer.SerializeToElement(new { value = "blue", expiresAt = before.ExpiresAt });

        var result = await InvokeHandlerAsync("PutAsync", CreateAdministratorContext(), store, body: body);
        var response = Assert.IsType<Ok<MemoryEntry>>(Unwrap(result));

        Assert.Equal("blue", response.Value!.Value);
        Assert.NotNull(response.Value.ExpiresAt);
        Assert.InRange(response.Value.ExpiresAt.Value - before.ExpiresAt!.Value, TimeSpan.Zero, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task AdministratorDelete_OnlyRemovesTheSelectedUsersEntry()
    {
        var store = new InMemoryMemoryStore();
        await store.StoreAsync("user-a", "favorite-color", "red");
        await store.StoreAsync("user-b", "favorite-color", "blue");

        await InvokeHandlerAsync("DeleteAsync", CreateAdministratorContext(), store);

        Assert.Equal("red", await store.RetrieveAsync("user-a", "favorite-color"));
        Assert.Null(await store.RetrieveAsync("user-b", "favorite-color"));
    }

    [Fact]
    public async Task EditingMemory_RejectsAnExpiredDeadlineWithoutChangingTheValue()
    {
        var store = new InMemoryMemoryStore();
        await store.StoreAsync("user-b", "favorite-color", "red");
        var body = JsonSerializer.SerializeToElement(new { value = "blue", expiresAt = DateTime.UtcNow.AddMinutes(-1) });

        var result = await InvokeHandlerAsync("PutAsync", CreateAdministratorContext(), store, body: body);

        Assert.IsType<BadRequest<string>>(Unwrap(result));
        Assert.Equal("red", await store.RetrieveAsync("user-b", "favorite-color"));
    }

    private static DefaultHttpContext CreateAdministratorContext() => new()
    {
        User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "dashboard-key-id"),
            new Claim(ClaimTypes.Role, AuthOptions.AdministratorRole),
        ], "session")),
    };

    private static object Unwrap(object result) =>
        result.GetType().GetProperty("Result")?.GetValue(result) ?? result;

    private static bool InvokeIsAuthorizedForUser(HttpContext context, string userId)
    {
        var method = typeof(MemoryApi).GetMethod("IsAuthorizedForUser", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var result = method.Invoke(null, [context, userId]);
        Assert.IsType<bool>(result);
        return (bool)result;
    }

    private static async Task<object> InvokeHandlerAsync(
        string methodName,
        HttpContext context,
        IMemoryStore memoryStore,
        bool personalOnly = false,
        string? query = null,
        JsonElement? body = null)
    {
        var method = typeof(MemoryApi).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        object?[] arguments = methodName switch
        {
            "GetAllAsync" => [context, "user-b", memoryStore, CancellationToken.None, personalOnly, query],
            "GetAsync" => [context, "user-b", "favorite-color", memoryStore, CancellationToken.None],
            "PutAsync" => [context, "user-b", "favorite-color", body ?? JsonSerializer.SerializeToElement(new { value = "blue" }), memoryStore, CancellationToken.None],
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
