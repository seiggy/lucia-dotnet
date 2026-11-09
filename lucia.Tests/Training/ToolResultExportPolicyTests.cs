using System.Reflection;
using System.Text.Json;
using FakeItEasy;
using lucia.AgentHost.Apis;
using lucia.Agents.Training;
using lucia.Agents.Training.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace lucia.Tests.Training;

public sealed class ToolResultExportPolicyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LegacyResults_AreResanitizedOnlyWithExportOptIn(bool enabled)
    {
        var trace = Trace("{\"nested\":{\"token\":\"SECRET\"},\"status\":\"offline\"}");
        var jsonl = JsonlConverter.ConvertTraceToJsonl(trace, false, "sensor-agent", new()
        {
            IncludeToolResultsInTrainingExport = enabled
        });
        using var document = JsonDocument.Parse(jsonl);
        var toolMessages = document.RootElement.GetProperty("messages").EnumerateArray()
            .Where(message => message.GetProperty("role").GetString() == "tool").ToList();
        Assert.DoesNotContain("SECRET", jsonl);
        Assert.Equal(enabled ? 1 : 0, toolMessages.Count);
        if (enabled)
            Assert.Contains("[REDACTED]", toolMessages.Single().GetProperty("content").GetString());
        Assert.Contains("SECRET", trace.AgentExecutions.Single().ToolCalls.Single().Result!);
    }

    [Fact]
    public void CaptureOptIn_DoesNotImplyExportOptIn()
    {
        var jsonl = JsonlConverter.ConvertTraceToJsonl(Trace("PRIVATE"), false, "sensor-agent",
            new() { CaptureToolResults = true });
        Assert.DoesNotContain("PRIVATE", jsonl);
    }

    [Fact]
    public void ExportOptIn_UsesCurrentBoundAndPatternsForLegacyData()
    {
        var trace = Trace("PRIVATE-" + new string('x', 1000) + "-END");
        var jsonl = JsonlConverter.ConvertTraceToJsonl(trace, false, "sensor-agent", new()
        {
            IncludeToolResultsInTrainingExport = true,
            MaxToolResultBytes = 10,
            RedactionPatterns = ["PRIVATE-.*-END"]
        });
        using var document = JsonDocument.Parse(jsonl);
        var tool = document.RootElement.GetProperty("messages").EnumerateArray()
            .Single(message => message.GetProperty("role").GetString() == "tool");
        Assert.Equal("read_sensor: [REDACTED]", tool.GetProperty("content").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExportEndpoint_PassesServerPolicyToConverter(bool enabled)
    {
        var repository = A.Fake<ITraceRepository>();
        A.CallTo(() => repository.GetTracesForExportAsync(A<ExportFilterCriteria>._, A<CancellationToken>._))
            .Returns([Trace("password=SECRET")]);
        var method = typeof(DatasetExportApi).GetMethod("CreateExportAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        var task = (Task<Results<Created<DatasetExportRecord>, BadRequest<string>>>)method.Invoke(null,
            [repository, Options.Create(new TraceCaptureOptions { IncludeToolResultsInTrainingExport = enabled }),
                new ExportFilterCriteria { AgentFilter = "sensor-agent" }, CancellationToken.None])!;
        var created = Assert.IsType<Created<DatasetExportRecord>>((await task).Result);
        var record = created.Value!;
        try
        {
            var jsonl = await File.ReadAllTextAsync(record.FilePath!);
            Assert.DoesNotContain("SECRET", jsonl);
            Assert.Equal(enabled, jsonl.Contains("[REDACTED]", StringComparison.Ordinal));
            Assert.True(record.IsComplete);
        }
        finally
        {
            File.Delete(record.FilePath!);
        }
    }

    private static ConversationTrace Trace(string result) => new()
    {
        SessionId = "legacy", UserInput = "Check.", FinalResponse = "Finished.",
        AgentExecutions = [new AgentExecutionRecord
        {
            AgentId = "sensor-agent",
            ToolCalls = [new TracedToolCall { ToolName = "read_sensor", Result = result }]
        }]
    };
}
