namespace lucia.EvalHarness.Configuration;

/// <summary>
/// Root configuration for the evaluation harness.
/// Bound from the <c>Harness</c> section in <c>appsettings.json</c>.
/// Environment variable overrides use <c>Harness__Ollama__Endpoint</c> convention.
/// </summary>
public sealed class HarnessConfiguration
{
    public OllamaSettings Ollama { get; set; } = new();
    public AzureOpenAIJudgeSettings AzureOpenAI { get; set; } = new();

    /// <summary>
    /// Named inference backends for multi-backend comparison.
    /// When empty, a single backend is synthesized from <see cref="Ollama"/> settings.
    /// </summary>
    public List<InferenceBackend> Backends { get; set; } = [];

    /// <summary>
    /// Directory path for evaluation report output.
    /// Defaults to <c>%TEMP%/lucia-eval-reports</c> when empty.
    /// </summary>
    public string? ReportPath { get; set; }

    /// <summary>
    /// Manual GPU label for non-NVIDIA setups (e.g., "Apple M3 Max 128GB").
    /// When empty, GPU is auto-detected via nvidia-smi.
    /// </summary>
    public string? GpuLabel { get; set; }

    /// <summary>
    /// Named parameter profiles for model inference tuning.
    /// Built-in profiles ("default", "precise", "creative") are always available.
    /// Custom profiles defined here are merged with built-ins.
    /// </summary>
    public Dictionary<string, ModelParameterProfile> ParameterProfiles { get; set; } = [];

    /// <summary>
    /// Returns the resolved list of backends. If <see cref="Backends"/> is empty,
    /// falls back to a single Ollama backend from <see cref="Ollama"/> settings.
    /// </summary>
    public IReadOnlyList<InferenceBackend> GetEffectiveBackends()
    {
        if (Backends is { Count: > 0 })
            return Backends;

        return
        [
            new InferenceBackend
            {
                Name = "Ollama",
                Endpoint = Ollama.Endpoint,
                Type = InferenceBackendType.Ollama
            }
        ];
    }

    /// <summary>
    /// Returns all available parameter profiles (built-in + custom).
    /// Custom profiles override built-ins with the same name.
    /// </summary>
    public IReadOnlyDictionary<string, ModelParameterProfile> GetAllProfiles()
    {
        var merged = new Dictionary<string, ModelParameterProfile>(
            ModelParameterProfile.BuiltInProfiles, StringComparer.OrdinalIgnoreCase);

        foreach (var (name, profile) in ParameterProfiles)
        {
            merged[name] = profile;
        }

        return merged;
    }
}
