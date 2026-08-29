namespace lucia.VoiceBenchmarks.Tests;

public sealed class SpeakerBenchmarkMetricsTests
{
    [Fact]
    public void ComputeEer_ReturnsZero_WhenScoresAreSeparable()
    {
        var genuineScores = new[] { 0.91d, 0.95d };
        var impostorScores = new[] { 0.10d, 0.20d };

        var eer = SpeakerBenchmarkMetrics.ComputeEer(genuineScores, impostorScores);

        Assert.Equal(0d, eer, 12);
    }

    [Fact]
    public void ComputeEer_ReturnsErrorRateAtEqualOperatingPoint()
    {
        var genuineScores = new[] { 0.40d, 0.90d };
        var impostorScores = new[] { 0.30d, 0.80d };

        var eer = SpeakerBenchmarkMetrics.ComputeEer(genuineScores, impostorScores);

        Assert.Equal(0.5d, eer, 12);
    }

    [Fact]
    public void ComputeEer_ReturnsHalf_WhenAllScoresAreEqual()
    {
        var eer = SpeakerBenchmarkMetrics.ComputeEer([0.5d], [0.5d]);

        Assert.Equal(0.5d, eer, 12);
    }

    [Fact]
    public void ComputeEer_InterpolatesBetweenOperatingPoints()
    {
        var eer = SpeakerBenchmarkMetrics.ComputeEer([1d], [0d, 2d]);

        Assert.Equal(0.5d, eer, 12);
    }

    [Fact]
    public void ComputeTop1Accuracy_ReturnsOne_WhenPredictionsAreCorrect()
    {
        var predictions = new[]
        {
            new SpeakerBenchmarkPrediction("speaker-a", "speaker-a", 0.91d),
            new SpeakerBenchmarkPrediction("speaker-b", "speaker-b", 0.86d),
        };

        var accuracy = SpeakerBenchmarkMetrics.ComputeTop1Accuracy(predictions);

        Assert.Equal(1d, accuracy, 12);
    }

    [Fact]
    public void ComputeErrorRates_UsesFrozenThreshold()
    {
        var rates = SpeakerBenchmarkMetrics.ComputeErrorRates(
            [0.6d, 0.9d],
            [0.2d, 0.8d],
            threshold: 0.7d);

        Assert.Equal(0.5d, rates.FalseAcceptanceRate);
        Assert.Equal(0.5d, rates.FalseRejectionRate);
    }

    [Fact]
    public void ComputeNormalizedMinDcf_ReturnsZeroForSeparableScores()
    {
        var minDcf = SpeakerBenchmarkMetrics.ComputeNormalizedMinDcf(
            [0.8d, 0.9d],
            [0.1d, 0.2d],
            targetPrior: 0.01d);

        Assert.Equal(0d, minDcf);
    }
}
