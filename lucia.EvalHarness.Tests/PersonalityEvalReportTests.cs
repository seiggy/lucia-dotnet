using lucia.EvalHarness.Personality;

namespace lucia.EvalHarness.Tests;

/// <summary>
/// Verifies that a judge timeout (a <see cref="JudgeResult"/> with zero scores and
/// <see cref="JudgeResult.TimedOut"/> set) is excluded from score/failure aggregation
/// in <see cref="PersonalityEvalReport"/> and surfaced separately as a timeout.
/// </summary>
public sealed class PersonalityEvalReportTests
{
    private static PersonalityScenarioResult Scored(int personality, int meaning) => new()
    {
        ScenarioId = "s-scored",
        ScenarioDescription = "scored",
        Category = "test",
        ProfileId = "p1",
        ProfileName = "Profile 1",
        ModelName = "model",
        Score = (personality + meaning) / 2.0,
        LlmResponse = "ok",
        DurationMs = 10,
        JudgeResult = new JudgeResult
        {
            PersonalityScore = personality,
            PersonalityReason = "reason",
            MeaningScore = meaning,
            MeaningReason = "reason"
        }
    };

    private static PersonalityScenarioResult JudgeTimedOut() => new()
    {
        ScenarioId = "s-timeout",
        ScenarioDescription = "timeout",
        Category = "test",
        ProfileId = "p1",
        ProfileName = "Profile 1",
        ModelName = "model",
        Score = 0,
        LlmResponse = "ok",
        DurationMs = 10,
        TimedOut = true,
        JudgeResult = new JudgeResult
        {
            PersonalityScore = 0,
            PersonalityReason = "Judge call timed out",
            MeaningScore = 0,
            MeaningReason = "Judge call timed out",
            TimedOut = true
        }
    };

    private static PersonalityEvalReport Report(params PersonalityScenarioResult[] results) => new()
    {
        ModelName = "model",
        JudgeModelName = "judge",
        Results = results,
        StartedAt = DateTimeOffset.UtcNow,
        CompletedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public void Averages_ExcludeTimedOutJudgeResults()
    {
        var report = Report(Scored(4, 4), JudgeTimedOut());

        Assert.Equal(4.0, report.AveragePersonalityScore);
        Assert.Equal(4.0, report.AverageMeaningScore);
        Assert.Equal(4.0, report.AverageCombinedScore);
    }

    [Fact]
    public void MeaningFailures_ExcludeTimedOutJudgeResults()
    {
        var report = Report(Scored(4, 4), JudgeTimedOut());

        Assert.Empty(report.MeaningFailures);
    }

    [Fact]
    public void Timeouts_SurfaceTimedOutResultsSeparately()
    {
        var report = Report(Scored(4, 4), JudgeTimedOut());

        var timedOut = Assert.Single(report.Timeouts);
        Assert.Equal("s-timeout", timedOut.ScenarioId);
    }
}
