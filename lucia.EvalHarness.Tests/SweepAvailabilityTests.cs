using lucia.EvalHarness.Configuration;
using lucia.EvalHarness.Evaluation;
using lucia.EvalHarness.Reports;
using lucia.EvalHarness.Tests.TestDoubles;

namespace lucia.EvalHarness.Tests;

[Collection("Parameter sweep")]
public sealed class SweepAvailabilityTests
{
    [Fact]
    public async Task RunAsync_AllUnavailable_HasNoWinnerAndReportSaysUnavailable()
    {
        var runner = new ParameterSweepRunner((_, _, _) =>
            Task.FromResult<IReadOnlyList<ModelEvalResult>>([EvalResultFactory.Create(null)]));
        var config = new ParameterSweepConfig
        {
            RunsPerCombination = 1,
            TemperatureValues = [0.5],
            TopKValues = [40],
            TopPValues = [0.9],
            RepeatPenaltyValues = [1.1],
            MaxCombinations = 1
        };

        var result = await runner.RunAsync(
            "baseline",
            ["target"],
            [],
            config,
            _ => [],
            null);

        Assert.Null(result.BaselineMeanScore);
        var entries = result.TargetResults["target"];
        Assert.Null(SweepRunAggregator.SelectWinner(entries));

        var directory = Path.Combine(
            AppContext.BaseDirectory,
            "sweep-availability-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = SweepReportGenerator.Export(result, directory);
            var markdown = File.ReadAllText(paths.Single(path => path.EndsWith(".md", StringComparison.Ordinal)));
            var json = File.ReadAllText(paths.Single(path => path.EndsWith(".json", StringComparison.Ordinal)));
            Assert.Contains("No winner", markdown, StringComparison.Ordinal);
            Assert.Contains("\"bestConfigStatus\": \"unavailable\"", json, StringComparison.Ordinal);
            Assert.Contains("\"scoreStatus\": \"unavailable\"", json, StringComparison.Ordinal);
            using var document = System.Text.Json.JsonDocument.Parse(json);
            var configElement = document.RootElement.GetProperty("targets")[0].GetProperty("allConfigs")[0];
            Assert.False(configElement.TryGetProperty("scoreVariance", out _));
            Assert.False(configElement.TryGetProperty("scoreStdDev", out _));
            Assert.False(configElement.TryGetProperty("averageLatencyMs", out _));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void ComputeMean_PartialAvailability_ExcludesUnavailableAndKeepsZero()
    {
        IReadOnlyList<IReadOnlyList<ModelEvalResult>> runs =
        [
            [EvalResultFactory.Create(null), EvalResultFactory.Create(0)],
            [EvalResultFactory.Create(100)]
        ];

        Assert.Equal(50, SweepRunAggregator.ComputeMean(runs));
    }

    [Fact]
    public void SweepEntry_AllUnavailable_HasNoVarianceOrLatency()
    {
        var entry = new SweepEntry
        {
            Profile = new ModelParameterProfile { Name = "unavailable" },
            Results = [EvalResultFactory.Create(null)],
            AllRunResults =
            [
                [EvalResultFactory.Create(null)],
                [EvalResultFactory.Create(null)]
            ]
        };

        Assert.True((object?)entry.ScoreVariance is null);
        Assert.True((object?)entry.ScoreStdDev is null);
        Assert.True((object?)entry.AverageLatencyMs is null);
    }

    [Fact]
    public void SweepEntry_GenuineZero_HasMeasuredZeroVariance()
    {
        var entry = new SweepEntry
        {
            Profile = new ModelParameterProfile { Name = "zero" },
            Results = [EvalResultFactory.Create(0)],
            AllRunResults = [[EvalResultFactory.Create(0)]]
        };

        Assert.Equal(0, entry.ScoreVariance);
    }
}
