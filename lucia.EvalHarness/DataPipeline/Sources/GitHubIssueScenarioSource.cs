using System.Text.Json;
using System.Text.RegularExpressions;
using lucia.EvalHarness.DataPipeline.Models;
using Microsoft.Extensions.Logging;

namespace lucia.EvalHarness.DataPipeline.Sources;

/// <summary>
/// Converts GitHub issues into evaluation scenarios.
/// Parses issue body to extract: user intent, expected behavior, actual behavior, agent involved.
/// </summary>
public sealed class GitHubIssueScenarioSource : IEvalScenarioSource
{
    private readonly ILogger<GitHubIssueScenarioSource> _logger;
    private readonly string _repositoryPath;

    private static readonly Regex TraceReportRegex = new(
        @"## (?:Conversation Trace Report|Command Trace Report)",
        RegexOptions.Compiled | RegexOptions.Multiline
    );

    private static readonly Regex UserInputRegex = new(
        @"### (?:User Input|Raw Input)\s*```\s*(.+?)\s*```",
        RegexOptions.Compiled | RegexOptions.Singleline
    );

    private static readonly Regex ExpectedBehaviorRegex = new(
        @"### Expected Behavior\s*(.+?)(?=###|$)",
        RegexOptions.Compiled | RegexOptions.Singleline
    );

    private static readonly Regex AgentExecutionsRegex = new(
        @"\*\*Agents\*\*\s*\|\s*(.+?)\s*\|",
        RegexOptions.Compiled | RegexOptions.Singleline
    );

    private static readonly Regex SelectedAgentRegex = new(
        @"- \*\*Selected Agent:\*\*\s*(.+?)\s*$",
        RegexOptions.Compiled | RegexOptions.Multiline
    );

    public GitHubIssueScenarioSource(
        ILogger<GitHubIssueScenarioSource> logger,
        string? repositoryPath = null)
    {
        _logger = logger;
        _repositoryPath = repositoryPath ?? Environment.CurrentDirectory;
    }

    public async Task<List<EvalScenario>> GetScenariosAsync(ScenarioFilter? filter = null, CancellationToken ct = default)
    {
        var scenarios = new List<EvalScenario>();

        var issuesJson = await ExecuteGhCommandAsync(
            "issue list --state all --json number,title,labels,body --limit 100",
            ct
        ).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(issuesJson))
        {
            _logger.LogWarning("No issues retrieved from GitHub CLI");
            return scenarios;
        }

        var issues = JsonSerializer.Deserialize<List<GitHubIssue>>(issuesJson);
        if (issues is null)
        {
            return scenarios;
        }

        foreach (var issue in issues)
        {
            if (ShouldSkipIssue(issue, filter))
            {
                continue;
            }

            try
            {
                var scenario = ConvertIssueToScenario(issue);
                if (scenario is not null)
                {
                    scenarios.Add(scenario);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to convert issue #{Number} to scenario", issue.Number);
            }
        }

        _logger.LogInformation("Converted {Count} GitHub issues into eval scenarios", scenarios.Count);
        return scenarios;
    }

