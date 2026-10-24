namespace lucia.Agents.Configuration;

/// <summary>Controls model-content capture independently of operational telemetry.</summary>
public sealed class ModelTelemetryOptions
{
    public const string SectionName = "Observability";

    /// <summary>
    /// Explicit opt-in to model input/output telemetry and SDK payload logging.
    /// Defaults to false in every environment. Applies when clients are constructed.
    /// </summary>
    public bool IncludeModelContent { get; set; }
}
