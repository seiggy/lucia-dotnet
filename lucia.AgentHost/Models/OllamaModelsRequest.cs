namespace lucia.AgentHost.Models;

/// <summary>
/// Request body for listing Ollama models.
/// </summary>
public sealed record OllamaModelsRequest(string? Endpoint);