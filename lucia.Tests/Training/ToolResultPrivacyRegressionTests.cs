using System.Text.Json;
using lucia.Agents.Training;
using lucia.Agents.Training.Models;
using lucia.Data.InMemory;
using lucia.Tests.TestDoubles;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests.Training;

public sealed class ToolResultPrivacyRegressionTests
{
    [Fact]
    public async Task DefaultCapture_DoesNotPersistToolResult()
    {
        var repository = new InMemoryTraceRepository();
        using var inner = new StubChatClient([
            _ => new ChatResponse(new ChatMessage(ChatRole.Assistant,
                [new FunctionCallContent("call-1", "read_sensor")])),
            _ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "Finished."))
        ]);
        using var client = new AgentTracingChatClient(inner, "sensor-agent", repository,
            NullLogger<AgentTracingChatClient>.Instance);
        var session = Guid.NewGuid().ToString("N");
        AgentTracingChatClient.SetSessionId(session);
        var user = new ChatMessage(ChatRole.User, "Check the sensor.");
        var first = await client.GetResponseAsync([user]);
        await client.GetResponseAsync([user, first.Messages.Single(),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call-1", "PRIVATE_RESULT")])]);

        var trace = Assert.Single(await repository.GetTracesBySessionIdAsync(session));
        Assert.Null(Assert.Single(Assert.Single(trace.AgentExecutions).ToolCalls).Result);
        Assert.DoesNotContain("PRIVATE_RESULT", JsonSerializer.Serialize(trace));
    }

    [Fact]
    public void DefaultExport_DoesNotIncludeLegacyToolResults()
    {
        var trace = new ConversationTrace
        {
            SessionId = "legacy-session",
            UserInput = "Check.",
            AgentExecutions = [new AgentExecutionRecord
            {
                AgentId = "sensor-agent",
                ToolCalls = [new TracedToolCall { ToolName = "read_sensor", Result = "PRIVATE_LEGACY_RESULT" }]
            }]
        };

        var jsonl = JsonlConverter.ConvertTraceToJsonl(trace, false, "sensor-agent");
        Assert.DoesNotContain("PRIVATE_LEGACY_RESULT", jsonl);
        using var document = JsonDocument.Parse(jsonl);
        Assert.DoesNotContain(document.RootElement.GetProperty("messages").EnumerateArray(),
            message => message.GetProperty("role").GetString() == "tool");
    }
}
