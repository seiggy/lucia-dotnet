using AgentEval.DataLoaders;
using AgentEval.Models;
using lucia.EvalHarness;
using lucia.EvalHarness.Configuration;
using lucia.EvalHarness.Evaluation;
using lucia.EvalHarness.Infrastructure;
using lucia.EvalHarness.Providers;
using lucia.EvalHarness.Reports;
using lucia.EvalHarness.Tui;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Spectre.Console;

// ─── Load Configuration ──────────────────────────────────────────────
var configBuilder = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables(prefix: "Harness__")
    .AddUserSecrets<HarnessConfiguration>();

var configRoot = configBuilder.Build();
var config = new HarnessConfiguration();
configRoot.GetSection("Harness").Bind(config);

// ─── Initialize Services ─────────────────────────────────────────────
using var httpClient = new HttpClient();
var discovery = new OllamaModelDiscovery(httpClient, config.Ollama);
var backendDiscovery = new BackendModelDiscovery(httpClient);
var gpuEnv = new GpuEnvironment(config, discovery);

// ─── Resolve Backends ────────────────────────────────────────────────
var effectiveBackends = config.GetEffectiveBackends();

// ─── GPU Detection ───────────────────────────────────────────────────
var gpuInfo = await gpuEnv.DetectAsync();

// ─── Backend Connectivity Check ──────────────────────────────────────
var anyBackendAvailable = false;
foreach (var backend in effectiveBackends)
{
    var available = await backendDiscovery.IsAvailableAsync(backend);
    if (available)
    {
        anyBackendAvailable = true;
        AnsiConsole.MarkupLine($"[green]✓[/] Backend [bold]{Markup.Escape(backend.Name)}[/] ({Markup.Escape(backend.Endpoint)}) — online");
    }
    else
    {
        AnsiConsole.MarkupLine($"[red]✗[/] Backend [bold]{Markup.Escape(backend.Name)}[/] ({Markup.Escape(backend.Endpoint)}) — unreachable");
    }
}

// Legacy Ollama check for WelcomeScreen compat
var ollamaAvailable = anyBackendAvailable;

// ─── Welcome Screen ──────────────────────────────────────────────────
await WelcomeScreen.RenderAsync(config, gpuInfo, ollamaAvailable);

if (!ollamaAvailable)
{
    return 1;
}

// ─── Backend Selection ───────────────────────────────────────────────
var selectedBackends = BackendSelector.Select(effectiveBackends);
if (selectedBackends.Count == 0) return 0;

// ─── Model Discovery (union across all selected backends) ────────────
var allDiscoveredModels = new Dictionary<string, DiscoveredModel>(StringComparer.OrdinalIgnoreCase);
foreach (var backend in selectedBackends)
{
    var backendModels = await backendDiscovery.ListModelsAsync(backend);
    foreach (var m in backendModels)
    {
        allDiscoveredModels.TryAdd(m.Name, m);
    }
}

// Also discover via legacy Ollama path for backward compat
IReadOnlyList<OllamaModelInfo> models = [];
try
{
    models = await discovery.ListModelsAsync();
}
catch (Exception exception)
    when (exception is HttpRequestException or TimeoutException or OperationCanceledException)
{
    AnsiConsole.MarkupLine("[yellow]\u26a0[/] Legacy Ollama model discovery unavailable.");
}
foreach (var m in models)
{
    allDiscoveredModels.TryAdd(m.Name, new DiscoveredModel
    {
        Name = m.Name, Size = m.Size,
        ParameterSize = m.ParameterSize, QuantizationLevel = m.QuantizationLevel
    });
}

if (allDiscoveredModels.Count == 0)
{
    AnsiConsole.MarkupLine("[red]No models found on any backend. Pull a model first:[/]");
    AnsiConsole.MarkupLine("[dim]  ollama pull llama3.2[/]");
    return 1;
}

// ─── Interactive Model Selection ─────────────────────────────────────
var discoveredList = allDiscoveredModels.Values
    .Select(d => new OllamaModelInfo
    {
        Name = d.Name, Size = d.Size,
        ParameterSize = d.ParameterSize, QuantizationLevel = d.QuantizationLevel
    }).ToList();
var selectedModels = ModelSelector.Select(discoveredList);
if (selectedModels.Count == 0) return 0;

// ─── Eval Type Selection ──────────────────────────────────────────────
// ─── Create Judge Client (Azure OpenAI) ──────────────────────────────
var judgeChatClient = JudgeClientFactory.Create(config.AzureOpenAI);
if (judgeChatClient is not null)
{
    AnsiConsole.MarkupLine(
        $"[green]\u2713[/] Azure judge model configured: {Markup.Escape(config.AzureOpenAI.JudgeDeployment)}");
}
else
{
    AnsiConsole.MarkupLine("[yellow]\u26a0[/] No Azure OpenAI judge configured. LLM-evaluated metrics disabled.");
}
AnsiConsole.WriteLine();

