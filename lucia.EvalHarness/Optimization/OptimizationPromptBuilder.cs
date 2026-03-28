using System.Text;
using lucia.EvalHarness.Evaluation;

namespace lucia.EvalHarness.Optimization;

/// <summary>
/// Builds the meta-prompt sent to GPT-5.4 for system prompt optimization.
/// Assembles failure analysis, score data, and the current system prompt
/// into a structured prompt that guides the LLM to suggest improvements.
/// </summary>
public static class OptimizationPromptBuilder
{
    /// <summary>
    /// Builds the optimization meta-prompt for a specific agent/model combination.
    /// </summary>
    public static string Build(
        string agentName,
        string targetModel,
        string currentSystemPrompt,
        IReadOnlyList<ModelEvalResult> targetResults,
        IReadOnlyList<ModelEvalResult>? baselineResults)
    {
        var sb = new StringBuilder();

        sb.AppendLine("""
            You are an expert prompt engineer specializing in optimizing system prompts for small language models (SLMs).
            Your task is to analyze evaluation results from an AI home assistant agent and suggest specific prompt improvements
            that will help a small model perform closer to a larger baseline model.

            IMPORTANT: Small models have limited reasoning capacity. Your suggestions should focus on:
            - Explicit, unambiguous instructions (no implicit reasoning required)
            - In-context examples that show the exact tool call format expected
            - Structured output guidance that constrains the model's response space
            - Removing unnecessary complexity that confuses small models
            """);

        // Current system prompt
        sb.AppendLine("## Current System Prompt");
        sb.AppendLine("```");
        sb.AppendLine(currentSystemPrompt);
        sb.AppendLine("```");
        sb.AppendLine();

        // Target model results
        var targetResult = targetResults.FirstOrDefault(r => r.AgentName == agentName);
        if (targetResult is not null)
        {
            sb.AppendLine($"## Target Model: {targetModel} on {agentName}");
            sb.AppendLine($"- Overall Score: {targetResult.OverallScore:F1}/100");
            sb.AppendLine($"- Tool Selection: {targetResult.ToolSelectionScore:F1}");
            sb.AppendLine($"- Tool Success: {targetResult.ToolSuccessScore:F1}");
            sb.AppendLine($"- Tool Efficiency: {targetResult.ToolEfficiencyScore:F1}");
            sb.AppendLine($"- Task Completion: {targetResult.TaskCompletionScore:F1}");
            sb.AppendLine($"- Pass Rate: {targetResult.PassedCount}/{targetResult.TestCaseCount}");
            sb.AppendLine();

            // Failed test cases with traces
            var failedCases = targetResult.TestCaseResults.Where(tc => !tc.Passed).ToList();
            if (failedCases.Count > 0)
            {
                sb.AppendLine("## Failed Test Cases (analyze these for patterns)");
                sb.AppendLine();

                foreach (var tc in failedCases)
                {
                    sb.AppendLine($"### {tc.TestCaseId} (score: {tc.Score:F0})");
                    sb.AppendLine($"**User Input:** {tc.Input}");
                    sb.AppendLine($"**Failure:** {tc.FailureReason}");

                    if (tc.ConversationHistory is { Count: > 0 })
                    {
                        sb.AppendLine("**Conversation trace:**");
                        foreach (var turn in tc.ConversationHistory)
                        {
                            if (turn.ToolCalls is { Count: > 0 })
                            {
                                foreach (var call in turn.ToolCalls)
                                {
                                    var args = call.Arguments is not null
                                        ? string.Join(", ", call.Arguments.Select(a => $"{a.Key}={a.Value}"))
                                        : "";
                                    sb.AppendLine($"  {turn.Role} → {call.Name}({args})");
                                }
                            }
                            else if (turn.Content is not null)
                            {
                                var truncated = turn.Content.Length > 200
                                    ? turn.Content[..200] + "…"
                                    : turn.Content;
                                sb.AppendLine($"  {turn.Role}: {truncated}");
                            }
                        }
                    }
                    sb.AppendLine();
                }
            }
        }

        // Baseline comparison
        var baselineResult = baselineResults?.FirstOrDefault(r => r.AgentName == agentName);
        if (baselineResult is not null)
        {
            sb.AppendLine($"## Baseline Model: {baselineResult.ModelName} (target to match)");
            sb.AppendLine($"- Overall Score: {baselineResult.OverallScore:F1}/100");
            sb.AppendLine($"- Pass Rate: {baselineResult.PassedCount}/{baselineResult.TestCaseCount}");
            sb.AppendLine();
        }

        // Instructions for the LLM
        sb.AppendLine("""
            ## Your Task

            Analyze the failure patterns above and provide:

            1. **Analysis**: A brief summary of why the small model is failing (common patterns)
            2. **Suggestions**: 3-5 specific, actionable prompt changes, each with:
               - `type`: one of "add_example", "clarify_instruction", "add_constraint", "restructure", "remove_ambiguity", "add_tool_guidance"
               - `location`: where in the prompt to apply the change
               - `content`: the exact text to add/change
               - `reasoning`: why this helps small models specifically
               - `predicted_impact`: which metric improves and by roughly how much
            3. **Revised Prompt**: The complete updated system prompt incorporating all suggestions

            Respond in this exact JSON format:
            ```json
            {
              "analysis": "string",
              "suggestions": [
                {
                  "type": "string",
                  "location": "string",
                  "content": "string",
                  "reasoning": "string",
                  "predicted_impact": "string"
                }
              ],
              "suggested_prompt": "string"
            }
            ```
            """);

        return sb.ToString();
    }
}
