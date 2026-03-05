namespace lucia.AgentHost.Models;

/// <summary>
/// Request body for assigning a model to an existing provider.
/// </summary>
public sealed record SetProviderModelRequest(string ModelName);
