using lucia.Agents.Training;
using lucia.Data.InMemory;
using lucia.Tests.TestDoubles;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests.Training;

public sealed class AgentTracingChatClientToolResultTests
{
    private const string SessionId = "tool-result-capture-session";

    [Fact]
    public async Task ToolResultSentInNextRound_IsCapturedOnTheMatchingToolCall()
    {
        var repository = new InMemoryTraceRepository();
        var inner = new StubChatClient(
        [
            _ => new ChatResponse(
            [
                new ChatMessage(ChatRole.Assistant,
                    [new FunctionCallContent(
                        "call_1", "homeassistant__GetLiveContext", new Dictionary<string, object?> { ["domain"] = "vacuum" })])
            ]),
            _ => new ChatResponse([new ChatMessage(ChatRole.Assistant, "No, Bob is docked.")])
        ]);

        var client = new AgentTracingChatClient(
            inner, "robot-vacuum-agent", repository, NullLogger<AgentTracingChatClient>.Instance);
        AgentTracingChatClient.SetSessionId(SessionId);

        var userMessage = new ChatMessage(ChatRole.User, "Is the vacuum cleaning?");
        var round1 = await client.GetResponseAsync([userMessage]);
        var assistantToolCallMessage = round1.Messages!.Single();

        var round2Messages = new List<ChatMessage>
        {
            userMessage,
            assistantToolCallMessage,
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call_1", "status: docked")])
        };
        await client.GetResponseAsync(round2Messages);

        var trace = await WaitForTraceAsync(repository);
        var toolCall = trace.AgentExecutions.Single().ToolCalls.Single();

        Assert.Equal("homeassistant__GetLiveContext", toolCall.ToolName);
        Assert.Equal("call_1", toolCall.CallId);
        Assert.Equal("status: docked", toolCall.Result);
    }

    private static async Task<Agents.Training.Models.ConversationTrace> WaitForTraceAsync(InMemoryTraceRepository repository)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var traces = await repository.GetTracesBySessionIdAsync(SessionId);
            if (traces.Count > 0)
                return traces.Single();

            await Task.Delay(50);
        }

        throw new TimeoutException("Agent trace was not persisted within the wait window.");
    }
}