var evalType = EvalTypeSelector.Select();

if (evalType == EvalTypeSelector.PersonalityEval)
{
    // ─── Personality Eval Flow ────────────────────────────────────────
    IReadOnlyList<lucia.EvalHarness.Personality.PersonalityProfile> allProfiles;
    IReadOnlyList<lucia.EvalHarness.Personality.PersonalityEvalScenario> scenarios;

    try
    {
        allProfiles = lucia.EvalHarness.Personality.PersonalityEvalRunner.LoadProfiles();
        scenarios = lucia.EvalHarness.Personality.PersonalityEvalRunner.LoadScenarios();
    }
    catch (FileNotFoundException ex)
    {
        AnsiConsole.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
        return 1;
    }

    AnsiConsole.MarkupLine($"[dim]Loaded {scenarios.Count} scenarios and {allProfiles.Count} personality profiles[/]");
    AnsiConsole.WriteLine();

    // ─── Judge: reuse Azure OpenAI judge from main config ────────────
    var judgeModelName = judgeChatClient is null
        ? "not configured"
        : config.AzureOpenAI.JudgeDeployment;
    AnsiConsole.MarkupLine(
        judgeChatClient is null
            ? "[yellow]\u26a0[/] Judge unavailable; scores will be reported as N/A."
            : $"[green]\u2713[/] Judge: [bold]{Markup.Escape(judgeModelName)}[/] (Azure OpenAI)");
    AnsiConsole.WriteLine();

    var personalityProfiles = lucia.EvalHarness.Personality.PersonalityProfileSelector.Select(allProfiles);
    if (personalityProfiles.Count == 0) return 0;

    var combinations = lucia.EvalHarness.Personality.PersonalityEvalRunner.CountCombinations(scenarios, personalityProfiles);
    AnsiConsole.MarkupLine(
        $"[dim]Running {combinations} scenario\u00d7profile combinations per model " +
        $"({selectedModels.Count} model(s)), judged by {Markup.Escape(judgeModelName)}...[/]");
    AnsiConsole.WriteLine();

    AnsiConsole.Write(new Rule("[bold]Running Personality Eval[/]").LeftJustified());
    AnsiConsole.WriteLine();

    var reports = await lucia.EvalHarness.Personality.PersonalityEvalDisplay.RunWithProgressAsync(
        config.Ollama.Endpoint,
        selectedModels,
        judgeChatClient,
        judgeModelName,
        scenarios,
        personalityProfiles);

    lucia.EvalHarness.Personality.PersonalityEvalDisplay.RenderReport(reports);

    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[dim]Personality evaluation complete.[/]");
    return 0;
}

// ─── Standard Agent Eval Flow ────────────────────────────────────────

// Create real agent factory (uses HA snapshot + FakeItEasy fakes for non-LLM deps)
var loggerFactory = LoggerFactory.Create(builder =>
    builder.AddConsole().SetMinimumLevel(LogLevel.Warning));

var haSnapshotPath = Path.Combine(AppContext.BaseDirectory, "TestData", "ha-snapshot.json");

var availableAgents = new RealAgentFactory(config.Ollama.Endpoint, haSnapshotPath, loggerFactory)
    .AgentFactories.Keys.ToList();
var selectedAgents = AgentSelector.Select(availableAgents);
if (selectedAgents.Count == 0) return 0;

var testScope = TestSuiteSelector.Select();

// Scenario-based evaluation requires conversation tracing for tool call validation.
// Standard agent eval uses scenario datasets, so tracing is auto-enabled to prevent
// false "0 tool calls" results when the tracer is absent.
var enableTraces = true;
AnsiConsole.MarkupLine("[yellow]ℹ Conversation tracing auto-enabled (required for scenario tool call validation)[/]");
AnsiConsole.WriteLine();

// ─── Parameter Profile Selection ─────────────────────────────────────
var selectedProfiles = ParameterSelector.SelectMultiple(config.GetAllProfiles());
foreach (var p in selectedProfiles)
    AnsiConsole.MarkupLine($"[green]✓[/] Profile: [bold]{Markup.Escape(p.Name)}[/] ({Markup.Escape(p.ToSummary())})");
AnsiConsole.WriteLine();

