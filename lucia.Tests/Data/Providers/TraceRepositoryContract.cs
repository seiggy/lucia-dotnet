using lucia.Agents.Training;
using lucia.Agents.Training.Models;

namespace lucia.Tests.Data.Providers;

/// <summary>
/// Behaviour every <see cref="ITraceRepository"/> provider must share.
/// </summary>
public abstract class TraceRepositoryContract
{
    protected abstract Task<StoreScope<ITraceRepository>> CreateAsync();

    [Fact]
    public async Task InsertTraceAsync_And_GetTraceAsync_RoundTripMeaningfulFields()
    {
        await using var scope = await CreateAsync();

        var trace = CreateTrace(
            "trace-1",
            "session-1",
            "turn on the living room lights",
            LabelStatus.Positive,
            DateTime.UtcNow.AddDays(-2),
            isErrored: true,
            agentId: "livingroom-agent",
            modelDeploymentName: "gpt-4o-mini",
            success: false);

        await scope.Store.InsertTraceAsync(trace);

        var result = await scope.Store.GetTraceAsync(trace.Id);

        Assert.NotNull(result);
        Assert.Equal("session-1", result.SessionId);
        Assert.Equal("turn on the living room lights", result.UserInput);
        Assert.Equal(LabelStatus.Positive, result.Label.Status);
        Assert.True(result.IsErrored);
        var execution = Assert.Single(result.AgentExecutions);
        Assert.Equal("livingroom-agent", execution.AgentId);
        Assert.Equal("gpt-4o-mini", execution.ModelDeploymentName);
        Assert.False(execution.Success);
    }

    [Fact]
    public async Task GetTracesBySessionIdAsync_And_ListTracesAsync_FilterAndPage_ApplyExpectedSemantics()
    {
        await using var scope = await CreateAsync();

        var first = CreateTrace("trace-1", "session-1", "turn on the kitchen lights", LabelStatus.Unlabeled, DateTime.UtcNow.AddDays(-5), agentId: "kitchen-agent", modelDeploymentName: "gpt-4o");
        var second = CreateTrace("trace-2", "session-1", "what is the weather", LabelStatus.Positive, DateTime.UtcNow.AddDays(-4), agentId: "weather-agent", modelDeploymentName: "gpt-4o");
        var third = CreateTrace("trace-3", "session-2", "turn off the bedroom lights", LabelStatus.Negative, DateTime.UtcNow.AddDays(-3), agentId: "bedroom-agent", modelDeploymentName: "gpt-4.1", success: false);

        await scope.Store.InsertTraceAsync(first);
        await scope.Store.InsertTraceAsync(second);
        await scope.Store.InsertTraceAsync(third);

        var bySession = await scope.Store.GetTracesBySessionIdAsync("session-1");
        Assert.Equal(2, bySession.Count);

        var byAgent = await scope.Store.ListTracesAsync(new TraceFilterCriteria { AgentFilter = "weather-agent", Page = 1, PageSize = 10 });
        var agentResult = Assert.Single(byAgent.Items);
        Assert.Equal("trace-2", agentResult.Id);

        var byText = await scope.Store.ListTracesAsync(new TraceFilterCriteria { SearchText = "weather", Page = 1, PageSize = 10 });
        var textResult = Assert.Single(byText.Items);
        Assert.Equal("trace-2", textResult.Id);

        var paged = await scope.Store.ListTracesAsync(new TraceFilterCriteria { Page = 2, PageSize = 2 });
        Assert.Single(paged.Items);
        Assert.Equal(3, paged.TotalCount);
        Assert.Equal(2, paged.Page);
    }

    [Fact]
    public async Task UpdateLabelAsync_DeleteTraceAsync_And_DeleteOldUnlabeledAsync_ApplyCorrectly()
    {
        await using var scope = await CreateAsync();

        var oldUnlabeled = CreateTrace("old", "session-old", "old unlabeled trace", LabelStatus.Unlabeled, DateTime.UtcNow.AddDays(-30));
        var recentPositive = CreateTrace("recent", "session-recent", "recent positive trace", LabelStatus.Positive, DateTime.UtcNow.AddDays(-1));

        await scope.Store.InsertTraceAsync(oldUnlabeled);
        await scope.Store.InsertTraceAsync(recentPositive);

        await scope.Store.UpdateLabelAsync(recentPositive.Id, new TraceLabel { Status = LabelStatus.Negative, ReviewerNotes = "Needs review" });

        var updated = await scope.Store.GetTraceAsync(recentPositive.Id);
        Assert.NotNull(updated);
        Assert.Equal(LabelStatus.Negative, updated.Label.Status);
        Assert.Equal("Needs review", updated.Label.ReviewerNotes);

        var deletedCount = await scope.Store.DeleteOldUnlabeledAsync(7);
        Assert.Equal(1, deletedCount);
        Assert.Null(await scope.Store.GetTraceAsync(oldUnlabeled.Id));

        await scope.Store.DeleteTraceAsync(recentPositive.Id);
        Assert.Null(await scope.Store.GetTraceAsync(recentPositive.Id));
    }

