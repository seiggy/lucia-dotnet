namespace lucia.Tests.Orchestration;

/// <summary>
/// Configuration for a single model deployment used in evaluation tests.
/// </summary>
public sealed class EvalModelConfig
{
    /// <summary>
    /// The Azure OpenAI deployment name (e.g., <c>gpt-4o</c>, <c>gpt-oss-120b</c>).
    /// </summary>
    public required string DeploymentName { get; set; }

    /// <summary>
    /// Optional temperature override for this model. When null, the model's default
    /// temperature is used. Set to <c>0</c> for deterministic outputs. Omit for
    /// reasoning models (o-series) that do not support custom temperature.
    /// </summary>
    public float? Temperature { get; set; }
}
