using System.Collections;
using System.Diagnostics;
using GitHub.Copilot;
using lucia.EvalHarness.Configuration;
using lucia.EvalHarness.Evaluation;
using Microsoft.Extensions.AI;
using Spectre.Console;

namespace lucia.EvalHarness.Tui;

public static class JudgeSelector
{
    public static async Task<IChatClient?> SelectAsync(
        HarnessConfiguration config,
        bool interactive = true,
        CancellationToken cancellationToken = default)
    {
        if (interactive)
        {
            config.JudgeProvider = AnsiConsole.Prompt(new SelectionPrompt<JudgeProvider>()
                .Title("[cornflowerblue]Select judge provider:[/]")
                .UseConverter(ProviderName)
                .AddChoices(Enum.GetValues<JudgeProvider>()
                    .OrderBy(provider => provider == config.JudgeProvider ? 0 : 1)));
        }

        if (config.JudgeProvider == JudgeProvider.None)
        {
            return null;
        }

        if (config.JudgeProvider == JudgeProvider.AzureOpenAI)
        {
            return JudgeClientFactory.Create(config.AzureOpenAI);
        }

        CopilotClient? client = CreateCopilotClient();
        try
        {
            await client.StartAsync(cancellationToken);
            var auth = await client.GetAuthStatusAsync(cancellationToken);
            if (interactive)
            {
                AnsiConsole.MarkupLine(auth.IsAuthenticated
                    ? $"[green]Signed in to GitHub as {Markup.Escape(auth.Login ?? "current user")}.[/]"
                    : "[yellow]GitHub sign-in is required.[/]");
            }

            if (!auth.IsAuthenticated ||
                interactive && AnsiConsole.Confirm("Sign in with a different GitHub account?", defaultValue: false))
            {
                if (!interactive)
                {
                    throw new InvalidOperationException("Copilot is not authenticated. Run 'copilot login' first.");
                }

                await client.DisposeAsync();
                client = null;
                using var login = Process.Start(new ProcessStartInfo("copilot")
                {
                    UseShellExecute = false,
                    ArgumentList = { "login", "--device-code" }
                }) ?? throw new InvalidOperationException("Could not start 'copilot login'. Install the GitHub Copilot CLI.");
                await login.WaitForExitAsync(cancellationToken);
                if (login.ExitCode != 0)
                {
                    throw new InvalidOperationException($"GitHub sign-in failed with exit code {login.ExitCode}.");
                }

                client = CreateCopilotClient();
                await client.StartAsync(cancellationToken);
                auth = await client.GetAuthStatusAsync(cancellationToken);
                if (!auth.IsAuthenticated)
                {
                    throw new InvalidOperationException("Copilot could not use the GitHub login. Run 'copilot login' and retry.");
                }
            }

            var models = (await client.ListModelsAsync(cancellationToken))
                .Where(model => model.Policy?.State is null or "enabled")
                .OrderBy(model => model.Id == config.GitHubCopilot.Model ? 0 : 1)
                .ThenBy(model => model.Id, StringComparer.Ordinal)
                .ToList();
            if (models.Count == 0)
            {
                throw new InvalidOperationException("No Copilot models are available for this GitHub account and policy.");
            }

            var selected = interactive
                ? AnsiConsole.Prompt(new SelectionPrompt<ModelInfo>()
                    .Title("[cornflowerblue]Select Copilot judge model:[/]")
                    .PageSize(15)
                    .UseConverter(model => Markup.Escape($"{model.Name} [{model.Id}]"))
                    .AddChoices(models))
                : models.Find(model => model.Id == config.GitHubCopilot.Model)
                  ?? throw new InvalidOperationException("Harness:GitHubCopilot:Model must name an available Copilot model.");
            config.GitHubCopilot.Model = selected.Id;

            if (interactive)
            {
                SelectControls(config.GitHubCopilot, selected);
                AnsiConsole.MarkupLine("[dim]Copilot plan limits, model policies, and usage charges apply.[/]");
            }

            config.GitHubCopilot.ValidateForModel(selected);
            var judge = new CopilotJudgeChatClient(client, config.GitHubCopilot, config.JudgeTimeout);
            client = null;
            return judge;
        }
        finally
        {
            if (client is not null)
            {
                await client.DisposeAsync();
            }
        }
    }

    public static string ProviderName(JudgeProvider provider) => provider switch
    {
        JudgeProvider.AzureOpenAI => "Azure OpenAI / Foundry",
        JudgeProvider.GitHubCopilot => "GitHub Copilot",
        JudgeProvider.None => "No judge",
        _ => throw new ArgumentOutOfRangeException(nameof(provider))
    };

    private static CopilotClient CreateCopilotClient() => new(new CopilotClientOptions
    {
        UseLoggedInUser = true,
        WorkingDirectory = Path.GetTempPath(),
        Environment = CreateChildEnvironment()
    });

    internal static IReadOnlyDictionary<string, string> CreateChildEnvironment() =>
        // The SDK replaces, rather than merges, the child environment.
        Environment.GetEnvironmentVariables().Cast<DictionaryEntry>()
            .Where(entry => (string)entry.Key is not ("COPILOT_GITHUB_TOKEN" or "GH_TOKEN" or "GITHUB_TOKEN") &&
                            !((string)entry.Key).StartsWith("COPILOT_GH_ACCOUNT_", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!);

    private static void SelectControls(CopilotJudgeSettings settings, ModelInfo model)
    {
        var prices = model.Billing?.TokenPrices;
        var contexts = new List<string> { "default" };
        if (prices?.LongContext is not null)
        {
            contexts.Add("long_context");
        }

        settings.ContextTier = AnsiConsole.Prompt(new SelectionPrompt<string>()
            .Title("[cornflowerblue]Select context size:[/]")
            .UseConverter(tier =>
            {
                var promptLimit = tier == "long_context"
                    ? prices?.LongContext?.MaxPromptTokens
                    : prices?.MaxPromptTokens ?? model.Capabilities.Limits.MaxPromptTokens;
                return $"{(tier == "long_context" ? "Long context" : "Default")}" +
                       (promptLimit is > 0 ? $" ({promptLimit:N0} prompt tokens)" : " (provider default)");
            })
            .AddChoices(contexts.OrderBy(tier => tier == settings.ContextTier ? 0 : 1)));

        var efforts = model.Capabilities.Supports.ReasoningEffort
            ? model.SupportedReasoningEfforts?.ToList() ?? []
            : [];
        if (efforts.Count == 0)
        {
            settings.ReasoningEffort = null;
            AnsiConsole.MarkupLine("[dim]This model does not expose reasoning effort selection.[/]");
            return;
        }

        settings.ReasoningEffort = AnsiConsole.Prompt(new SelectionPrompt<string>()
            .Title("[cornflowerblue]Select reasoning effort:[/]")
            .AddChoices(efforts.OrderBy(effort =>
                effort == (settings.ReasoningEffort ?? model.DefaultReasoningEffort) ? 0 : 1)));
    }
}
