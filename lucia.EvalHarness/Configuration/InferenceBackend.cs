namespace lucia.EvalHarness.Configuration;

/// <summary>
/// Describes an inference backend (e.g., Ollama instance, llama.cpp server)
/// that the eval harness can target. Multiple backends enable side-by-side
/// latency and quality comparison of the same model across different runtimes.
/// </summary>
public sealed class InferenceBackend
{
    /// <summary>
    /// Human-readable label shown in reports (e.g., "Ollama", "llama.cpp").
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Base URL of the inference server (e.g., "http://localhost:11434").
    /// </summary>
    public required string Endpoint { get; set; }

    /// <summary>
    /// API protocol the server speaks. Defaults to <see cref="InferenceBackendType.Ollama"/>.
    /// </summary>
    public InferenceBackendType Type { get; set; } = InferenceBackendType.Ollama;
}
