using lucia.Agents.Abstractions;
using lucia.Agents.Family;

namespace lucia.Tests.Data.Providers;

public abstract class LlmUsageStoreContract
{
    private static readonly DateTime Now = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    protected abstract Task<StoreScope<ILlmUsageStore>> CreateAsync();

    private static LlmUsageRecord Usage(string id, string memberId, DateTime at, decimal cost) => new()
    {
        Id = id,
        MemberId = memberId,
        Timestamp = at,
        ProviderId = "openai",
        Model = "gpt-test",
        InputTokens = 100,
        OutputTokens = 20,
        CostUsd = cost,
    };

    [Fact]
    public async Task GetTotalsAsync_SumsOnlyTheMembersRecordsSinceTheCutoff()
    {
        await using var scope = await CreateAsync();
        await scope.Store.RecordAsync(Usage("old", "kid", Now.AddDays(-2), 1.00m));
        await scope.Store.RecordAsync(Usage("r1", "kid", Now.AddHours(-1), 0.0125m));
        await scope.Store.RecordAsync(Usage("r2", "kid", Now, 0.0250m));
        await scope.Store.RecordAsync(Usage("other", "parent", Now, 5m));

        var totals = await scope.Store.GetTotalsAsync("kid", Now.AddDays(-1));

        Assert.Equal(new LlmUsageTotals(2, 200, 40, 0.0375m), totals);
    }

    [Fact]
    public async Task GetTotalsAsync_NoRecords_ReturnsEmpty()
    {
        await using var scope = await CreateAsync();

        Assert.Equal(LlmUsageTotals.Empty, await scope.Store.GetTotalsAsync("kid", Now.AddDays(-1)));
    }

    [Fact]
    public async Task ListRecentAsync_ReturnsNewestFirstWithinTheLimit()
    {
        await using var scope = await CreateAsync();
        await scope.Store.RecordAsync(Usage("r1", "kid", Now.AddMinutes(-2), 0.01m));
        await scope.Store.RecordAsync(Usage("r2", "kid", Now, 0.01m));
        await scope.Store.RecordAsync(Usage("r3", "kid", Now.AddMinutes(-1), 0.01m));

        var recent = await scope.Store.ListRecentAsync("kid", 2);

        Assert.Equal(["r2", "r3"], recent.Select(r => r.Id));
    }
}
