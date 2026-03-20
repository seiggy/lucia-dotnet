using lucia.AgentHost.Conversation.Tracing;
using lucia.Agents.CommandTracing;
using lucia.Wyoming.CommandRouting;

namespace lucia.Tests.Conversation;

public sealed class CommandTraceRecordingTests
{
    [Fact]
    public async Task InMemoryRepository_SaveAndRetrieve_RoundTrips()
    {
        var repo = new InMemoryCommandTraceRepository();
        var trace = CreateTrace("t1", CommandTraceOutcome.CommandHandled, "turn on bedroom light");

        await repo.SaveAsync(trace);

        var retrieved = await repo.GetByIdAsync("t1");
        Assert.NotNull(retrieved);
        Assert.Equal("t1", retrieved.Id);
        Assert.Equal("turn on bedroom light", retrieved.RawText);
        Assert.Equal(CommandTraceOutcome.CommandHandled, retrieved.Outcome);
    }

    [Fact]
    public async Task InMemoryRepository_GetById_ReturnsNullForMissing()
    {
        var repo = new InMemoryCommandTraceRepository();

        var result = await repo.GetByIdAsync("nonexistent");

        Assert.Null(result);
    }

    [Fact]
    public async Task InMemoryRepository_EvictsOldest_AtCapacity()
    {
        var repo = new InMemoryCommandTraceRepository();

        for (var i = 0; i < InMemoryCommandTraceRepository.MaxCapacity + 50; i++)
        {
            await repo.SaveAsync(CreateTrace($"t{i}", CommandTraceOutcome.CommandHandled, $"input {i}"));
        }

        // Oldest 50 should be evicted
        Assert.Null(await repo.GetByIdAsync("t0"));
        Assert.Null(await repo.GetByIdAsync("t49"));

        // Most recent should still be present
        Assert.NotNull(await repo.GetByIdAsync("t500"));
        Assert.NotNull(await repo.GetByIdAsync("t549"));

        var stats = await repo.GetStatsAsync();
        Assert.Equal(InMemoryCommandTraceRepository.MaxCapacity, stats.TotalCount);
    }

    [Fact]
    public async Task InMemoryRepository_Stats_AggregatesCorrectly()
    {
        var repo = new InMemoryCommandTraceRepository();

        await repo.SaveAsync(CreateTrace("t1", CommandTraceOutcome.CommandHandled, "turn on light", skillId: "LightControlSkill"));
        await repo.SaveAsync(CreateTrace("t2", CommandTraceOutcome.CommandHandled, "set temp", skillId: "ClimateControlSkill"));
        await repo.SaveAsync(CreateTrace("t3", CommandTraceOutcome.LlmFallback, "what's the weather"));
        await repo.SaveAsync(CreateTrace("t4", CommandTraceOutcome.Error, "broken input", error: "oops"));
        await repo.SaveAsync(CreateTrace("t5", CommandTraceOutcome.CommandHandled, "turn off light", skillId: "LightControlSkill"));

        var stats = await repo.GetStatsAsync();

        Assert.Equal(5, stats.TotalCount);
        Assert.Equal(3, stats.CommandHandledCount);
        Assert.Equal(1, stats.LlmFallbackCount);
        Assert.Equal(1, stats.ErrorCount);
        Assert.Equal(2, stats.BySkill["LightControlSkill"]);
        Assert.Equal(1, stats.BySkill["ClimateControlSkill"]);
    }

    [Fact]
    public async Task InMemoryRepository_ListAsync_FiltersCorrectly()
    {
        var repo = new InMemoryCommandTraceRepository();

        await repo.SaveAsync(CreateTrace("t1", CommandTraceOutcome.CommandHandled, "turn on light", skillId: "LightControlSkill"));
        await repo.SaveAsync(CreateTrace("t2", CommandTraceOutcome.LlmFallback, "what is the weather"));
        await repo.SaveAsync(CreateTrace("t3", CommandTraceOutcome.CommandHandled, "set thermostat", skillId: "ClimateControlSkill"));

        // Filter by outcome
        var commandOnly = await repo.ListAsync(new CommandTraceFilter { Outcome = CommandTraceOutcome.CommandHandled });
        Assert.Equal(2, commandOnly.TotalCount);

        // Filter by search
        var searchResult = await repo.ListAsync(new CommandTraceFilter { Search = "weather" });
        Assert.Equal(1, searchResult.TotalCount);
        Assert.Equal("t2", searchResult.Items[0].Id);

        // Filter by skill
        var skillResult = await repo.ListAsync(new CommandTraceFilter { SkillId = "LightControlSkill" });
        Assert.Equal(1, skillResult.TotalCount);
    }

