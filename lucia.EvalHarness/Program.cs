using AgentEval.DataLoaders;
using AgentEval.Models;
using Azure;
using Azure.AI.OpenAI;
using lucia.EvalHarness.Configuration;
using lucia.EvalHarness.Evaluation;
using lucia.EvalHarness.Infrastructure;
using lucia.EvalHarness.Providers;
using lucia.EvalHarness.Reports;
using lucia.EvalHarness.Tui;
using Microsoft.Extensions.AI;
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
var gpuEnv = new GpuEnvironment(config, discovery);

// ─── GPU Detection ───────────────────────────────────────────────────
var gpuInfo = await gpuEnv.DetectAsync();

// ─── Ollama Connectivity Check ───────────────────────────────────────
var ollamaAvailable = await discovery.IsAvailableAsync();

// ─── Welcome Screen ──────────────────────────────────────────────────
await WelcomeScreen.RenderAsync(config, gpuInfo, ollamaAvailable);

if (!ollamaAvailable)
{
    return 1;
}

// ─── Model Discovery ─────────────────────────────────────────────────
IReadOnlyList<OllamaModelInfo> models;
await AnsiConsole.Status()
    .Spinner(Spinner.Known.Dots)
    .StartAsync("Discovering Ollama models...", async ctx =>
    {
        await Task.CompletedTask;
    });

models = await discovery.ListModelsAsync();

if (models.Count == 0)
{
    AnsiConsole.MarkupLine("[red]No models found on Ollama. Pull a model first:[/]");
    AnsiConsole.MarkupLine("[dim]  ollama pull llama3.2[/]");
    return 1;
}

// ─── Interactive Selection ───────────────────────────────────────────
var selectedModels = ModelSelector.Select(models);
if (selectedModels.Count == 0) return 0;

// ─── Eval Type Selection ──────────────────────────────────────────────
// ─── Create Judge Client (Azure OpenAI) ──────────────────────────────
IChatClient? judgeChatClient = null;
if (!string.IsNullOrWhiteSpace(config.AzureOpenAI.Endpoint))
{
    try
    {
        var azureClient = !string.IsNullOrWhiteSpace(config.AzureOpenAI.ApiKey)
            ? new AzureOpenAIClient(
                new Uri(config.AzureOpenAI.Endpoint),
                new AzureKeyCredential(config.AzureOpenAI.ApiKey))
            : new AzureOpenAIClient(
                new Uri(config.AzureOpenAI.Endpoint),
                new Azure.Identity.AzureCliCredential());

        judgeChatClient = azureClient
            .GetChatClient(config.AzureOpenAI.JudgeDeployment)
            .AsIChatClient();

        AnsiConsole.MarkupLine($"[green]\u2713[/] Azure judge model connected: {Markup.Escape(config.AzureOpenAI.JudgeDeployment)}");
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[yellow]\u26a0[/] Azure judge unavailable: {Markup.Escape(ex.Message)}");
        AnsiConsole.MarkupLine("[dim]  LLM-as-judge metrics (TaskCompletion) will be skipped.[/]");
    }
}
else
{
    AnsiConsole.MarkupLine("[yellow]\u26a0[/] No Azure OpenAI judge configured. LLM-evaluated metrics disabled.");
}
AnsiConsole.WriteLine();

// Use a no-op judge if Azure isn't configured
judgeChatClient ??= new NoOpChatClient();

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
    if (judgeChatClient is NoOpChatClient)
    {
        AnsiConsole.MarkupLine("[red]\u2717[/] Personality eval requires an Azure OpenAI judge model. Configure AzureOpenAI settings.");
        return 1;
    }

    var judgeModelName = config.AzureOpenAI.JudgeDeployment;
    AnsiConsole.MarkupLine($"[green]\u2713[/] Judge: [bold]{Markup.Escape(judgeModelName)}[/] (Azure OpenAI)");
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

var enableTraces = AnsiConsole.Confirm(
    "[cornflowerblue]Enable conversation traces?[/] (writes full input/tool calls/output per test case)", defaultValue: false);
AnsiConsole.WriteLine();

// ─── Parameter Profile Selection ─────────────────────────────────────
var selectedProfiles = ParameterSelector.SelectMultiple(config.GetAllProfiles());
foreach (var p in selectedProfiles)
    AnsiConsole.MarkupLine($"[green]\u2713[/] Profile: [bold]{Markup.Escape(p.Name)}[/] ({Markup.Escape(p.ToSummary())})");
AnsiConsole.WriteLine();

await using var agentFactory = new RealAgentFactory(config.Ollama.Endpoint, haSnapshotPath, loggerFactory);
agentFactory.EnableTracing = enableTraces;


var runner = new EvalRunner(config, judgeChatClient);

// ─── Run Evaluations ─────────────────────────────────────────────────
AnsiConsole.Write(new Rule("[bold]Running Evaluations[/]").LeftJustified());
AnsiConsole.MarkupLine("[dim]Constructing real lucia agents with Ollama backends + HA snapshot data...[/]");
AnsiConsole.WriteLine();

var result = await EvalProgressDisplay.RunWithProgressAsync(
    runner,
    agentFactory,
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
var sweepSelection = ParameterSweepSelector.Select(models, selectedAgents);
if (sweepSelection is not null)
{
    AnsiConsole.WriteLine();
    AnsiConsole.Write(new Rule("[bold]Parameter Sweep[/]").LeftJustified());
    AnsiConsole.WriteLine();

    var sweepRunner = new lucia.EvalHarness.Evaluation.ParameterSweepRunner(runner, agentFactory);
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
        AnsiConsole.MarkupLine($"  [green]\u2713[/] {Markup.Escape(file)}");
    }
}

// ─── Meta-Prompt Optimization (optional) ────────────────────────────
if (judgeChatClient is not NoOpChatClient &&
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
        .OrderByDescending(g => g.Average(m => m.OverallScore))
        .First().Key;
    var baselineResults = result.AgentResults
        .SelectMany(a => a.ModelResults)
        .Where(m => m.ModelName == bestModelName)
        .ToList();

    // Target the worst-performing models
    var targetModelNames = result.AgentResults
        .SelectMany(a => a.ModelResults)
        .GroupBy(m => m.ModelName)
        .OrderBy(g => g.Average(m => m.OverallScore))
        .Take(2)
        .Select(g => g.Key)
        .ToList();

    foreach (var targetModel in targetModelNames)
    {
        var targetResults = result.AgentResults
            .SelectMany(a => a.ModelResults)
            .Where(m => m.ModelName == targetModel)
            .ToList();

        foreach (var agentResult in result.AgentResults)
        {
            var agentInstance = await agentFactory.AgentFactories[agentResult.AgentName](targetModel);
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
                AnsiConsole.MarkupLine($"[yellow]\u26a0[/] Optimization failed for {agentResult.AgentName}: {Markup.Escape(ex.Message)}");
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
            AnsiConsole.MarkupLine($"  [green]\u2713[/] {Markup.Escape(file)}");
        }
    }
}

AnsiConsole.WriteLine();
AnsiConsole.MarkupLine("[dim]All done.[/]");

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

/// <summary>
/// No-op chat client used when Azure OpenAI judge is not configured.
/// Returns a neutral response so code-based metrics still work.
/// </summary>
file sealed class NoOpChatClient : IChatClient
{
    public ChatClientMetadata Metadata { get; } = new("NoOp");

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ChatResponse(
            [new ChatMessage(ChatRole.Assistant, """{"score": 50, "reasoning": "Judge not configured"}""")]));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return AsyncEnumerable.Empty<ChatResponseUpdate>();
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
