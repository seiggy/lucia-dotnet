using System.Net.Http;
using lucia.EvalHarness.Evaluation;
using lucia.EvalHarness.Personality;
using lucia.EvalHarness.Tests.TestDoubles;
using Microsoft.Extensions.AI;

namespace lucia.EvalHarness.Tests;

public sealed class PersonalityAvailabilityTests
{
    [Theory]
    [InlineData("""{"personalityScore":0,"personalityReason":"x","meaningScore":5,"meaningReason":"y"}""")]
    [InlineData("""{"personalityScore":5,"personalityReason":"","meaningScore":5,"meaningReason":"y"}""")]
    [InlineData("""{"personalityScore":5,"personalityReason":"x","meaningScore":6,"meaningReason":"y"}""")]
    [InlineData("""{"personalityScore":5,"meaningScore":5}""")]
    [InlineData("not json")]
    public async Task EvaluateAsync_InvalidResponse_IsUnavailable(string response)
    {
        var result = await new PersonalityJudge(Responding(response)).EvaluateAsync(CreateTrace());

        Assert.Null(result.CombinedScore);
        Assert.Equal(JudgeAvailability.InvalidResponse, result.Status);
        Assert.Equal("Judge response was invalid.", result.UnavailableReason);
    }

    [Fact]
    public async Task EvaluateAsync_ProviderFailure_IsSanitized()
    {
        var client = new ScriptedChatClient(_ =>
            Task.FromException<ChatResponse>(new HttpRequestException("api-key=secret")));

        var result = await new PersonalityJudge(client).EvaluateAsync(CreateTrace());

        Assert.Equal(JudgeAvailability.ProviderError, result.Status);
        Assert.DoesNotContain("secret", result.UnavailableReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_NoJudge_ProducesUnavailableReport()
    {
        var runner = new PersonalityEvalRunner();
        var report = await runner.RunAsync(
            Responding("rewritten"),
            "model",
            null,
            "not configured",
            [CreateScenario()],
            [CreateProfile()]);

        var result = Assert.Single(report.Results);
        Assert.Null(result.Score);
        Assert.Equal(JudgeAvailability.NotConfigured, result.JudgeStatus);
        Assert.Null(report.AverageCombinedScore);
        Assert.Null(report.AveragePersonalityScore);
        Assert.Null(report.AverageMeaningScore);
    }

    [Fact]
    public async Task RunAsync_GenuineMinimumScore_RemainsAvailable()
    {
        var runner = new PersonalityEvalRunner();
        var report = await runner.RunAsync(
            Responding("rewritten"),
            "model",
            Responding("""{"personalityScore":1,"personalityReason":"weak","meaningScore":1,"meaningReason":"lost"}"""),
            "judge",
            [CreateScenario()],
            [CreateProfile()]);

        Assert.Equal(1, Assert.Single(report.Results).Score);
        Assert.Equal(1, report.AverageCombinedScore);
    }

    private static ScriptedChatClient Responding(string text) =>
        new(_ => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text))));

    private static ConversationTrace CreateTrace() =>
        new()
        {
            SystemPrompt = "system",
            UserMessage = "user",
            AssistantResponse = "assistant",
            OriginalResponse = "original"
        };

    private static PersonalityEvalScenario CreateScenario() =>
        new()
        {
            Id = "scenario",
            Description = "description",
            Category = "category",
            AgentResponse = "original",
            SkillId = "skill",
            Action = "action",
            PersonalityPrompt = "prompt",
            Expectations = new PersonalityEvalExpectations()
        };

    private static PersonalityProfile CreateProfile() =>
        new()
        {
            Id = "profile",
            Name = "Profile",
            Instructions = "instructions"
        };
}
