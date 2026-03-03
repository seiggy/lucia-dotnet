namespace lucia.AgentHost.Models;

/// <summary>
/// Request body for the Copilot CLI connect endpoint.
/// </summary>
public sealed record CopilotConnectRequest(string? GithubToken);