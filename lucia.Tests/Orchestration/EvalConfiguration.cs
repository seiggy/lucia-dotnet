namespace lucia.Tests.Orchestration;

/// <summary>
/// Configuration for agent evaluation tests. Bound from the <c>EvalConfiguration</c>
/// section in <c>appsettings.json</c>. Environment variables override JSON values
/// using the standard ASP.NET Core double-underscore convention
/// (e.g., <c>EvalConfiguration__AzureOpenAI__Endpoint</c>).
/// </summary>
public sealed class EvalConfiguration
{
    /// <summary>
    /// Azure OpenAI / AI Foundry connection settings.
    /// </summary>
    public AzureOpenAISettings AzureOpenAI { get; set; } = new();

    /// <summary>
    /// Model deployments to evaluate. Each test is parameterized across all models.
    /// </summary>
    public List<EvalModelConfig> Models { get; set; } = [];

    /// <summary>
    /// Deployment name for the LLM-as-judge evaluator. Defaults to <c>gpt-4o</c>.
    /// </summary>
    public string JudgeModel { get; set; } = "gpt-4o";

    /// <summary>
    /// Directory path for <c>DiskBasedReportingConfiguration</c> storage.
    /// Defaults to <c>%TEMP%/lucia-eval-reports</c> when null.
    /// </summary>
    public string? ReportPath { get; set; }

    /// <summary>
    /// Execution name for report grouping. Defaults to a timestamp when null.
    /// </summary>
    public string? ExecutionName { get; set; }
}
