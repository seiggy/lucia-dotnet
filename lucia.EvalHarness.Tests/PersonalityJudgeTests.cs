using lucia.EvalHarness.Personality;
using lucia.EvalHarness.Tests.TestDoubles;

namespace lucia.EvalHarness.Tests;

/// <summary>
/// Verifies the judge-site deadline behavior: a deadline timeout is recorded as a
/// distinct <see cref="JudgeResult.TimedOut"/> result (not a silent zero score),
/// while caller cancellation propagates rather than being swallowed.
/// </summary>
public sealed class PersonalityJudgeTests
{
    private static ConversationTrace SampleTrace() => new()
    {
        SystemPrompt = "Be a pirate.",
        UserMessage = "Rephrase: The light is on.",
        AssistantResponse = "Arr, the light be lit!",
        OriginalResponse = "The light is on."
    };

    [Fact]
    public async Task EvaluateAsync_ValidJudgeResponse_ParsesScores()
    {
        var client = ScriptedChatClient.Returning(
            """{"personalityScore":4,"personalityReason":"strong","meaningScore":5,"meaningReason":"preserved"}""");
        var judge = new PersonalityJudge(client, TimeSpan.FromSeconds(120));

        var result = await judge.EvaluateAsync(SampleTrace(), "scenario-1");

        Assert.False(result.TimedOut);
        Assert.Equal(4, result.PersonalityScore);
        Assert.Equal(5, result.MeaningScore);
    }

    [Fact]
    public async Task EvaluateAsync_JudgeCallTimesOut_RecordsDistinctTimeout()
    {
        var client = ScriptedChatClient.Throwing(_ => new TimeoutException("judge deadline exceeded"));
        var judge = new PersonalityJudge(client, TimeSpan.FromSeconds(120));

        var result = await judge.EvaluateAsync(SampleTrace(), "scenario-1");

        Assert.True(result.TimedOut);
        Assert.Equal(0, result.PersonalityScore);
        Assert.Contains("timed out", result.PersonalityReason);
    }

    [Fact]
    public async Task EvaluateAsync_CallerCancels_Propagates()
    {
        var client = ScriptedChatClient.Throwing(token => new OperationCanceledException(token));
        var judge = new PersonalityJudge(client, TimeSpan.FromSeconds(120));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => judge.EvaluateAsync(SampleTrace(), "scenario-1", cts.Token));
    }
}