// ─── Create Per-Backend Factories ────────────────────────────────────
var backendFactories = new List<(lucia.EvalHarness.Configuration.InferenceBackend, RealAgentFactory)>();
var disposableFactories = new List<RealAgentFactory>();
foreach (var backend in selectedBackends)
{
    var factory = new RealAgentFactory(backend, haSnapshotPath, loggerFactory);
    factory.EnableTracing = enableTraces;
    backendFactories.Add((backend, factory));
    disposableFactories.Add(factory);
}

var runner = new EvalRunner(config, judgeChatClient);

// ─── Run Evaluations ─────────────────────────────────────────────────
AnsiConsole.Write(new Rule("[bold]Running Evaluations[/]").LeftJustified());
var backendLabel = selectedBackends.Count > 1
    ? string.Join(" + ", selectedBackends.Select(b => b.Name))
    : selectedBackends[0].Name;
AnsiConsole.MarkupLine($"[dim]Constructing real lucia agents with {Markup.Escape(backendLabel)} backend(s) + HA snapshot data...[/]");
AnsiConsole.WriteLine();

var result = await EvalProgressDisplay.RunWithProgressAsync(
    runner,
    backendFactories,
    selectedModels,
    selectedAgents,
    datasetFile => LoadTestCases(datasetFile),
    testScope.MaxCasesPerAgent,
    parameterProfiles: selectedProfiles);

// ─── Render Report ───────────────────────────────────────────────────
ReportRenderer.Render(result, gpuInfo);

// ─── Export Report Files ────────────────────────────────────────────
var reportDir = config.ReportPath;
if (string.IsNullOrWhiteSpace(reportDir))
    reportDir = Path.Combine(Path.GetTempPath(), "lucia-eval-reports");

var exportedFiles = ReportExporter.Export(result, gpuInfo, reportDir);
AnsiConsole.WriteLine();
AnsiConsole.Write(new Rule("[bold]Exported Reports[/]").LeftJustified());
foreach (var file in exportedFiles)
{
    AnsiConsole.MarkupLine($"  [green]\u2713[/] {Markup.Escape(file)}");
}

// ─── HTML Report ────────────────────────────────────────────────────
try
{
    var htmlPath = HtmlReportGenerator.Generate(result, gpuInfo, reportDir);
    AnsiConsole.MarkupLine($"  [green]\u2713[/] {Markup.Escape(htmlPath)}");
}
catch (FileNotFoundException ex)
{
    AnsiConsole.MarkupLine($"  [yellow]\u26a0[/] HTML report skipped: {Markup.Escape(ex.Message)}");
}

// ─── Trace Export ───────────────────────────────────────────────────
if (enableTraces)
{
    var traceFiles = TraceExporter.Export(result, reportDir);
    AnsiConsole.WriteLine();
    AnsiConsole.Write(new Rule("[bold]Conversation Traces[/]").LeftJustified());
    foreach (var file in traceFiles)
    {
        AnsiConsole.MarkupLine($"  [green]\u2713[/] {Markup.Escape(file)}");
    }
    AnsiConsole.WriteLine();
    TraceExporter.RenderTraceSummary(result);
}

AnsiConsole.WriteLine();
AnsiConsole.MarkupLine("[dim]Evaluation complete.[/]");

// ─── Parameter Sweep (optional) ─────────────────────────────────────
// Sweep uses the first backend (primary) for consistency
var primaryFactory = backendFactories[0].Item2;
var sweepSelection = ParameterSweepSelector.Select(discoveredList, selectedAgents);
if (sweepSelection is not null)
{
    AnsiConsole.WriteLine();
    AnsiConsole.Write(new Rule("[bold]Parameter Sweep[/]").LeftJustified());
    AnsiConsole.WriteLine();

    var sweepRunner = new lucia.EvalHarness.Evaluation.ParameterSweepRunner(runner, primaryFactory);
    var sweepResult = await sweepRunner.RunAsync(
        sweepSelection.BaselineModel,
        sweepSelection.TargetModels,
        sweepSelection.AgentNames,
        sweepSelection.Config,
        datasetFile => LoadTestCases(datasetFile),
        testScope.MaxCasesPerAgent);

    var sweepFiles = lucia.EvalHarness.Reports.SweepReportGenerator.Export(sweepResult, reportDir);
    AnsiConsole.WriteLine();
    AnsiConsole.Write(new Rule("[bold]Sweep Reports[/]").LeftJustified());
    foreach (var file in sweepFiles)
    {
        AnsiConsole.MarkupLine($"  [green]✓[/] {Markup.Escape(file)}");
    }
}

