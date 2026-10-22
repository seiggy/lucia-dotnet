using lucia.EvalHarness.Configuration;
using lucia.EvalHarness.Personality;
using lucia.EvalHarness.Providers;
using lucia.EvalHarness.Reports;
using lucia.EvalHarness.Tests.TestDoubles;

namespace lucia.EvalHarness.Tests;

public sealed class PersonalityContextBudgetTests
{
    [Fact]
    public async Task Runner_PreservesRequestedInferenceProfile()
    {
        var profile = ModelParameterProfile.Default.WithContextFallback(32768);
        using var client = new ParameterInjectingChatClient(new CapturingChatClient(), profile);
        var runner = new PersonalityEvalRunner(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

        var report = await runner.RunAsync(client, "test", null, "none", [], [], parameterProfile: profile);

        Assert.Equal(32768, report.ParameterProfile?.NumContext);
    }

    [Fact]
    public void Exports_PreserveRequestedPersonalityContext()
    {
        var report = new PersonalityEvalReport
        {
            ModelName = "test",
            JudgeModelName = "none",
            Results = [],
            ParameterProfile = ModelParameterProfile.Default.WithContextFallback(32768),
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow
        };
        var directory = Path.Combine(Path.GetTempPath(), $"lucia-personality-context-{Guid.NewGuid():N}");
        try
        {
            var paths = PersonalityCostReport.Export([report], directory);
            foreach (var path in paths)
                Assert.Contains("32768", File.ReadAllText(path));
            Assert.Contains("Requested context", File.ReadAllText(paths.Single(path => path.EndsWith(".html"))));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
