using lucia.Agents.Abstractions;
using lucia.Agents.Family;

namespace lucia.Tests.Data.Providers;

public abstract class ActionRecordStoreContract
{
    private static readonly DateTime Now = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    protected abstract Task<StoreScope<IActionRecordStore>> CreateAsync();

    private static ActionRecord Action(string id, string requester, DateTime createdAt, params string[] approvers) => new()
    {
        Id = id,
        RequesterId = requester,
        Kind = CapabilityKind.Tool,
        CapabilityId = "climate.set_temperature",
        ArgumentsJson = """{"entity":"climate.bedroom","temperature":24}""",
        PayloadHash = $"hash-{id}",
        Summary = "Set the bedroom to 24 C",
        ApproverIds = [.. approvers],
        CreatedAt = createdAt,
        UpdatedAt = createdAt,
        ExpiresAt = createdAt.AddMinutes(30),
    };

    [Fact]
    public async Task CreateAsync_RoundTripsTheRecord()
    {
        await using var scope = await CreateAsync();
        await scope.Store.CreateAsync(Action("a1", "kid", Now, "parent"));

        var loaded = await scope.Store.GetAsync("a1");

        Assert.NotNull(loaded);
        Assert.Equal(ActionStatus.AwaitingDecision, loaded.Status);
        Assert.Equal("hash-a1", loaded.PayloadHash);
        Assert.Equal(["parent"], loaded.ApproverIds);
        Assert.Contains("climate.bedroom", loaded.ArgumentsJson);
        Assert.Null(await scope.Store.GetAsync("missing"));
    }

    [Fact]
    public async Task TryUpdateAsync_AppliesOnlyFromTheExpectedStatus()
    {
        await using var scope = await CreateAsync();
        await scope.Store.CreateAsync(Action("a1", "kid", Now, "parent"));
        var approved = (await scope.Store.GetAsync("a1"))!;
        approved.Status = ActionStatus.Approved;
        approved.DecidedBy = "parent";

        Assert.True(await scope.Store.TryUpdateAsync(approved, ActionStatus.AwaitingDecision));
        Assert.False(await scope.Store.TryUpdateAsync(approved, ActionStatus.AwaitingDecision));

        var loaded = await scope.Store.GetAsync("a1");
        Assert.Equal(ActionStatus.Approved, loaded!.Status);
        Assert.Equal("parent", loaded.DecidedBy);
    }

    [Fact]
    public async Task TryUpdateAsync_ConcurrentClaimsSucceedOnce()
    {
        await using var scope = await CreateAsync();
        var record = Action("a1", "kid", Now, "parent");
        record.Status = ActionStatus.Approved;
        await scope.Store.CreateAsync(record);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            var claim = (await scope.Store.GetAsync("a1"))!;
            claim.Status = ActionStatus.Executing;
            return await scope.Store.TryUpdateAsync(claim, ActionStatus.Approved);
        }));

        Assert.Single(results, r => r);
    }

    [Fact]
    public async Task ListAwaitingDecisionAsync_ReturnsOnlyPendingRecordsForThatApproverOldestFirst()
    {
        await using var scope = await CreateAsync();
        await scope.Store.CreateAsync(Action("newer", "kid", Now, "parent", "other-parent"));
        await scope.Store.CreateAsync(Action("older", "kid", Now.AddMinutes(-5), "parent"));
        await scope.Store.CreateAsync(Action("not-mine", "kid", Now, "other-parent"));
        var decided = Action("decided", "kid", Now, "parent");
        decided.Status = ActionStatus.Denied;
        await scope.Store.CreateAsync(decided);

        var pending = await scope.Store.ListAwaitingDecisionAsync("parent");

        Assert.Equal(["older", "newer"], pending.Select(a => a.Id));
    }

    [Fact]
    public async Task ListForRequesterAsync_ReturnsNewestFirstWithinTheLimit()
    {
        await using var scope = await CreateAsync();
        await scope.Store.CreateAsync(Action("a1", "kid", Now.AddMinutes(-2), "parent"));
        await scope.Store.CreateAsync(Action("a2", "kid", Now, "parent"));
        await scope.Store.CreateAsync(Action("a3", "kid", Now.AddMinutes(-1), "parent"));
        await scope.Store.CreateAsync(Action("b1", "parent", Now, "parent"));

        var mine = await scope.Store.ListForRequesterAsync("kid", 2);

        Assert.Equal(["a2", "a3"], mine.Select(a => a.Id));
    }
}