    [Fact]
    public async Task InMemoryRepository_ListAsync_Paginates()
    {
        var repo = new InMemoryCommandTraceRepository();

        for (var i = 0; i < 25; i++)
        {
            await repo.SaveAsync(CreateTrace($"t{i}", CommandTraceOutcome.CommandHandled, $"input {i}"));
        }

        var page1 = await repo.ListAsync(new CommandTraceFilter { Page = 1, PageSize = 10 });
        Assert.Equal(10, page1.Items.Count);
        Assert.Equal(25, page1.TotalCount);
        Assert.Equal(3, page1.TotalPages);

        var page3 = await repo.ListAsync(new CommandTraceFilter { Page = 3, PageSize = 10 });
        Assert.Equal(5, page3.Items.Count);
    }

    [Fact]
    public void TokenHighlightBuilder_ProducesHighlights_ForMatchedInput()
    {
        var normalizedTranscript = "turn on the bedroom light";
        var tokens = TranscriptNormalizer.Tokenize(normalizedTranscript);
        var template = "turn {action:on|off} [the] {entity}";
        var capturedValues = new Dictionary<string, string>
        {
            ["action"] = "on",
            ["entity"] = "bedroom light",
        };

        var highlights = TokenHighlightBuilder.Build(normalizedTranscript, tokens, template, capturedValues);

        Assert.NotEmpty(highlights);

        // "turn" should be a Literal
        var turnHighlight = highlights.First(h => h.Value == "turn");
        Assert.Equal(TokenHighlightType.Literal, turnHighlight.Type);
        Assert.Equal(0, turnHighlight.Start);

        // "on" should be a ConstrainedCapture
        var onHighlight = highlights.First(h => h.Value == "action");
        Assert.Equal(TokenHighlightType.ConstrainedCapture, onHighlight.Type);

        // "the" should be Optional
        var theHighlight = highlights.First(h => h.Value == "the");
        Assert.Equal(TokenHighlightType.Optional, theHighlight.Type);

        // "bedroom light" should be Capture spans
        var captureHighlights = highlights.Where(h => h.Value == "entity").ToList();
        Assert.Equal(2, captureHighlights.Count); // "bedroom" and "light"
        Assert.All(captureHighlights, h => Assert.Equal(TokenHighlightType.Capture, h.Type));
    }

    [Fact]
    public void TokenHighlightBuilder_ReturnsEmpty_WhenNoTokens()
    {
        var result = TokenHighlightBuilder.Build("", [], "template", new Dictionary<string, string>());
        Assert.Empty(result);
    }

    [Fact]
    public async Task ToolCallCollector_RecordsSuccessfulCall()
    {
        var collector = new ToolCallCollector();

        var result = await collector.RecordAsync(
            "TestMethod",
            new { param1 = "value1" },
            () => Task.FromResult("success response"));

        Assert.Equal("success response", result);
        Assert.Single(collector.ToolCalls);

        var call = collector.ToolCalls[0];
        Assert.Equal("TestMethod", call.MethodName);
        Assert.Contains("value1", call.Arguments!);
        Assert.Equal("success response", call.Response);
        Assert.True(call.Success);
        Assert.Null(call.Error);
        Assert.True(call.DurationMs >= 0);
    }

    [Fact]
    public async Task ToolCallCollector_RecordsFailedCall()
    {
        var collector = new ToolCallCollector();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            collector.RecordAsync(
                "FailingMethod",
                new { entityId = "test" },
                () => throw new InvalidOperationException("something broke")));

        Assert.Single(collector.ToolCalls);

        var call = collector.ToolCalls[0];
        Assert.Equal("FailingMethod", call.MethodName);
        Assert.False(call.Success);
        Assert.Equal("something broke", call.Error);
        Assert.Null(call.Response);
    }

    [Fact]
    public async Task ToolCallCollector_RecordsMultipleCalls()
    {
        var collector = new ToolCallCollector();

        await collector.RecordAsync("GetState", new { entityId = "climate.1" }, () => Task.FromResult("72°F"));
        await collector.RecordAsync("SetTemp", new { entityId = "climate.1", temperature = 75.0 }, () => Task.FromResult("OK"));

        Assert.Equal(2, collector.ToolCalls.Count);
        Assert.Equal("GetState", collector.ToolCalls[0].MethodName);
        Assert.Equal("SetTemp", collector.ToolCalls[1].MethodName);
    }

    private static CommandTrace CreateTrace(
        string id,
        CommandTraceOutcome outcome,
        string text,
        string? skillId = null,
        string? error = null) => new()
    {
        Id = id,
        Timestamp = DateTimeOffset.UtcNow,
        RawText = text,
        CleanText = text,
        RequestContext = new CommandTraceContext(),
        Match = new CommandTraceMatch
        {
            IsMatch = outcome == CommandTraceOutcome.CommandHandled,
            Confidence = outcome == CommandTraceOutcome.CommandHandled ? 0.9f : 0f,
            SkillId = skillId,
            MatchDurationMs = 1.5,
        },
        Outcome = outcome,
        TotalDurationMs = 25.0,
        Error = error,
    };
}
