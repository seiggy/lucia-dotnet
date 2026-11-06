using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FakeItEasy;
using lucia.AgentHost.Apis;
using lucia.AgentHost.Auth;
using lucia.Agents.Abstractions;
using lucia.Agents.Auth;
using lucia.Agents.Family;
using lucia.Data.Sqlite;
using lucia.Tests.Data.Providers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests.Family;

/// <summary>
/// Exercises the real authentication, authorization, and family endpoints over HTTP:
/// administrator setup, QR pairing, member-only access, and guardian approvals.
/// </summary>
public sealed class FamilyHttpFlowTests : IAsyncLifetime
{
    private SqliteStoreEnvironment _environment = null!;
    private WebApplication _app = null!;
    private HttpClient _anonymous = null!;
    private SqliteApiKeyService _keys = null!;
    private string _adminKey = null!;

    public async Task InitializeAsync()
    {
        _environment = await SqliteStoreEnvironment.CreateAsync();
        _keys = new SqliteApiKeyService(_environment.Config, NullLogger<SqliteApiKeyService>.Instance);
        _adminKey = (await _keys.CreateKeyAsync("portal")).Key;

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IApiKeyService>(_keys);
        builder.Services.AddSingleton<IFamilyMemberStore>(new SqliteFamilyMemberStore(_environment.Config));
        builder.Services.AddSingleton<IDevicePairingStore>(new SqliteDevicePairingStore(_environment.Config));
        builder.Services.AddSingleton<IActionRecordStore>(new SqliteActionRecordStore(_environment.Config));
        builder.Services.AddSingleton<ILlmUsageStore>(new SqliteLlmUsageStore(_environment.Config));
        builder.Services.AddSingleton(A.Fake<IModelProviderRepository>());
        builder.Services.AddSingleton(A.Fake<IConfigStoreWriter>());
        builder.Services.AddSingleton(A.Fake<ISessionService>());
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.Configure<AuthOptions>(_ => { });
        builder.Services.AddLuciaFamilyAccess(new OidcAuthOptions());

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.UseRateLimiter();
        _app.MapAuthApi();
        _app.MapPairingApi(requireAdministrator: false);
        _app.MapFamilyApi(requireAdministrator: false);
        _app.MapMeApi();
        await _app.StartAsync();
        _anonymous = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _anonymous.Dispose();
        await _app.DisposeAsync();
        _environment.Dispose();
    }

    private HttpClient Client(string apiKey)
    {
        var client = _app.GetTestClient();
        client.DefaultRequestHeaders.Add(AuthOptions.ApiKeyHeaderName, apiKey);
        return client;
    }

    private async Task<string> CreateMemberAsync(HttpClient admin, string name, FamilyRole role, params string[] guardians)
    {
        var response = await admin.PostAsJsonAsync("/api/family/members", new { displayName = name, role = role.ToString(), guardianIds = guardians });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
    }

    private async Task<string> PairAsync(HttpClient admin, string memberId, string deviceName)
    {
        var pairing = await admin.PostAsJsonAsync($"/api/family/members/{memberId}/pairings", new { });
        Assert.Equal(HttpStatusCode.OK, pairing.StatusCode);
        var body = await pairing.Content.ReadFromJsonAsync<JsonElement>();
        Assert.StartsWith("lucia://pair?server=", body.GetProperty("pairingUri").GetString());

        var redeemed = await _anonymous.PostAsJsonAsync("/api/pairing/redeem", new { code = body.GetProperty("code").GetString(), deviceName });
        Assert.Equal(HttpStatusCode.OK, redeemed.StatusCode);
        return (await redeemed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("apiKey").GetString()!;
    }

    [Fact]
    public async Task PairedMembers_ReachOnlyMemberEndpoints_AndGuardiansDecideApprovals()
    {
        using var admin = Client(_adminKey);
        var parentId = await CreateMemberAsync(admin, "Alex", FamilyRole.Adult);
        var childId = await CreateMemberAsync(admin, "Sam", FamilyRole.Child, parentId);
        var childKey = await PairAsync(admin, childId, "Sam's iPad");
        var parentKey = await PairAsync(admin, parentId, "Alex's phone");
        using var child = Client(childKey);
        using var parent = Client(parentKey);

        var me = await child.GetFromJsonAsync<JsonElement>("/api/me");
        Assert.Equal(childId, me.GetProperty("id").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await child.GetAsync("/api/family/members")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await child.PostAsJsonAsync($"/api/family/members/{childId}/pairings", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _anonymous.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client("lk_not-a-key").GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _anonymous.PostAsJsonAsync("/api/auth/login", new { apiKey = childKey })).StatusCode);

        var broker = _app.Services.GetRequiredService<ActionBroker>();
        var childMember = (await _app.Services.GetRequiredService<IFamilyMemberStore>().GetAsync(childId))!;
        var pending = await broker.AuthorizeToolAsync(childMember, "climate-agent", "SetClimateTemperatureAsync",
            new Dictionary<string, object?> { ["temperature"] = 24 });
        Assert.Equal(BrokerOutcomeKind.Pending, pending.Kind);

        var approvals = await parent.GetFromJsonAsync<JsonElement>("/api/me/approvals");
        var approval = Assert.Single(approvals.EnumerateArray());
        Assert.Equal("Sam", approval.GetProperty("requester").GetProperty("displayName").GetString());
        Assert.Equal(24, approval.GetProperty("arguments").GetProperty("temperature").GetInt32());
        Assert.Empty((await child.GetFromJsonAsync<JsonElement>("/api/me/approvals")).EnumerateArray());

        var id = pending.Record!.Id;
        Assert.Equal(HttpStatusCode.NotFound, (await child.PostAsync($"/api/me/approvals/{id}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await parent.PostAsync($"/api/me/approvals/{id}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await parent.PostAsync($"/api/me/approvals/{id}/deny", null)).StatusCode);

        var requests = await child.GetFromJsonAsync<JsonElement>("/api/me/requests");
        Assert.Equal("Approved", Assert.Single(requests.EnumerateArray()).GetProperty("status").GetString());
    }

    [Fact]
    public async Task DeletingAMember_RevokesTheirDeviceKeys()
    {
        using var admin = Client(_adminKey);
        var guestId = await CreateMemberAsync(admin, "Visitor", FamilyRole.Guest);
        var guestKey = await PairAsync(admin, guestId, "Guest phone");
        using var guest = Client(guestKey);
        Assert.Equal(HttpStatusCode.OK, (await guest.GetAsync("/api/me")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/family/members/{guestId}")).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync("/api/me")).StatusCode);
    }

    [Fact]
    public async Task PairingCodes_AreSingleUse()
    {
        using var admin = Client(_adminKey);
        var memberId = await CreateMemberAsync(admin, "Alex", FamilyRole.Adult);
        var pairing = await (await admin.PostAsJsonAsync($"/api/family/members/{memberId}/pairings", new { }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var code = pairing.GetProperty("code").GetString();

        Assert.Equal(HttpStatusCode.OK, (await _anonymous.PostAsJsonAsync("/api/pairing/redeem", new { code })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _anonymous.PostAsJsonAsync("/api/pairing/redeem", new { code })).StatusCode);
    }
}
