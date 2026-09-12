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
    .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true, reloadOnChange: false)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddUserSecrets<HarnessConfiguration>()
    .AddEnvironmentVariables();

var configRoot = configBuilder.Build();
var config = new HarnessConfiguration();
configRoot.GetSection("Harness").Bind(config);
config.Validate();

var listModelsOnly = args.Contains("--list-backend-models");
using var judgeChatClient = listModelsOnly ? null : await JudgeSelector.SelectAsync(
    config, interactive: !args.Contains("--check-judge"));
if (args.Contains("--check-judge"))
{
    if (judgeChatClient is null)
    {
        throw new InvalidOperationException("Configure a judge before running --check-judge.");
    }

    using var deadline = new CancellationTokenSource(config.JudgeTimeout);
    var response = await judgeChatClient.GetResponseAsync(
        [new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.User,
            "Evaluate this test: the user requested 'Say hello' and the assistant replied 'Hello!'. " +
            "Return only JSON with score 100 if the request was completed, and a short reasoning.")],
        new Microsoft.Extensions.AI.ChatOptions { ResponseFormat = Microsoft.Extensions.AI.ChatResponseFormat.Json },
        cancellationToken: deadline.Token);
    using var json = System.Text.Json.JsonDocument.Parse(response.Text);
    if (json.RootElement.GetProperty("score").GetInt32() != 100)
    {
        throw new InvalidOperationException("Judge returned an unexpected smoke-check score.");
    }

    AnsiConsole.MarkupLine($"[green]Judge connected:[/] {Markup.Escape(config.JudgeModelName)}");
    return 0;
}

// ─── Initialize Services ─────────────────────────────────────────────
using var httpClient = new HttpClient();
var discovery = new OllamaModelDiscovery(httpClient, config.Ollama);
var backendDiscovery = new BackendModelDiscovery(httpClient);
var gpuEnv = new GpuEnvironment(config, discovery);

// ─── Resolve Backends ────────────────────────────────────────────────
var effectiveBackends = config.GetEffectiveBackends();

// ─── GPU Detection ───────────────────────────────────────────────────
var gpuInfo = await gpuEnv.DetectAsync();

// ─── Backend Discovery ───────────────────────────────────────────────
var availableBackends = new List<InferenceBackend>();
var discoveredByBackend = new Dictionary<string, IReadOnlyList<OllamaModelInfo>>(StringComparer.OrdinalIgnoreCase);
foreach (var backend in effectiveBackends)
{
    if (backend.Type == InferenceBackendType.OpenRouter && string.IsNullOrWhiteSpace(backend.ApiKey))
    {
        AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(backend.Name)} needs an API key before running inference.[/]");
        continue;
    }

    try
    {
        var backendModels = await backendDiscovery.ListModelsAsync(backend);
        foreach (var model in backendModels)
        {
            if (model.Pricing is not null)
            {
                backend.ModelPricing.TryAdd(model.Name, model.Pricing);
            }
        }
        discoveredByBackend[backend.Name] = backendModels.Select(model => new OllamaModelInfo
        {
            Name = model.Name,
            Size = model.Size,
            ParameterSize = model.ParameterSize,
            QuantizationLevel = model.QuantizationLevel
        }).ToList();
        if (backendModels.Count == 0)
        {
            AnsiConsole.MarkupLine($"[yellow]No compatible models on {Markup.Escape(backend.Name)}.[/]");
            continue;
        }

        availableBackends.Add(backend);
        AnsiConsole.MarkupLine($"[green]\u2713[/] {Markup.Escape(backend.Name)}: {backendModels.Count} model(s) ({Markup.Escape(backend.Endpoint)})");
    }
    catch (Exception exception) when (exception is HttpRequestException or System.Text.Json.JsonException or
                                      Azure.Identity.AuthenticationFailedException or OperationCanceledException)
    {
        AnsiConsole.MarkupLine($"[red]Backend {Markup.Escape(backend.Name)} unavailable:[/] {Markup.Escape(exception.Message)}");
    }
}

if (listModelsOnly)
{
    foreach (var backend in availableBackends)
    {
        foreach (var model in discoveredByBackend[backend.Name])
        {
            AnsiConsole.WriteLine($"{backend.Name}: {model.Name}");
        }
    }

    return availableBackends.Count > 0 ? 0 : 1;
}

// Legacy Ollama check for WelcomeScreen compat
var ollamaAvailable = availableBackends.Count > 0;

// ─── Welcome Screen ──────────────────────────────────────────────────
await WelcomeScreen.RenderAsync(config, gpuInfo, ollamaAvailable);

