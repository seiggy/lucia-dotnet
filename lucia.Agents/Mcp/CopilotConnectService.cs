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
    /// Starts the bundled Copilot CLI and lists available models.
    /// The SDK ships its own CLI binary — no system-level installation required.
    /// </summary>
    /// <param name="githubToken">Optional GitHub token. If null, uses the logged-in user's credentials.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<CopilotConnectResult> ConnectAndListModelsAsync(string? githubToken, CancellationToken ct)
    {
        CopilotClient? client = null;
        try
        {
            var options = new CopilotClientOptions();

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

}
