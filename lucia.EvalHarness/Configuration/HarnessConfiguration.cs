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
    public JudgeProvider JudgeProvider { get; set; } = JudgeProvider.AzureOpenAI;
    public CopilotJudgeSettings GitHubCopilot { get; set; } = new();

    public string JudgeModelName => JudgeProvider == JudgeProvider.GitHubCopilot
        ? GitHubCopilot.Model
        : AzureOpenAI.JudgeDeployment;

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
    /// Per-call deadline (seconds) for a single model-under-test / agent LLM invocation.
    /// Must be positive; a non-positive value is a configuration error, not a silent opt-out.
    /// </summary>
    public int AgentTimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// Per-call deadline (seconds) for a single judge / LLM-metric invocation.
    /// Must be positive; a non-positive value is a configuration error, not a silent opt-out.
    /// </summary>
    public int JudgeTimeoutSeconds { get; set; } = 120;

    /// <summary>Resolved per-call deadline for agent / model-under-test LLM calls.</summary>
    public TimeSpan AgentTimeout => TimeSpan.FromSeconds(AgentTimeoutSeconds);

    /// <summary>Resolved per-call deadline for judge / LLM-metric calls.</summary>
    public TimeSpan JudgeTimeout => TimeSpan.FromSeconds(JudgeTimeoutSeconds);

    /// <summary>
    /// Validates configuration invariants. Throws when a required positive value is
    /// zero or negative so misconfiguration fails fast instead of silently disabling deadlines.
    /// </summary>
    public void Validate()
    {
        if (!Enum.IsDefined(JudgeProvider))
        {
            throw new InvalidOperationException("Harness:JudgeProvider is not supported.");
        }

        var backends = GetEffectiveBackends();
        if (backends.Any(backend => string.IsNullOrWhiteSpace(backend.Name)) ||
            backends.Select(backend => backend.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != backends.Count)
        {
            throw new InvalidOperationException("Harness:Backends must have unique, non-empty names.");
        }

        foreach (var backend in backends)
        {
            if (!Enum.IsDefined(backend.Type) ||
                !Uri.TryCreate(backend.Endpoint, UriKind.Absolute, out var endpoint) ||
                endpoint.Scheme is not ("http" or "https") ||
                !string.IsNullOrEmpty(endpoint.UserInfo) ||
                !string.IsNullOrEmpty(endpoint.Query) ||
                !string.IsNullOrEmpty(endpoint.Fragment))
            {
                throw new InvalidOperationException($"Backend '{backend.Name}' requires a supported type and an HTTP(S) endpoint without credentials, query, or fragment.");
            }
        }

        if (AgentTimeoutSeconds <= 0)
            throw new InvalidOperationException(
                $"Harness:AgentTimeoutSeconds must be positive, but was {AgentTimeoutSeconds}.");

        if (JudgeTimeoutSeconds <= 0)
            throw new InvalidOperationException(
                $"Harness:JudgeTimeoutSeconds must be positive, but was {JudgeTimeoutSeconds}.");
    }

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
