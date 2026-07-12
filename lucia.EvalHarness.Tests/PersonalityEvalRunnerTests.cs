using lucia.EvalHarness.Personality;
using lucia.EvalHarness.Tests.TestDoubles;

namespace lucia.EvalHarness.Tests;

/// <summary>
/// Verifies the model-under-test site deadline behavior in
/// <see cref="PersonalityEvalRunner"/>: a model-call timeout is recorded as a distinct
/// <see cref="PersonalityScenarioResult.TimedOut"/> result, and caller cancellation propagates.
/// </summary>
public sealed class PersonalityEvalRunnerTests
{
    private static PersonalityEvalScenario SampleScenario() => new()
    {
        Id = "scenario-1",
        Category = "test",
        Description = "sample",
        SkillId = "skill",
        Action = "action",
        AgentResponse = "The light is on.",
        PersonalityPrompt = "Be a pirate.",
        Expectations = new PersonalityEvalExpectations()
    };

    private static PersonalityProfile SampleProfile() => new()
    {
        Id = "pirate",
        Name = "Pirate",
        Instructions = "Talk like a pirate."
    };

    [Fact]
    public async Task RunAsync_ModelCallTimesOut_RecordsDistinctTimeout()
    {
        var modelClient = ScriptedChatClient.Throwing(_ => new TimeoutException("model deadline exceeded"));
        var judgeClient = ScriptedChatClient.Returning("{}"); // never reached — model fails first
        var runner = new PersonalityEvalRunner(TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(120));

        var report = await runner.RunAsync(
            modelClient, "model-under-test", judgeClient, "judge-model",
            [SampleScenario()], [SampleProfile()]);

        var result = Assert.Single(report.Results);
        Assert.True(result.TimedOut);
        Assert.Equal(0, result.Score);
        Assert.Contains("timed out", result.ErrorMessage);
    }

    [Fact]
    public async Task RunAsync_CallerCancels_Propagates()
    {
        var modelClient = ScriptedChatClient.Throwing(token => new OperationCanceledException(token));
        var judgeClient = ScriptedChatClient.Returning("{}");
        var runner = new PersonalityEvalRunner(TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(120));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => runner.RunAsync(
            modelClient, "model-under-test", judgeClient, "judge-model",
            [SampleScenario()], [SampleProfile()], ct: cts.Token));
    }
}
