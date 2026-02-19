using System.Text.Json;
using lucia.Agents.Training;
using lucia.Agents.Training.Models;

namespace lucia.Tests.Training;

public sealed class ConversationTraceTests
{
    [Fact]
    public void ConversationTrace_DefaultId_IsNonEmptyGuidFormat()
    {
        var trace = new ConversationTrace { SessionId = "s1", UserInput = "hello" };

        Assert.False(string.IsNullOrWhiteSpace(trace.Id));
        Assert.True(Guid.TryParse(trace.Id, out _));
    }

    [Fact]
    public void ConversationTrace_DefaultTimestamp_IsApproximatelyUtcNow()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var trace = new ConversationTrace { SessionId = "s1", UserInput = "hello" };
        var after = DateTime.UtcNow.AddSeconds(1);

        Assert.InRange(trace.Timestamp, before, after);
    }

    [Fact]
    public void ConversationTrace_AgentExecutions_DefaultsToEmptyList()
    {
        var trace = new ConversationTrace { SessionId = "s1", UserInput = "hello" };

        Assert.NotNull(trace.AgentExecutions);
        Assert.Empty(trace.AgentExecutions);
    }

    [Fact]
    public void ConversationTrace_Label_DefaultsToUnlabeled()
    {
        var trace = new ConversationTrace { SessionId = "s1", UserInput = "hello" };

        Assert.NotNull(trace.Label);
        Assert.Equal(LabelStatus.Unlabeled, trace.Label.Status);
    }

    [Theory]
    [InlineData(0, 10, 0)]
    [InlineData(25, 10, 3)]
    [InlineData(10, 10, 1)]
    [InlineData(1, 10, 1)]
    [InlineData(11, 10, 2)]
    [InlineData(100, 0, 0)]
    public void PagedResult_TotalPages_CalculatesCorrectly(int totalCount, int pageSize, int expectedPages)
    {
        var result = new PagedResult<string>
        {
            Items = [],
            TotalCount = totalCount,
            PageSize = pageSize
        };

        Assert.Equal(expectedPages, result.TotalPages);
    }

    [Fact]
    public void TraceCaptureOptions_Defaults_AreCorrect()
    {
        var options = new TraceCaptureOptions();

        Assert.True(options.Enabled);
        Assert.Equal(30, options.RetentionDays);
        Assert.Equal("luciatraces", options.DatabaseName);
        Assert.Equal("traces", options.TracesCollectionName);
        Assert.Equal("exports", options.ExportsCollectionName);
        Assert.NotNull(options.RedactionPatterns);
        Assert.Equal(2, options.RedactionPatterns.Length);
    }

    [Fact]
    public void ConversationTrace_JsonSerializationRoundTrip_PreservesFields()
    {
        var trace = new ConversationTrace
        {
            SessionId = "session-42",
            UserInput = "Turn on the lights",
            FinalResponse = "Lights turned on.",
            TotalDurationMs = 123.45,
            IsErrored = false,
            TaskId = "task-7",
            Routing = new RoutingDecision
            {
                SelectedAgentId = "home-agent",
                Confidence = 0.95,
                Reasoning = "matched home intent"
            }
        };

        trace.Label.Status = LabelStatus.Positive;
        trace.AgentExecutions.Add(new AgentExecutionRecord
        {
            AgentId = "home-agent",
            ResponseContent = "Done",
            Success = true
        });
        trace.Metadata["key1"] = "value1";

        var json = JsonSerializer.Serialize(trace);
        var deserialized = JsonSerializer.Deserialize<ConversationTrace>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(trace.Id, deserialized.Id);
        Assert.Equal(trace.SessionId, deserialized.SessionId);
        Assert.Equal(trace.UserInput, deserialized.UserInput);
        Assert.Equal(trace.FinalResponse, deserialized.FinalResponse);
        Assert.Equal(trace.TotalDurationMs, deserialized.TotalDurationMs);
        Assert.Equal(trace.TaskId, deserialized.TaskId);
        Assert.Equal(trace.IsErrored, deserialized.IsErrored);
        Assert.Equal(LabelStatus.Positive, deserialized.Label.Status);
        Assert.Single(deserialized.AgentExecutions);
        Assert.Equal("home-agent", deserialized.AgentExecutions[0].AgentId);
        Assert.NotNull(deserialized.Routing);
        Assert.Equal("home-agent", deserialized.Routing.SelectedAgentId);
        Assert.Equal(0.95, deserialized.Routing.Confidence);
        Assert.Equal("value1", deserialized.Metadata["key1"]);
    }
}
