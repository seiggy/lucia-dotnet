using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Mcp;

/// <summary>
/// DTO returned from the Copilot CLI connect + list-models flow.
/// </summary>
public sealed record CopilotModelInfo(
    string Id,
    string Name,
    bool SupportsVision,
    bool SupportsReasoningEffort,
    double? MaxPromptTokens,
    double? MaxOutputTokens,
    double MaxContextWindowTokens,
    string? PolicyState,
    string? PolicyTerms,
    double BillingMultiplier,
    List<string> SupportedReasoningEfforts,
    string? DefaultReasoningEffort);

/// <summary>
/// Result of the Copilot CLI connect operation.
/// </summary>
public sealed record CopilotConnectResult(
    bool Success,
    string Message,
    List<CopilotModelInfo> Models);

/// <summary>
/// Validates the GitHub Copilot CLI is available and retrieves the list of models
/// the authenticated user has access to.
/// </summary>
public sealed class CopilotConnectService
{
    private readonly ILogger<CopilotConnectService> _logger;

    public CopilotConnectService(ILogger<CopilotConnectService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Checks for the copilot CLI binary and lists available models.
    /// </summary>
    /// <param name="githubToken">Optional GitHub token. If null, uses the logged-in user's credentials.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<CopilotConnectResult> ConnectAndListModelsAsync(string? githubToken, CancellationToken ct)
    {
        // First, check if the copilot CLI is in PATH
        if (!await IsCopilotCliAvailableAsync(ct))
        {
            return new CopilotConnectResult(
                false,
                "GitHub Copilot CLI ('copilot') not found in PATH. " +
                "Install it from: https://docs.github.com/en/copilot/managing-copilot/configure-personal-settings/installing-the-github-copilot-extension-for-your-cli",
                []);
        }

        CopilotClient? client = null;
        try
        {
            var options = new CopilotClientOptions
            {
                AutoStart = false,
            };

            if (!string.IsNullOrWhiteSpace(githubToken))
            {
                options.GithubToken = githubToken;
            }

            client = new CopilotClient(options);
            await client.StartAsync(ct);

            _logger.LogInformation("Copilot CLI started, listing models...");

            var models = await client.ListModelsAsync(ct);

            var result = models.Select(m => new CopilotModelInfo(
                Id: m.Id,
                Name: m.Name,
                SupportsVision: m.Capabilities?.Supports?.Vision ?? false,
                SupportsReasoningEffort: m.Capabilities?.Supports?.ReasoningEffort ?? false,
                MaxPromptTokens: m.Capabilities?.Limits?.MaxPromptTokens,
                MaxOutputTokens: null, // ModelLimits doesn't expose MaxOutputTokens
                MaxContextWindowTokens: m.Capabilities?.Limits?.MaxContextWindowTokens ?? 0,
                PolicyState: m.Policy?.State,
                PolicyTerms: m.Policy?.Terms,
                BillingMultiplier: m.Billing?.Multiplier ?? 1.0,
                SupportedReasoningEfforts: m.SupportedReasoningEfforts ?? [],
                DefaultReasoningEffort: m.DefaultReasoningEffort
            )).ToList();

            _logger.LogInformation("Copilot CLI returned {ModelCount} models", result.Count);
            return new CopilotConnectResult(true, $"Connected. Found {result.Count} available models.", result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to GitHub Copilot CLI");
            return new CopilotConnectResult(false, $"Failed to connect: {ex.Message}", []);
        }
        finally
        {
            if (client is not null)
            {
                try { await client.StopAsync(); }
                catch { /* best-effort cleanup */ }
            }
        }
    }

    private static async Task<bool> IsCopilotCliAvailableAsync(CancellationToken ct)
    {
        try
        {
            using var process = new System.Diagnostics.Process();
            process.StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "copilot",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            process.Start();
            await process.WaitForExitAsync(ct);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
