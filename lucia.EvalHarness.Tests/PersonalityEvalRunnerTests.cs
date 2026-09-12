using lucia.EvalHarness.Evaluation;
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
    [Theory]
    [InlineData("provider/model")]
    [InlineData("ollama:latest")]
    [InlineData("../../outside")]
    [InlineData("..\\..\\outside")]
    [InlineData("C:\\outside\\model")]
    [InlineData("/outside/model")]
    public void TraceDirectory_ModelNameCannotEscapeRootOrCreateNestedDirectories(string modelName)
    {
        var startedAt = new DateTimeOffset(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);
        var path = PersonalityEvalRunner.GetTraceDirectory(modelName, startedAt);
        var root = Path.GetFullPath("personality-eval-traces");
        var fullPath = Path.GetFullPath(path);

        Assert.Equal(root, Path.GetDirectoryName(fullPath));
        Assert.DoesNotContain(Path.GetFileName(fullPath), character =>
            Path.GetInvalidFileNameChars().Contains(character));
        Assert.EndsWith("_20260911_120000", Path.GetFileName(fullPath), StringComparison.Ordinal);
    }

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
        Assert.Null(result.Score);
        Assert.Equal(JudgeAvailability.Timeout, result.JudgeStatus);
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
