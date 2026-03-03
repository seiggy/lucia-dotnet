namespace lucia.Agents.Models;

/// <summary>
/// Result of a model provider connection test.
/// </summary>
public sealed record ModelProviderConnectionTestResult(bool Success, string Message);
