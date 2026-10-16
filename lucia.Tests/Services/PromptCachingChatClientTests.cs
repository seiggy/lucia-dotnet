using lucia.Agents;
using lucia.Agents.Integration;
using lucia.Agents.Services;
using lucia.Tests.TestDoubles;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests.Services;

public sealed class PromptCachingChatClientTests
{
    [Fact]
    public void StripVolatileFields_RemovesTimestampAndDayOfWeek()
    {
        var input = """
            HOME ASSISTANT CONTEXT:

            REQUEST_CONTEXT:
            {
              "timestamp": "2026-03-01 15:58:51",
              "day_of_week": "Sunday",
              "location": "Home",
              "device": {
                "id": "conversation.lucia",
                "area": "Zack's Office",
                "type": "conversation"
              }
            }

            turn on the lights in the office
            """;

        var result = PromptCachingChatClient.StripVolatileFields(input);

        Assert.DoesNotContain("2026-03-01 15:58:51", result);
        Assert.DoesNotContain("Sunday", result);
        Assert.DoesNotContain("conversation.lucia", result);
        Assert.Contains("location", result);
        Assert.Contains("Zack's Office", result);
        Assert.Contains("turn on the lights in the office", result);
    }

    [Fact]
    public void StripVolatileFields_DifferentTimestampsProduceSameResult()
    {
        var prompt1 = """
            REQUEST_CONTEXT:
            {"timestamp": "2026-03-01 15:58:51", "day_of_week": "Sunday", "location": "Home"}
            turn on the lights
            """;

        var prompt2 = """
            REQUEST_CONTEXT:
            {"timestamp": "2026-03-01 16:30:00", "day_of_week": "Sunday", "location": "Home"}
            turn on the lights
            """;

        var prompt3 = """
            REQUEST_CONTEXT:
            {"timestamp": "2026-03-02 09:00:00", "day_of_week": "Monday", "location": "Home"}
            turn on the lights
            """;

        var result1 = PromptCachingChatClient.StripVolatileFields(prompt1);
        var result2 = PromptCachingChatClient.StripVolatileFields(prompt2);
        var result3 = PromptCachingChatClient.StripVolatileFields(prompt3);

        Assert.Equal(result1, result2);
        Assert.Equal(result1, result3);
    }

    [Fact]
    public void StripVolatileFields_PreservesNonVolatileContent()
    {
        var input = "turn on the lights in the kitchen";

        var result = PromptCachingChatClient.StripVolatileFields(input);

        Assert.Equal(input, result);
    }

    private static (PromptCachingChatClient Client, RecordingPromptCacheService Cache) BuildClient(IChatClient inner)
    {
        var cache = new RecordingPromptCacheService();
        var client = new PromptCachingChatClient(
            inner, cache, new AgentsTelemetrySource(), NullLogger<PromptCachingChatClient>.Instance);
        return (client, cache);
    }

    private static ChatOptions WithGetLiveContextTool() => new()
    {
        Tools = [AIFunctionFactory.Create(() => "ok", "homeassistant__GetLiveContext", "Get live context")]
    };

    [Fact]
    public async Task PlanningDecisionWithRegisteredToolName_IsCached()
    {
        var inner = new StubChatClient(
        [
            _ => new ChatResponse(
            [
                new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call_1", "homeassistant__GetLiveContext")])
            ])
        ]);
        var (client, cache) = BuildClient(inner);

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "is the vacuum cleaning?")], WithGetLiveContextTool());

        Assert.Single(cache.CachedChatResponses);
    }

    [Fact]
    public async Task PlanningDecisionWithUnregisteredToolName_IsNotCached()
    {
        var inner = new StubChatClient(
        [
            _ => new ChatResponse(
            [
                new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call_1", "GetLiveContext")])
            ])
        ]);
        var (client, cache) = BuildClient(inner);

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "is the vacuum cleaning?")], WithGetLiveContextTool());

        Assert.Empty(cache.CachedChatResponses);
    }
}
