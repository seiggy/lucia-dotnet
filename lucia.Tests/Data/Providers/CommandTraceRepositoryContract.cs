using lucia.Agents.CommandTracing;

namespace lucia.Tests.Data.Providers;

/// <summary>
/// Behaviour every <see cref="ICommandTraceRepository"/> provider must share.
/// </summary>
public abstract class CommandTraceRepositoryContract
{
    protected abstract Task<StoreScope<ICommandTraceRepository>> CreateAsync();

    [Fact]
    public async Task SaveAsync_And_GetByIdAsync_RoundTripRealState()
    {
        await using var scope = await CreateAsync();

        var trace = CreateTrace(
            "cmd-1",
            DateTimeOffset.UtcNow.AddDays(-1),
            "turn on the bedroom lights",
            CommandTraceOutcome.CommandHandled,
            "scene-skill",
            0.92f);

        await scope.Store.SaveAsync(trace);

        var result = await scope.Store.GetByIdAsync(trace.Id);

        Assert.NotNull(result);
        Assert.Equal(trace.Id, result.Id);
        Assert.Equal("turn on the bedroom lights", result.CleanText);
        Assert.Equal("scene-skill", result.Match.SkillId);
        Assert.Equal(CommandTraceOutcome.CommandHandled, result.Outcome);
        Assert.Equal(128.5, result.TotalDurationMs);
    }

    [Fact]
    public async Task ListAsync_FiltersBySearchOutcomeSkillAndDate_AndPagesCorrectly()
    {
        await using var scope = await CreateAsync();

        var older = CreateTrace("cmd-old", DateTimeOffset.UtcNow.AddDays(-40), "weather report", CommandTraceOutcome.LlmFallback, "weather-skill", 0.71f);
        var target = CreateTrace("cmd-target", DateTimeOffset.UtcNow.AddDays(-10), "turn on the kitchen lights", CommandTraceOutcome.CommandHandled, "scene-skill", 0.94f);
        var newest = CreateTrace("cmd-new", DateTimeOffset.UtcNow.AddDays(-2), "set thermostat to 72", CommandTraceOutcome.CommandHandled, "climate-skill", 0.88f);

        await scope.Store.SaveAsync(older);
        await scope.Store.SaveAsync(target);
        await scope.Store.SaveAsync(newest);

        var byOutcome = await scope.Store.ListAsync(new CommandTraceFilter { Outcome = CommandTraceOutcome.CommandHandled, Page = 1, PageSize = 10 });
        Assert.Equal(2, byOutcome.TotalCount);

        var bySearch = await scope.Store.ListAsync(new CommandTraceFilter { Search = "weather", Page = 1, PageSize = 10 });
        var weather = Assert.Single(bySearch.Items);
        Assert.Equal("cmd-old", weather.Id);

        var bySkill = await scope.Store.ListAsync(new CommandTraceFilter { SkillId = "scene-skill", Page = 1, PageSize = 10 });
        var skillMatch = Assert.Single(bySkill.Items);
        Assert.Equal("cmd-target", skillMatch.Id);

        var byDate = await scope.Store.ListAsync(new CommandTraceFilter { FromDate = DateTime.UtcNow.AddDays(-30), ToDate = DateTime.UtcNow.AddDays(-1), Page = 1, PageSize = 10 });
        Assert.Equal(2, byDate.TotalCount);

        var pageTwo = await scope.Store.ListAsync(new CommandTraceFilter { Page = 2, PageSize = 1 });
        Assert.Single(pageTwo.Items);
        Assert.Equal(3, pageTwo.TotalCount);
    }

    [Fact]
    public async Task GetStatsAsync_AggregatesOutcomeSkillAndDurationMetrics()
    {
        await using var scope = await CreateAsync();

        await scope.Store.SaveAsync(CreateTrace("cmd-a", DateTimeOffset.UtcNow.AddHours(-2), "turn on light", CommandTraceOutcome.CommandHandled, "scene-skill", 0.97f));
        await scope.Store.SaveAsync(CreateTrace("cmd-b", DateTimeOffset.UtcNow.AddHours(-1), "set thermostat", CommandTraceOutcome.CommandHandled, "scene-skill", 0.86f));
        await scope.Store.SaveAsync(CreateTrace("cmd-c", DateTimeOffset.UtcNow.AddMinutes(-30), "what is the weather", CommandTraceOutcome.LlmFallback, "weather-skill", 0.41f));
        await scope.Store.SaveAsync(CreateTrace("cmd-d", DateTimeOffset.UtcNow.AddMinutes(-15), "broken input", CommandTraceOutcome.Error, "scene-skill", 0.10f, error: "boom"));

        var stats = await scope.Store.GetStatsAsync();

        Assert.Equal(4, stats.TotalCount);
        Assert.Equal(2, stats.CommandHandledCount);
        Assert.Equal(1, stats.LlmFallbackCount);
        Assert.Equal(1, stats.ErrorCount);
        Assert.Equal(3, stats.BySkill["scene-skill"]);
        Assert.Equal(1, stats.BySkill["weather-skill"]);
        Assert.True(stats.AvgDurationMs > 0d);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenMissing()
    {
        await using var scope = await CreateAsync();

        Assert.Null(await scope.Store.GetByIdAsync("missing-trace-id"));
    }

    private static CommandTrace CreateTrace(
        string id,
        DateTimeOffset timestamp,
        string text,
        CommandTraceOutcome outcome,
        string skillId,
        float confidence,
        string? error = null)
        => new()
        {
            Id = id,
            Timestamp = timestamp,
            RawText = text,
            CleanText = text,
            RequestContext = new CommandTraceContext { DeviceId = "living-room" },
            Match = new CommandTraceMatch
            {
                IsMatch = outcome == CommandTraceOutcome.CommandHandled,
                Confidence = confidence,
                SkillId = skillId,
                MatchDurationMs = 8.5,
            },
            Outcome = outcome,
            TotalDurationMs = 128.5,
            Error = error,
        };
}
