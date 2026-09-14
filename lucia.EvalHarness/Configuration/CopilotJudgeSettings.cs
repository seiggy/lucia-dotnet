using GitHub.Copilot;

namespace lucia.EvalHarness.Configuration;

public sealed class CopilotJudgeSettings
{
    public string Model { get; set; } = string.Empty;
    public string ContextTier { get; set; } = "default";
    public string? ReasoningEffort { get; set; }

    public void ValidateForModel(ModelInfo model)
    {
        if (string.IsNullOrWhiteSpace(Model) || Model != model.Id)
        {
            throw new InvalidOperationException("Select an available Copilot judge model.");
        }

        if (model.Policy?.State is { } state && state != "enabled")
        {
            throw new InvalidOperationException($"Copilot model '{Model}' is disabled by policy.");
        }

        if (ContextTier is not ("default" or "long_context") ||
            ContextTier == "long_context" && model.Billing?.TokenPrices?.LongContext is null)
        {
            throw new InvalidOperationException($"Copilot model '{Model}' does not support context tier '{ContextTier}'.");
        }

        if (ReasoningEffort is not null &&
            (model.Capabilities.Supports.ReasoningEffort != true ||
             model.SupportedReasoningEfforts?.Contains(ReasoningEffort) != true))
        {
            throw new InvalidOperationException($"Copilot model '{Model}' does not support effort '{ReasoningEffort}'.");
        }
    }
}