// ─── Meta-Prompt Optimization (optional) ────────────────────────────
if (judgeChatClient is not null &&
    AnsiConsole.Confirm(
        "[cornflowerblue]Run prompt optimization analysis?[/] (uses GPT-5.4 to suggest system prompt improvements)",
        defaultValue: false))
{
    AnsiConsole.WriteLine();
    AnsiConsole.Write(new Rule("[bold]Prompt Optimization[/]").LeftJustified());
    AnsiConsole.WriteLine();

    var optimizer = new lucia.EvalHarness.Optimization.PromptOptimizer(judgeChatClient);
    var optimizationResults = new List<lucia.EvalHarness.Optimization.PromptOptimizationResult>();

    // Find the best-performing model's results as baseline
    var bestModelName = result.AgentResults
        .SelectMany(a => a.ModelResults)
        .GroupBy(m => m.ModelName)
        .Where(group => group.Any(model => model.OverallScore.HasValue))
        .OrderByDescending(group => group.Select(model => model.OverallScore).OfType<double>().Average())
        .FirstOrDefault()?.Key;
    if (bestModelName is null)
    {
        AnsiConsole.MarkupLine("[yellow]\u26a0[/] Prompt optimization skipped: no available model scores.");
        foreach (var factory in disposableFactories)
            await factory.DisposeAsync();
        return 0;
    }
    var baselineResults = result.AgentResults
        .SelectMany(a => a.ModelResults)
        .Where(m => m.ModelName == bestModelName)
        .ToList();

    // Target the worst-performing models
    var targetModelNames = result.AgentResults
        .SelectMany(a => a.ModelResults)
        .GroupBy(m => m.ModelName)
        .Where(group => group.Any(model => model.OverallScore.HasValue))
        .OrderBy(group => group.Select(model => model.OverallScore).OfType<double>().Average())
        .Take(2)
        .Select(g => g.Key)
        .ToList();

    foreach (var targetModel in targetModelNames)
    {
        var targetResults = result.AgentResults
            .SelectMany(a => a.ModelResults)
            .Where(m => m.ModelName == targetModel)
            .ToList();

        // Strip backend tag from model name for agent construction
        var rawModel = lucia.EvalHarness.Tui.BackendComparisonRenderer.ExtractBaseModel(targetModel);

        foreach (var agentResult in result.AgentResults)
        {
            var agentInstance = await primaryFactory.AgentFactories[agentResult.AgentName](rawModel);
            var systemPrompt = ExtractInstructions(agentInstance.Agent);

            try
            {
                AnsiConsole.MarkupLine($"[dim]  Analyzing {Markup.Escape(agentResult.AgentName)} × {Markup.Escape(targetModel)}...[/]");
                var optResult = await optimizer.OptimizeAsync(
                    agentResult.AgentName, targetModel, systemPrompt, targetResults, baselineResults);
                optimizationResults.Add(optResult);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[yellow]⚠[/] Optimization failed for {agentResult.AgentName}: {Markup.Escape(ex.Message)}");
            }
        }
    }

    if (optimizationResults.Count > 0)
    {
        lucia.EvalHarness.Tui.PromptOptimizationDisplay.RenderAll(optimizationResults);

        var optFiles = lucia.EvalHarness.Optimization.OptimizationExporter.Export(
            optimizationResults, reportDir, result.StartedAt);
        AnsiConsole.Write(new Rule("[bold]Optimization Reports[/]").LeftJustified());
        foreach (var file in optFiles)
        {
            AnsiConsole.MarkupLine($"  [green]✓[/] {Markup.Escape(file)}");
        }
    }
}

AnsiConsole.WriteLine();
AnsiConsole.MarkupLine("[dim]All done.[/]");

// ─── Cleanup ─────────────────────────────────────────────────────────
foreach (var f in disposableFactories)
    await f.DisposeAsync();

return 0;

// ═══════════════════════════════════════════════════════════════════════
// Local functions
// ═══════════════════════════════════════════════════════════════════════

static IReadOnlyList<TestCase> LoadTestCases(string datasetFile)
{
    if (!File.Exists(datasetFile))
    {
        AnsiConsole.MarkupLine($"[yellow]\u26a0 Dataset not found: {Markup.Escape(datasetFile)}[/]");
        return [];
    }

    try
    {
        var loader = DatasetLoaderFactory.CreateFromExtension(".yaml");
        var datasetCases = loader.LoadAsync(datasetFile).GetAwaiter().GetResult();
        return datasetCases.Select(dc => dc.ToTestCase()).ToList();
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[yellow]\u26a0 Failed to load {Markup.Escape(datasetFile)}: {Markup.Escape(ex.Message)}[/]");
        return [];
    }
}

static string ExtractInstructions(lucia.Agents.Abstractions.ILuciaAgent agent)
{
    // All concrete agents have an Instructions property, but it's not on ILuciaAgent
    var prop = agent.GetType().GetProperty("Instructions");
    return prop?.GetValue(agent) as string ?? "No system prompt available";
}
