using System.Security.Cryptography;
using System.Text;
using FakeItEasy;
using lucia.Agents;
using lucia.Agents.Abstractions;
using lucia.Agents.Integration;
using lucia.Agents.Models;
using lucia.Agents.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

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
    public async Task GetResponseAsync_PassesInstructionsHashToCacheLookup()
    {
        var (client, cacheService) = CreateClient(innerReturnsFunctionCall: true);

        var hashCapture = A.Captured<string>();
        A.CallTo(() => cacheService.TryGetCachedChatResponseAsync(
                A<string>._, A<string?>._, hashCapture.Ignored, A<CancellationToken>._))
            .Returns((CachedChatResponseData?)null);

        var options = new ChatOptions { Instructions = "You are the open wearables agent." };
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "how many steps today")], options);

        Assert.Equal(HashOf("You are the open wearables agent."), hashCapture.GetLastValue());
    }

    [Fact]
    public async Task GetResponseAsync_PassesNullHash_WhenNoInstructions()
    {
        var (client, cacheService) = CreateClient(innerReturnsFunctionCall: true);

        var hashCapture = A.Captured<string>();
        A.CallTo(() => cacheService.TryGetCachedChatResponseAsync(
                A<string>._, A<string?>._, hashCapture.Ignored, A<CancellationToken>._))
            .Returns((CachedChatResponseData?)null);

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "how many steps today")]);

        Assert.Null(hashCapture.GetLastValue());
    }

    [Fact]
    public async Task GetResponseAsync_StampsInstructionsHashOnStoredData()
    {
        var (client, cacheService) = CreateClient(innerReturnsFunctionCall: true);

        var storedCapture = A.Captured<CachedChatResponseData>();
        A.CallTo(() => cacheService.TryGetCachedChatResponseAsync(A<string>._, A<string?>._, A<string?>._, A<CancellationToken>._))
            .Returns((CachedChatResponseData?)null);
        A.CallTo(() => cacheService.CacheChatResponseAsync(A<string>._, storedCapture.Ignored, A<CancellationToken>._))
            .Returns(Task.CompletedTask);

        var options = new ChatOptions { Instructions = "You are the lights agent." };
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "turn on the kitchen light")], options);

        var stored = storedCapture.GetLastValue();
        Assert.NotNull(stored);
        Assert.Equal(HashOf("You are the lights agent."), stored.InstructionsHash);
    }

    private static (PromptCachingChatClient Client, IPromptCacheService CacheService) CreateClient(bool innerReturnsFunctionCall)
    {
        var inner = A.Fake<IChatClient>();
        if (innerReturnsFunctionCall)
        {
            A.CallTo(() => inner.GetResponseAsync(A<IEnumerable<ChatMessage>>._, A<ChatOptions?>._, A<CancellationToken>._))
                .Returns(new ChatResponse(
                [
                    new ChatMessage(ChatRole.Assistant,
                        [new FunctionCallContent("call-1", "get_activity_summary", new Dictionary<string, object?> { ["days"] = 1 })]),
                ]));
        }
        else
        {
            A.CallTo(() => inner.GetResponseAsync(A<IEnumerable<ChatMessage>>._, A<ChatOptions?>._, A<CancellationToken>._))
                .Returns(new ChatResponse([new ChatMessage(ChatRole.Assistant, "plain text")]));
        }

        var cacheService = A.Fake<IPromptCacheService>();
        var telemetry = new AgentsTelemetrySource();
        var logger = A.Fake<ILogger<PromptCachingChatClient>>();

        return (new PromptCachingChatClient(inner, cacheService, telemetry, logger), cacheService);
    }

    private static string HashOf(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(bytes);
    }

    [Fact]
    public void StripVolatileFields_PreservesNonVolatileContent()
    {
        var input = "turn on the lights in the kitchen";

        var result = PromptCachingChatClient.StripVolatileFields(input);

        Assert.Equal(input, result);
    }
}
