using System.Diagnostics;
using System.Text.Json;
using AgentEval.Core;
using AgentEval.Models;
using FakeItEasy;
using lucia.Agents.Abstractions;
using lucia.EvalHarness.Configuration;
using lucia.EvalHarness.Evaluation;
using lucia.EvalHarness.Infrastructure;
using lucia.EvalHarness.Providers;
using lucia.EvalHarness.Reports;
using lucia.EvalHarness.Tests.TestDoubles;
using lucia.EvalHarness.Tui;
using Spectre.Console;

namespace lucia.EvalHarness.Tests;

public sealed class ReportAvailabilityTests
{
    [Fact]
    public void Export_UnavailableJudge_RendersAndSerializesExplicitly()
    {
        var directory = CreateDirectory();
        try
        {
            var paths = ReportExporter.Export(CreateRun(), new GpuInfo("test"), directory);
            var markdown = File.ReadAllText(paths.Single(path => path.EndsWith(".md", StringComparison.Ordinal)));
            var json = File.ReadAllText(paths.Single(path => path.EndsWith(".json", StringComparison.Ordinal)));

            Assert.Contains("N/A (provider_error)", markdown, StringComparison.Ordinal);
            using var document = JsonDocument.Parse(json);
            var model = document.RootElement.GetProperty("agents")[0].GetProperty("models")[0];
            Assert.False(model.TryGetProperty("taskCompletionScore", out _));
            Assert.Equal(JudgeAvailability.ProviderError, model.GetProperty("taskCompletionStatus").GetString());
            Assert.Equal(100, model.GetProperty("overallScore").GetDouble());
            Assert.False(model.TryGetProperty("modelParameters", out _));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Generate_UnavailableJudge_EmbedsStatusForTemplate()
    {
        var directory = CreateDirectory();
        try
        {
            var path = HtmlReportGenerator.Generate(CreateRun(), new GpuInfo("test"), directory);
            var html = File.ReadAllText(path);

            Assert.Contains("\"taskCompletionStatus\":\"provider_error\"", html, StringComparison.Ordinal);
            Assert.Contains("taskCompletionStatus", html, StringComparison.Ordinal);
            Assert.Contains("N/A", html, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Export_GenuineZero_PreservesNumericWireFields()
    {
        var directory = CreateDirectory();
        try
        {
            var run = CreateRun(EvalResultFactory.Create(0, taskCompletionScore: 0));
            var paths = ReportExporter.Export(run, new GpuInfo("test"), directory);
            var json = File.ReadAllText(paths.Single(path => path.EndsWith(".json", StringComparison.Ordinal)));

            using var document = JsonDocument.Parse(json);
            var model = document.RootElement.GetProperty("agents")[0].GetProperty("models")[0];
            Assert.Equal(0, model.GetProperty("overallScore").GetDouble());
            Assert.Equal(100, model.GetProperty("toolSelectionScore").GetDouble());
            Assert.Equal(100, model.GetProperty("toolSuccessScore").GetDouble());
            Assert.Equal(100, model.GetProperty("toolEfficiencyScore").GetDouble());
            Assert.Equal(0, model.GetProperty("taskCompletionScore").GetDouble());
            Assert.False(model.TryGetProperty("taskCompletionStatus", out _));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Export_PassRate_DistinguishesUnavailableFromGenuineZero()
    {
        var directory = CreateDirectory();
        try
        {
            var unavailable = CreateRun(CreateModelResult("unavailable", null, 0, []));
            var unavailablePaths = ReportExporter.Export(unavailable, new GpuInfo("test"), directory);
            var unavailableMarkdown = File.ReadAllText(
                unavailablePaths.Single(path => path.EndsWith(".md", StringComparison.Ordinal)));

            var genuineZero = CreateRun(CreateModelResult("genuine-zero", 0, 1, []));
            var genuineZeroPaths = ReportExporter.Export(genuineZero, new GpuInfo("test"), directory);
            var genuineZeroMarkdown = File.ReadAllText(
                genuineZeroPaths.Single(path => path.EndsWith(".md", StringComparison.Ordinal)));

            Assert.Contains("| unavailable | N/A |", unavailableMarkdown, StringComparison.Ordinal);
            Assert.Contains("| genuine-zero | 0% |", genuineZeroMarkdown, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Render_PassRate_DistinguishesUnavailableFromGenuineZero()
    {
        var unavailable = CreateRun(CreateModelResult("unavailable", null, 0, []));
        var genuineZero = CreateRun(CreateModelResult("genuine-zero", 0, 1, []));

        var unavailableOutput = CaptureConsole(
            () => ReportRenderer.Render(unavailable, new GpuInfo("test")));
        var genuineZeroOutput = CaptureConsole(
            () => ReportRenderer.Render(genuineZero, new GpuInfo("test")));

        Assert.DoesNotContain("0%", unavailableOutput, StringComparison.Ordinal);
        Assert.Contains("N/A", unavailableOutput, StringComparison.Ordinal);
        Assert.Contains("0%", genuineZeroOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Exporters_AllUnavailable_DoNotRenderValidResultOrWinner()
    {
        var directory = CreateDirectory();
        try
        {
            var runner = new EvalRunner(new HarnessConfiguration(), null)
            {
                TestContextFactory = (_, _, _) =>
                    Task.FromException<(EvaluationContext, PerformanceSnapshot)>(
                        new HttpRequestException("provider unavailable"))
            };
            var agent = new RealAgentInstance
            {
                AgentName = "unavailable-model",
                Agent = A.Fake<ILuciaAgent>(),
                DatasetFile = "unused"
            };
            var modelResult = await runner.EvaluateRealAgentAsync(
                "unavailable-model",
                agent,
                [new TestCase { Name = "case", Input = "input" }]);
            var secondModelResult = await runner.EvaluateRealAgentAsync(
                "other-unavailable-model",
                agent,
                [new TestCase { Name = "case", Input = "input" }]);
            var run = CreateRun([modelResult, secondModelResult]);

            var htmlPath = HtmlReportGenerator.Generate(run, new GpuInfo("test"), directory);
            var comparison = RenderHtmlComparison(htmlPath, directory);
            Assert.Contains("N/A", comparison, StringComparison.Ordinal);
            Assert.DoesNotContain("best</span>", comparison, StringComparison.Ordinal);
            Assert.DoesNotContain("0ms", comparison, StringComparison.Ordinal);

            var reportPaths = ReportExporter.Export(run, new GpuInfo("test"), directory);
            var reportPath = reportPaths.Single(path => path.EndsWith(".json", StringComparison.Ordinal));
            using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
            var reportModel = report.RootElement.GetProperty("agents")[0].GetProperty("models")[0];
            Assert.False(reportModel.TryGetProperty("toolSelectionScore", out _));
            Assert.False(reportModel.TryGetProperty("toolSuccessScore", out _));
            Assert.False(reportModel.TryGetProperty("toolEfficiencyScore", out _));

            var tracePath = TraceExporter.Export(run, directory).First();
            using var trace = JsonDocument.Parse(File.ReadAllText(tracePath));
            var summary = trace.RootElement.GetProperty("summary");
            Assert.False(summary.TryGetProperty("overall_score", out _));
            Assert.Equal(
                JudgeAvailability.Unavailable,
                summary.GetProperty("overall_score_status").GetString());
            Assert.False(summary.TryGetProperty("mean_latency_ms", out _));

            var renderedTrace = CaptureConsole(() => TraceExporter.RenderTraceSummary(run));
            Assert.Contains("score: N/A", renderedTrace, StringComparison.Ordinal);
            Assert.DoesNotContain("score: 0", renderedTrace, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void GenerateComparison_UnavailableAndMeasuredZero_RenderDistinctly()
    {
        var directory = CreateDirectory();
        try
        {
            var unavailable = CreateModelResult("unavailable", null, 0, []);
            var measuredZero = CreateModelResult(
                "measured-zero",
                0,
                1,
                [new PerformanceSnapshot { TotalDuration = TimeSpan.Zero }]);
            var htmlPath = HtmlReportGenerator.Generate(
                CreateRun([unavailable, measuredZero]),
                new GpuInfo("test"),
                directory);

            var comparison = RenderHtmlComparison(htmlPath, directory);
            var unavailableRow = GetComparisonRow(comparison, unavailable.ModelName);
            var measuredZeroRow = GetComparisonRow(comparison, measuredZero.ModelName);

            Assert.Contains("N/A", unavailableRow, StringComparison.Ordinal);
            Assert.DoesNotContain("best</span>", unavailableRow, StringComparison.Ordinal);
            Assert.DoesNotContain("N/A", measuredZeroRow, StringComparison.Ordinal);
            Assert.Equal(7, CountOccurrences(measuredZeroRow, "best</span>"));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Export_UnavailablePerformance_IsNotRenderedOrRecommended()
    {
        var directory = CreateDirectory();
        try
        {
            var unavailable = CreateModelResult("unavailable", 100, 1, []);
            var measured = CreateModelResult(
                "measured",
                90,
                1,
                [new PerformanceSnapshot { TotalDuration = TimeSpan.FromMilliseconds(25) }]);
            var paths = ReportExporter.Export(
                CreateRun([unavailable, measured]),
                new GpuInfo("test"),
                directory);
            var markdown = File.ReadAllText(paths.Single(path => path.EndsWith(".md", StringComparison.Ordinal)));

            Assert.Contains("| unavailable | 0% |", markdown, StringComparison.Ordinal);
            Assert.Contains("| N/A |", markdown, StringComparison.Ordinal);
            Assert.Contains("- **Fastest:** measured", markdown, StringComparison.Ordinal);
            Assert.DoesNotContain("- **Fastest:** unavailable", markdown, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Generate_UnavailableDetail_RendersNeitherFailureNorZeroLatency()
    {
        var directory = CreateDirectory();
        try
        {
            var unavailable = CreateModelResult(
                "unavailable",
                null,
                0,
                [],
                [
                    new TestCaseResult
                    {
                        TestCaseId = "case",
                        Passed = false,
                        Score = null,
                        Latency = TimeSpan.Zero,
                        JudgeStatus = JudgeAvailability.ProviderError
                    }
                ]);
            var htmlPath = HtmlReportGenerator.Generate(
                CreateRun(unavailable),
                new GpuInfo("test"),
                directory);

            var agentDetails = RenderHtmlPanel(htmlPath, directory, "renderAgents()", "#tab-agents");
            var traces = RenderHtmlPanel(htmlPath, directory, "filterTraces()", "#trace-list");
            var summary = RenderHtmlPanel(htmlPath, directory, "renderSummary()", "#tab-summary");
            var markdownPath = ReportExporter.Export(
                CreateRun(unavailable),
                new GpuInfo("test"),
                directory).Single(path => path.EndsWith(".md", StringComparison.Ordinal));
            var markdown = File.ReadAllText(markdownPath);

            Assert.Contains("N/A (provider_error)", agentDetails, StringComparison.Ordinal);
            Assert.Contains("Mean: <b class=\"font-mono\">N/A</b>", agentDetails, StringComparison.Ordinal);
            Assert.DoesNotContain("FAIL", agentDetails, StringComparison.Ordinal);
            Assert.Contains("N/A (provider_error)", traces, StringComparison.Ordinal);
            Assert.DoesNotContain("FAIL", traces, StringComparison.Ordinal);
            Assert.Contains("width:0%", summary, StringComparison.Ordinal);
            Assert.Contains("| case | N/A | N/A (provider_error) | N/A |", markdown, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void ComparisonAggregates_UnmeasuredResultsKeepScoresButNotLatency()
    {
        var profileRun = CreateRun(
        [
            CreateModelResult(
                "profile-model",
                100,
                1,
                [],
                parameterProfile: new ModelParameterProfile { Name = "profile-a" }),
            CreateModelResult(
                "profile-model",
                100,
                1,
                [],
                parameterProfile: new ModelParameterProfile { Name = "profile-b" })
        ]);
        var html = HtmlReportData.FromEvalResult(profileRun, new GpuInfo("test"));
        var profileScores = Assert.Single(html.ProfileComparison!).Profiles;

        Assert.All(profileScores, profile =>
        {
            Assert.Equal(0, profile.PassRate);
            Assert.Equal(100, profile.AvgOverall);
            Assert.Null(profile.AvgLatencyMs);
        });

        var backendRun = CreateRun(
        [
            CreateModelResult("backend-model@first", 100, 1, []),
            CreateModelResult("backend-model@second", 100, 1, [])
        ]);
        var markdown = new System.Text.StringBuilder();
        BackendComparisonRenderer.AppendMarkdown(markdown, backendRun);

        Assert.Contains("| **Overall Score** | 100.0 | 100.0 |", markdown.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("0ms", markdown.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_PartialJudgeScore_RendersStatusWithNumericScore()
    {
        var directory = CreateDirectory();
        try
        {
            var result = CreateModelResult(
                "partial",
                50,
                1,
                [new PerformanceSnapshot { TotalDuration = TimeSpan.FromMilliseconds(10) }],
                [
                    new TestCaseResult
                    {
                        TestCaseId = "case",
                        Passed = false,
                        Score = 50,
                        Latency = TimeSpan.FromMilliseconds(10),
                        JudgeStatus = JudgeAvailability.Partial
                    }
                ],
                overallStatus: JudgeAvailability.Partial);
            var htmlPath = HtmlReportGenerator.Generate(
                CreateRun(result),
                new GpuInfo("test"),
                directory);

            var agentDetails = RenderHtmlPanel(htmlPath, directory, "renderAgents()", "#tab-agents");
            var markdownPath = ReportExporter.Export(
                CreateRun(result),
                new GpuInfo("test"),
                directory).Single(path => path.EndsWith(".md", StringComparison.Ordinal));
            var markdown = File.ReadAllText(markdownPath);
            var rendered = CaptureConsole(() => ReportRenderer.Render(CreateRun(result), new GpuInfo("test")));

            Assert.Contains("50.0 (partial)", agentDetails, StringComparison.Ordinal);
            Assert.Contains("50.0 (partial)", markdown, StringComparison.Ordinal);
            Assert.Contains("50.0", rendered, StringComparison.Ordinal);
            Assert.Contains("partial", rendered, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static EvalRunResult CreateRun(ModelEvalResult? modelResult = null) =>
        CreateRun(
        [
            modelResult ?? EvalResultFactory.Create(
                100,
                taskCompletionScore: null,
                taskCompletionStatus: JudgeAvailability.ProviderError)
        ]);

    private static EvalRunResult CreateRun(IReadOnlyList<ModelEvalResult> modelResults) =>
        new()
        {
            RunId = "run",
            StartedAt = DateTimeOffset.UnixEpoch,
            CompletedAt = DateTimeOffset.UnixEpoch.AddSeconds(1),
            AgentResults = modelResults
                .Select((result, index) => new AgentEvalResult
                {
                    AgentName = $"agent-{index}",
                    ModelResults = [result]
                })
                .ToList()
        };

    private static ModelEvalResult CreateModelResult(
        string modelName,
        double? score,
        int testCaseCount,
        IReadOnlyList<PerformanceSnapshot> snapshots,
        IReadOnlyList<TestCaseResult>? testCases = null,
        ModelParameterProfile? parameterProfile = null,
        string? overallStatus = null) =>
        new()
        {
            ModelName = modelName,
            AgentName = "agent",
            ToolSelectionScore = score,
            ToolSuccessScore = score,
            ToolEfficiencyScore = score,
            TaskCompletionScore = score,
            OverallScore = score,
            OverallScoreStatus = score.HasValue ? overallStatus : JudgeAvailability.Unavailable,
            OverallScoreReason = score.HasValue ? null : JudgeAvailability.Reason(JudgeAvailability.Unavailable),
            TestCaseCount = testCaseCount,
            ScoredTestCaseCount = testCaseCount,
            PassedCount = 0,
            Performance = ModelPerformanceSummary.FromSnapshots(modelName, snapshots),
            TestCaseResults = testCases ?? [],
            ParameterProfile = parameterProfile
        };

    private static string RenderHtmlComparison(string htmlPath, string directory)
    {
        return RenderHtmlPanel(htmlPath, directory, "renderComparison()", "#tab-comparison");
    }

    private static string RenderHtmlPanel(
        string htmlPath,
        string directory,
        string renderCommand,
        string panelSelector)
    {
        const string ScriptStart = "<script>";
        const string ScriptEnd = "</script>";
        var html = File.ReadAllText(htmlPath);
        var scriptStart = html.LastIndexOf(ScriptStart, StringComparison.Ordinal) + ScriptStart.Length;
        var scriptEnd = html.IndexOf(ScriptEnd, scriptStart, StringComparison.Ordinal);
        var script = """
            const elements = new Map();
            global.window = {};
            global.requestAnimationFrame = callback => callback();
            global.localStorage = { getItem() { return null; }, setItem() {} };
            global.document = {
              querySelector(selector) {
                if (!elements.has(selector)) elements.set(selector, { innerHTML: '', textContent: '', value: '__all__', addEventListener() {} });
                return elements.get(selector);
              },
              querySelectorAll() { return []; },
              addEventListener() {},
              getElementById() { return null; },
              documentElement: {
                classList: { add() {}, remove() {}, toggle() {}, contains() { return true; } }
              }
            };
            """ + html[scriptStart..scriptEnd] + """

            """ + renderCommand + $"""
            ;
            process.stdout.write(document.querySelector('{panelSelector}').innerHTML);
            """;
        var scriptPath = Path.Combine(directory, "render-comparison.js");
        File.WriteAllText(scriptPath, script);

        var startInfo = new ProcessStartInfo("node")
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(scriptPath);

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error);
        return output;
    }

    private static string GetComparisonRow(string comparison, string modelName)
    {
        var modelIndex = comparison.IndexOf($">{modelName}</td>", StringComparison.Ordinal);
        var rowStart = comparison.LastIndexOf("<tr", modelIndex, StringComparison.Ordinal);
        var rowEnd = comparison.IndexOf("</tr>", modelIndex, StringComparison.Ordinal) + "</tr>".Length;
        return comparison[rowStart..rowEnd];
    }

    private static int CountOccurrences(string value, string search) =>
        (value.Length - value.Replace(search, string.Empty, StringComparison.Ordinal).Length) / search.Length;

    private static string CaptureConsole(Action render)
    {
        var previousConsole = AnsiConsole.Console;
        using var writer = new StringWriter();
        AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(writer)
        });
        try
        {
            render();
        }
        finally
        {
            AnsiConsole.Console = previousConsole;
        }

        return writer.ToString();
    }

    private static string CreateDirectory()
    {
        var directory = Path.Combine(
            AppContext.BaseDirectory,
            "report-availability-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
