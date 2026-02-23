namespace lucia.Agents.GitHubCopilot.Models;

/// <summary>
/// Result of the Copilot CLI connect operation.
/// </summary>
public sealed record CopilotConnectResult(
    bool Success,
    string Message,
    List<CopilotModelInfo> Models);