if (!ollamaAvailable)
{
    return 1;
}

// ─── Backend Selection ───────────────────────────────────────────────
var selectedBackends = BackendSelector.Select(availableBackends);
if (selectedBackends.Count == 0) return 0;

// ─── Model Selection Per Backend ────────────────────────────────────
var modelsByBackend = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
foreach (var backend in selectedBackends)
{
    modelsByBackend[backend.Name] = ModelSelector.Select(discoveredByBackend[backend.Name], backend.Name);
    if (backend.Type is InferenceBackendType.AzureFoundry or InferenceBackendType.OpenRouter)
    {
        foreach (var model in modelsByBackend[backend.Name])
        {
            if (!backend.ModelPricing.TryGetValue(model, out var pricing) ||
                pricing.InputUsdPerMillionTokens is null || pricing.OutputUsdPerMillionTokens is null)
            {
                AnsiConsole.MarkupLine($"[yellow]Token pricing is unknown for {Markup.Escape(model)} on {Markup.Escape(backend.Name)}; cost will be N/A.[/]");
            }
        }
    }

    if (backend.Type == InferenceBackendType.AzureFoundry)
    {
        AnsiConsole.MarkupLine("[dim]Foundry Responses uses provider sampling defaults. Temperature, top-p, and seed sweeps do not apply.[/]");
    }
}

var selectedModels = modelsByBackend.Values.SelectMany(models => models).Distinct(StringComparer.Ordinal).ToList();
if (selectedModels.Count == 0) return 0;

// ─── Eval Type Selection ──────────────────────────────────────────────
// ─── Judge Configuration ────────────────────────────────────────────
if (judgeChatClient is not null)
{
    AnsiConsole.MarkupLine(
        $"[green]\u2713[/] {Markup.Escape(JudgeSelector.ProviderName(config.JudgeProvider))} judge: {Markup.Escape(config.JudgeModelName)}");
}
else
{
    AnsiConsole.MarkupLine("[yellow]\u26a0[/] No judge configured. LLM-evaluated metrics disabled.");
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

    // ─── Judge: reuse the selected provider ──────────────────────────
    var judgeModelName = judgeChatClient is null
        ? "not configured"
        : config.JudgeModelName;
    AnsiConsole.MarkupLine(
        judgeChatClient is null
            ? "[yellow]\u26a0[/] Judge unavailable; scores will be reported as N/A."
            : $"[green]\u2713[/] Judge: [bold]{Markup.Escape(judgeModelName)}[/] ({Markup.Escape(JudgeSelector.ProviderName(config.JudgeProvider))})");
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

    var reports = new List<lucia.EvalHarness.Personality.PersonalityEvalReport>();
    foreach (var backend in selectedBackends)
    {
        reports.AddRange(await lucia.EvalHarness.Personality.PersonalityEvalDisplay.RunWithProgressAsync(
            backend.Endpoint,
            modelsByBackend[backend.Name],
            judgeChatClient,
            judgeModelName,
            scenarios,
            personalityProfiles,
            config.AgentTimeout,
            config.JudgeTimeout,
            chatClientFactory: model => BackendChatClientFactory.CreateChatClient(backend, model),
            backendName: selectedBackends.Count > 1 ? backend.Name : null));
    }

    lucia.EvalHarness.Personality.PersonalityEvalDisplay.RenderReport(reports);
    PersonalityCostReport.Render(reports);
    var personalityReportPath = string.IsNullOrWhiteSpace(config.ReportPath)
        ? Path.Combine(Path.GetTempPath(), "lucia-eval-reports")
        : config.ReportPath;
    foreach (var file in PersonalityCostReport.Export(reports, personalityReportPath))
    {
        AnsiConsole.MarkupLine($"[green]\u2713[/] {Markup.Escape(file)}");
    }

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
    parameterProfiles: selectedProfiles,
    modelsByBackend: modelsByBackend);

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
var primaryBackend = selectedBackends[0];
var sweepSelection = primaryBackend.Type == InferenceBackendType.AzureFoundry
    ? null
    : ParameterSweepSelector.Select(discoveredByBackend[primaryBackend.Name], selectedAgents);
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

    var optimizer = new lucia.EvalHarness.Optimization.PromptOptimizer(judgeChatClient, config.JudgeTimeout);
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
            var targetBackendName = BackendComparisonRenderer.ExtractBackendName(targetModel);
            var targetFactory = targetBackendName is null
                ? primaryFactory
                : backendFactories.Single(pair => pair.Item1.Name == targetBackendName).Item2;
            var agentInstance = await targetFactory.AgentFactories[agentResult.AgentName](rawModel);
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