    [Fact]
    public async Task InsertExportRecordAsync_And_GetExportRecordAsync_And_GetTracesForExportAsync_Work()
    {
        await using var scope = await CreateAsync();

        var export = new DatasetExportRecord
        {
            Id = "export-1",
            Timestamp = DateTime.UtcNow,
            RecordCount = 2,
            FileSizeBytes = 1024,
            FilePath = "/tmp/export.jsonl",
            IsComplete = true,
            FilterCriteria = new ExportFilterCriteria { LabelFilter = LabelStatus.Positive, AgentFilter = "weather-agent" }
        };

        await scope.Store.InsertExportRecordAsync(export);

        var storedExport = await scope.Store.GetExportRecordAsync(export.Id);
        Assert.NotNull(storedExport);
        Assert.Equal("export-1", storedExport.Id);
        Assert.Equal(2, storedExport.RecordCount);

        var list = await scope.Store.ListExportRecordsAsync();
        Assert.Contains(list, item => item.Id == export.Id);

        var traceOne = CreateTrace("export-a", "session-export", "weather summary request", LabelStatus.Positive, DateTime.UtcNow.AddHours(-1), agentId: "weather-agent", modelDeploymentName: "gpt-4o");
        var traceTwo = CreateTrace("export-b", "session-export", "lights summary request", LabelStatus.Unlabeled, DateTime.UtcNow.AddHours(-2), agentId: "lights-agent", modelDeploymentName: "gpt-4o");

        await scope.Store.InsertTraceAsync(traceOne);
        await scope.Store.InsertTraceAsync(traceTwo);

        var exported = await scope.Store.GetTracesForExportAsync(new ExportFilterCriteria { LabelFilter = LabelStatus.Positive, AgentFilter = "weather-agent" });
        Assert.Single(exported);
        Assert.Equal("export-a", exported[0].Id);
    }

    [Fact]
    public async Task GetStatsAsync_AggregatesCountsByLabelAndAgent()
    {
        await using var scope = await CreateAsync();

        await scope.Store.InsertTraceAsync(CreateTrace("stats-1", "session-stats", "lights on", LabelStatus.Unlabeled, DateTime.UtcNow.AddDays(-1), agentId: "lights-agent", modelDeploymentName: "gpt-4o", success: true));
        await scope.Store.InsertTraceAsync(CreateTrace("stats-2", "session-stats", "weather forecast", LabelStatus.Positive, DateTime.UtcNow.AddDays(-2), agentId: "weather-agent", modelDeploymentName: "gpt-4o", success: true));
        await scope.Store.InsertTraceAsync(CreateTrace("stats-3", "session-stats", "heat pump query", LabelStatus.Negative, DateTime.UtcNow.AddDays(-3), isErrored: true, agentId: "lights-agent", modelDeploymentName: "gpt-4.1", success: false));

        var stats = await scope.Store.GetStatsAsync();

        Assert.Equal(3, stats.TotalTraces);
        Assert.Equal(1, stats.UnlabeledCount);
        Assert.Equal(1, stats.PositiveCount);
        Assert.Equal(1, stats.NegativeCount);
        Assert.Equal(1, stats.ErroredCount);
        Assert.Equal(2, stats.ByAgent["lights-agent"]);
        Assert.Equal(1, stats.ByAgent["weather-agent"]);
        Assert.Equal(1, stats.ErrorsByAgent["lights-agent"]);
    }

    private static ConversationTrace CreateTrace(
        string id,
        string sessionId,
        string userInput,
        LabelStatus labelStatus,
        DateTime timestamp,
        bool isErrored = false,
        string? agentId = null,
        string? modelDeploymentName = null,
        bool success = true)
    {
        var trace = new ConversationTrace
        {
            Id = id,
            SessionId = sessionId,
            Timestamp = timestamp,
            UserInput = userInput,
            Label = new TraceLabel { Status = labelStatus },
            IsErrored = isErrored,
            AgentExecutions = string.IsNullOrWhiteSpace(agentId)
                ? []
                :
                [
                    new AgentExecutionRecord
                    {
                        AgentId = agentId,
                        ModelDeploymentName = modelDeploymentName,
                        Success = success,
                        ExecutionDurationMs = 12.5,
                    }
                ],
        };

        return trace;
    }
}