    private static bool ShouldSkipIssue(GitHubIssue issue, ScenarioFilter? filter)
    {
        // Skip feature requests unless explicitly requested
        if (issue.Labels.Any(l => l.Name.Equals("enhancement", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // Skip issues without trace reports (less actionable for eval)
        if (!TraceReportRegex.IsMatch(issue.Body ?? string.Empty))
        {
            return true;
        }

        // Apply error filter
        if (filter?.ErrorsOnly == true)
        {
            var hasErrorLabel = issue.Labels.Any(l => l.Name.Equals("bug", StringComparison.OrdinalIgnoreCase));
            if (!hasErrorLabel)
            {
                return true;
            }
        }

        return false;
    }

    private EvalScenario? ConvertIssueToScenario(GitHubIssue issue)
    {
        var body = issue.Body ?? string.Empty;

        // Extract user input from trace report
        var userInputMatch = UserInputRegex.Match(body);
        if (!userInputMatch.Success)
        {
            _logger.LogDebug("Issue #{Number} has no user input in trace report", issue.Number);
            return null;
        }

        var userPrompt = CleanUserInput(userInputMatch.Groups[1].Value);

        // Extract expected behavior
        var expectedBehaviorMatch = ExpectedBehaviorRegex.Match(body);
        var expectedBehavior = expectedBehaviorMatch.Success
            ? expectedBehaviorMatch.Groups[1].Value.Trim()
            : "Should not error";

        // Extract agent from trace report
        var agentMatch = SelectedAgentRegex.Match(body);
        if (!agentMatch.Success)
        {
            agentMatch = AgentExecutionsRegex.Match(body);
        }

        var expectedAgent = agentMatch.Success
            ? ParseAgentFromMatch(agentMatch.Groups[1].Value)
            : null;

        // Determine category based on issue content
        var category = DetermineCategory(body, issue.Title);

        var scenario = new EvalScenario
        {
            Id = $"github_issue_{issue.Number}",
            Description = $"Regression test from GitHub issue #{issue.Number}: {issue.Title}",
            Category = category,
            UserPrompt = userPrompt,
            ExpectedAgent = expectedAgent,
            Source = $"github-issue-{issue.Number}",
            Criteria = [expectedBehavior],
            Metadata = new Dictionary<string, string>
            {
                ["github_issue_number"] = issue.Number.ToString(),
                ["github_issue_title"] = issue.Title,
                ["difficulty"] = "production"
            }
        };

        // For error scenarios, we expect the error NOT to occur
        if (issue.Labels.Any(l => l.Name.Equals("bug", StringComparison.OrdinalIgnoreCase)))
        {
            scenario.ResponseMustNotContain.Add("encountered an issue");
            scenario.ResponseMustNotContain.Add("error");
            scenario.ResponseMustNotContain.Add("failed");
            scenario.Metadata["is_regression"] = "true";
        }

        return scenario;
    }

    private static string CleanUserInput(string rawInput)
    {
        // Remove HOME ASSISTANT CONTEXT and other preamble
        var lines = rawInput.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        // Find the actual user request (often after REQUEST_CONTEXT or marked with "User:")
        foreach (var line in lines.Reverse())
        {
            if (line.StartsWith("User:", StringComparison.OrdinalIgnoreCase))
            {
                return line.Substring(5).Trim();
            }

            // Also check for speaker tags like [Speaker: Unknown1]
            if (!line.Contains("CONTEXT") &&
                !line.Contains("timestamp") &&
                !line.Contains("device") &&
                !line.Contains("{") &&
                !line.Contains("}"))
            {
                return line;
            }
        }

        return rawInput.Trim();
    }

    private static string ParseAgentFromMatch(string agentText)
    {
        // Handle formats like "orchestration, music-agent" or "music-agent"
        var agents = agentText.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        // Return the last agent (typically the one that actually executed)
        // Skip "orchestration" as it's always present
        return agents.LastOrDefault(a => !a.Equals("orchestration", StringComparison.OrdinalIgnoreCase)) ?? "unknown";
    }

    private static string DetermineCategory(string body, string title)
    {
        if (body.Contains("Command Trace Report", StringComparison.OrdinalIgnoreCase))
        {
            return "command-execution";
        }

        if (body.Contains("Conversation Trace Report", StringComparison.OrdinalIgnoreCase))
        {
            return "conversation";
        }

        if (title.Contains("routing", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("agent selection", StringComparison.OrdinalIgnoreCase))
        {
            return "routing";
        }

        return "regression";
    }

    private async Task<string> ExecuteGhCommandAsync(string arguments, CancellationToken ct)
    {
        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "gh",
                Arguments = arguments,
                WorkingDirectory = _repositoryPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();

        var output = await process.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
        var error = await process.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);

        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            _logger.LogError("gh command failed: {Error}", error);
            throw new InvalidOperationException($"GitHub CLI command failed: {error}");
        }

        return output;
    }

    private sealed class GitHubIssue
    {
        public int Number { get; set; }
        public required string Title { get; set; }
        public string? Body { get; set; }
        public List<GitHubLabel> Labels { get; set; } = [];
    }

    private sealed class GitHubLabel
    {
        public required string Name { get; set; }
    }
}
