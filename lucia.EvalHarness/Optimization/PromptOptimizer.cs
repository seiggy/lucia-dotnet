using System.Text.Json;
using lucia.Agents.Abstractions;
using lucia.EvalHarness.Evaluation;
using lucia.EvalHarness.Infrastructure;
using Microsoft.Extensions.AI;

namespace lucia.EvalHarness.Optimization;

/// <summary>
/// Sends evaluation results and the current system prompt to a judge LLM
/// (e.g., GPT-5.4 on Azure OpenAI) for automated prompt improvement suggestions.
/// </summary>
public sealed class PromptOptimizer
{
    private readonly IChatClient _judgeChatClient;
    private readonly TimeSpan _judgeTimeout;
    private readonly TimeProvider _timeProvider;

    public PromptOptimizer(IChatClient judgeChatClient, TimeSpan judgeTimeout, TimeProvider? timeProvider = null)
    {
        _judgeChatClient = judgeChatClient;
        _judgeTimeout = judgeTimeout;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Analyzes an agent's performance on a target model and suggests prompt improvements.
    /// </summary>
    public async Task<PromptOptimizationResult> OptimizeAsync(
        string agentName,
        string targetModel,
        string currentSystemPrompt,
        IReadOnlyList<ModelEvalResult> targetResults,
        IReadOnlyList<ModelEvalResult>? baselineResults = null,
        CancellationToken ct = default)
    {
        var targetResult = targetResults.FirstOrDefault(r => r.AgentName == agentName);
        var baselineResult = baselineResults?.FirstOrDefault(r => r.AgentName == agentName);

        var metaPrompt = OptimizationPromptBuilder.Build(
            agentName, targetModel, currentSystemPrompt, targetResults, baselineResults);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, metaPrompt)
        };

        var options = new ChatOptions
        {
            Temperature = 0.3f,
            ResponseFormat = ChatResponseFormat.Json
        };

        var response = await LlmDeadline.RunAsync(
            token => _judgeChatClient.GetResponseAsync(messages, options, token),
            _judgeTimeout, _timeProvider, ct,
            $"Prompt optimizer judge call exceeded the {_judgeTimeout.TotalSeconds:0}s deadline for agent '{agentName}' on model '{targetModel}'.");
        var responseText = response.Text ?? "";

        return ParseResponse(
            agentName, targetModel, currentSystemPrompt,
            targetResult?.OverallScore ?? 0,
            baselineResult?.OverallScore ?? 0,
            responseText);
    }

    private static PromptOptimizationResult ParseResponse(
        string agentName,
        string targetModel,
        string currentSystemPrompt,
        double currentScore,
        double baselineScore,
        string responseText)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseText);
            var root = doc.RootElement;

            var analysis = root.TryGetProperty("analysis", out var analysisProp)
                ? analysisProp.GetString()
                : null;

            var suggestedPrompt = root.TryGetProperty("suggested_prompt", out var promptProp)
                ? promptProp.GetString()
                : null;

            var suggestions = new List<PromptSuggestion>();
            if (root.TryGetProperty("suggestions", out var suggestionsArr))
            {
                foreach (var item in suggestionsArr.EnumerateArray())
                {
                    suggestions.Add(new PromptSuggestion
                    {
                        Type = item.TryGetProperty("type", out var t) ? t.GetString() ?? "unknown" : "unknown",
                        Location = item.TryGetProperty("location", out var l) ? l.GetString() ?? "" : "",
                        Content = item.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "",
                        Reasoning = item.TryGetProperty("reasoning", out var r) ? r.GetString() ?? "" : "",
                        PredictedImpact = item.TryGetProperty("predicted_impact", out var p) ? p.GetString() : null
                    });
                }
            }

            return new PromptOptimizationResult
            {
                AgentName = agentName,
                TargetModel = targetModel,
                OriginalPrompt = currentSystemPrompt,
                CurrentScore = currentScore,
                BaselineScore = baselineScore,
                Suggestions = suggestions,
                SuggestedPrompt = suggestedPrompt,
                Analysis = analysis
            };
        }
        catch (JsonException)
        {
            // If the LLM didn't return valid JSON, return the raw text as analysis
            return new PromptOptimizationResult
            {
                AgentName = agentName,
                TargetModel = targetModel,
                OriginalPrompt = currentSystemPrompt,
                CurrentScore = currentScore,
                BaselineScore = baselineScore,
                Suggestions = [],
                Analysis = responseText
            };
        }
    }
}
