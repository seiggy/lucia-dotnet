using lucia.EvalHarness.Configuration;
using lucia.EvalHarness.Infrastructure;
using Spectre.Console;

namespace lucia.EvalHarness.Tui;

/// <summary>
/// Renders the welcome banner, configuration summary, and GPU environment info.
/// </summary>
public static class WelcomeScreen
{
    public static async Task RenderAsync(
        HarnessConfiguration config,
        GpuInfo gpuInfo,
        bool ollamaAvailable)
    {
        AnsiConsole.Write(new FigletText("lucia eval")
            .LeftJustified()
            .Color(Color.CornflowerBlue));

        AnsiConsole.MarkupLine("[dim]AgentEval Harness for lucia AI Agents[/]");
        AnsiConsole.WriteLine();

        // Configuration table
        var configTable = new Table()
            .Border(TableBorder.Rounded)
            .Title("[bold]Configuration[/]")
            .AddColumn("Setting")
            .AddColumn("Value");

        var ollamaStatus = ollamaAvailable
            ? $"[green]\u2713[/] {Markup.Escape(config.Ollama.Endpoint)}"
            : $"[red]\u2717[/] {Markup.Escape(config.Ollama.Endpoint)} (unreachable)";

        var judgeStatus = !string.IsNullOrWhiteSpace(config.AzureOpenAI.Endpoint)
            ? $"[green]\u2713[/] {Markup.Escape(config.AzureOpenAI.JudgeDeployment)}"
            : "[yellow]Not configured[/] (LLM judge metrics disabled)";

        configTable.AddRow("Ollama Endpoint", ollamaStatus);
        configTable.AddRow("Azure Judge Model", judgeStatus);
        configTable.AddRow("GPU", Markup.Escape(gpuInfo.GpuLabel));
        configTable.AddRow("Report Path", config.ReportPath ?? "[dim]%TEMP%/lucia-eval-reports[/]");

        AnsiConsole.Write(configTable);
        AnsiConsole.WriteLine();

        if (!ollamaAvailable)
        {
            AnsiConsole.MarkupLine("[red bold]Ollama is not reachable.[/] Please start Ollama and try again.");
            AnsiConsole.MarkupLine($"[dim]Expected at: {Markup.Escape(config.Ollama.Endpoint)}[/]");
        }

        await Task.CompletedTask;
    }
}
