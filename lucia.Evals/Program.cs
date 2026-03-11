using lucia.Evals;
using Spectre.Console;

AnsiConsole.Write(new FigletText("Lucia Evals").Color(Color.Blue));
AnsiConsole.MarkupLine("[grey]Agent evaluation runner powered by AgentEval SDK[/]");
AnsiConsole.WriteLine();

// Initialize fixture
EvalTestFixture fixture;
try
{
    fixture = new EvalTestFixture();
    await AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots)
        .StartAsync("[yellow]Initializing evaluation fixture...[/]", async _ =>
        {
            await fixture.InitializeAsync();
        });
    AnsiConsole.MarkupLine("[green]\u2713[/] Fixture initialized");
}
catch (Exception ex)
{
    AnsiConsole.MarkupLine($"[red]\u2717 Failed to initialize fixture:[/] {Markup.Escape(ex.Message)}");
    AnsiConsole.MarkupLine("[grey]Ensure Azure OpenAI credentials are configured in appsettings.json or user secrets.[/]");
    return 1;
}

// Build scenario groups
var groups = new List<EvalScenarioGroup>
{
    LightAgentScenarios.Create(fixture),
    MusicAgentScenarios.Create(fixture),
    FindLightSkillScenarios.Create(fixture),
    OrchestratorScenarios.Create(fixture),
};

var totalScenarios = groups.Sum(g => g.Scenarios.Count);
AnsiConsole.MarkupLine($"[green]\u2713[/] Loaded [bold]{totalScenarios}[/] scenarios across [bold]{groups.Count}[/] groups");
AnsiConsole.WriteLine();

// Interactive group selection
var selectedGroups = AnsiConsole.Prompt(
    new MultiSelectionPrompt<string>()
        .Title("[bold]Select scenario groups to run:[/]")
        .PageSize(10)
        .InstructionsText("[grey](Press [blue]<space>[/] to toggle, [green]<enter>[/] to run)[/]")
        .AddChoiceGroup("All Groups", groups.Select(g => $"{g.Name} ({g.Scenarios.Count} scenarios)")));

// Resolve selected scenarios
var scenariosToRun = new List<EvalScenario>();
foreach (var group in groups)
{
    var label = $"{group.Name} ({group.Scenarios.Count} scenarios)";
    if (selectedGroups.Contains(label))
    {
        scenariosToRun.AddRange(group.Scenarios);
    }
}

if (scenariosToRun.Count == 0)
{
    AnsiConsole.MarkupLine("[yellow]No scenarios selected. Exiting.[/]");
    return 0;
}

AnsiConsole.WriteLine();
AnsiConsole.Write(new Rule($"[bold blue]Running {scenariosToRun.Count} scenarios[/]").LeftJustified());
AnsiConsole.WriteLine();

// Run scenarios
var runner = new ScenarioRunner();
var report = await runner.RunAsync(scenariosToRun);

// Render results
AnsiConsole.WriteLine();
var reporter = new ResultReporter();
reporter.Render(report);

// Exit code: 0 if all passed, 1 if any failed
return report.Failed > 0 ? 1 : 0;
