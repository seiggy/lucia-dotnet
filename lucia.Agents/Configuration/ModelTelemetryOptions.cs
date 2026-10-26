namespace lucia.Agents.Configuration;

/// <summary>Controls model-content capture independently of operational telemetry.</summary>
public sealed class ModelTelemetryOptions
{
    /// <summary>
    /// Kept apart from <c>Observability</c>, whose mode and OTLP settings come from the
    /// environment, so <see cref="ConfigSeeder"/> can seed this section into the config store.
    /// </summary>
    public const string SectionName = "ModelTelemetry";

    /// <summary>
    /// Records model inputs and outputs in telemetry and enables SDK payload logging.
    /// On by default, matching earlier releases. The setup wizard offers the choice, and
    /// upgraded installs get this default seeded. Applies when clients are constructed.
    /// </summary>
    public bool IncludeModelContent { get; set; } = true;
}
