#pragma warning disable AIEVAL001 // Microsoft.Extensions.AI.Evaluation is experimental

using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace lucia.Tests.Orchestration;

/// <summary>
/// Custom tool-call evaluator for smart-home agents. Replaces the built-in
/// <c>ToolCallAccuracyEvaluator</c> with a prompt that understands state-aware
/// optimizations — e.g. an agent that skips redundant tool calls because the
/// device state is already known from earlier tool results.
/// Returns a 1–5 numeric score; ≥ 4 is considered passing.
/// </summary>
public sealed class SmartHomeToolCallEvaluator : IEvaluator
{
    public const string MetricName = "Smart Home Tool Call Accuracy";
    private const double PassingThreshold = 4.0;

    public IReadOnlyCollection<string> EvaluationMetricNames { get; } = [MetricName];

    private const string SystemPrompt = """
        You are an expert evaluator for smart-home AI agents. Your job is to score how
        well the agent used its tools to satisfy the user's request.

        SCORING RUBRIC (1–5):
        5 — All tool calls are correct, relevant, and efficient. The agent fulfilled the
            request with optimal tool usage.
        4 — All tool calls are correct and relevant. Minor inefficiencies (e.g. an extra
            state check) are acceptable but the request was fulfilled correctly.
        3 — The request was partially fulfilled, or some tool calls had minor parameter
            issues, but the overall intent was addressed.
        2 — Significant issues: wrong parameters, missing critical tool calls, or the
            final response does not match the user's request.
        1 — Completely wrong: irrelevant tool calls, no tool calls at all, or the agent
            failed to address the request.

        IMPORTANT RULES:
        1. A tool call is RELEVANT if it relates to the user's request and its parameters
           are correctly extracted from the conversation.
        2. An agent that SKIPS a tool call because the requested state by the user is already
            the state of the system. (e.g. user asked to turn on a light that is already on,
            or the agent cannot find the device a user asked the agent to control) 
            is behaving EFFICIENTLY — do not penalize.
        3. Calling a tool with WRONG parameters or calling a completely IRRELEVANT tool
           (unrelated domain) should lower the score.
        4. Checking device state (e.g. GetLightState) before or after changing it (e.g.
           SetLightState) is a NORMAL and ACCEPTABLE pattern. Do NOT penalize this.
        5. Finding a device (e.g. FindLight) before operating on it is EXPECTED behavior.
        6. The agent MUST call at least one tool to fulfill the request. There is no
           pre-existing state available by default — the agent must use tools to discover
           and act on devices. If the agent made NO tool calls at all, score 1.

        Respond with a JSON object containing:
        - "score": integer from 1 to 5
        - "reason": a brief explanation of your judgment
        """;

    public async ValueTask<EvaluationResult> EvaluateAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        ChatConfiguration? chatConfiguration,
        IEnumerable<EvaluationContext>? additionalContext,
        CancellationToken cancellationToken = default)
    {
        if (chatConfiguration?.ChatClient is null)
        {
            var empty = new NumericMetric(MetricName, value: null, reason: "No judge ChatClient configured.");
            return new EvaluationResult(empty);
        }

        var toolDefs = additionalContext?
            .OfType<SmartHomeToolCallEvaluatorContext>()
            .FirstOrDefault()?.ToolDefinitions;

        var evalPrompt = BuildEvalPrompt(messages, modelResponse, toolDefs);

        var judgeMessages = new List<ChatMessage>
        {
            new(ChatRole.System, SystemPrompt),
            new(ChatRole.User, evalPrompt)
        };

        var judgeResponse = await chatConfiguration.ChatClient.GetResponseAsync(
            judgeMessages,
            cancellationToken: cancellationToken);

        var responseText = judgeResponse.Text ?? "";

        double score;
        string reason;
        try
        {
            var jsonStart = responseText.IndexOf('{');
            var jsonEnd = responseText.LastIndexOf('}');
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var json = responseText[jsonStart..(jsonEnd + 1)];
                using var doc = JsonDocument.Parse(json);
                score = doc.RootElement.GetProperty("score").GetDouble();
                reason = doc.RootElement.TryGetProperty("reason", out var reasonProp)
                    ? reasonProp.GetString() ?? "No reason provided."
                    : "No reason provided.";
            }
            else
            {
                score = 1;
                reason = $"Could not parse judge response: {responseText}";
            }
        }
        catch (Exception ex)
        {
            score = 1;
            reason = $"Judge response parse error: {ex.Message}. Response: {responseText}";
        }

        bool passed = score >= PassingThreshold;
        var rating = score switch
        {
            >= 5 => EvaluationRating.Exceptional,
            >= 4 => EvaluationRating.Good,
            >= 3 => EvaluationRating.Average,
            >= 2 => EvaluationRating.Poor,
            _ => EvaluationRating.Unacceptable
        };

        var metric = new NumericMetric(MetricName, score, reason);
        metric.Interpretation = new EvaluationMetricInterpretation(rating, failed: !passed, reason);

        return new EvaluationResult(metric);
    }

    private static string BuildEvalPrompt(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        IReadOnlyList<AITool>? toolDefinitions)
    {
        var sb = new StringBuilder();

        if (toolDefinitions is { Count: > 0 })
        {
            sb.AppendLine("## Available Tools");
            foreach (var tool in toolDefinitions)
            {
                if (tool is AIFunction func)
                {
                    sb.AppendLine($"- **{func.Name}**: {func.Description}");
                }
            }

            sb.AppendLine();
        }

        sb.AppendLine("## Conversation");
        foreach (var msg in messages)
        {
            sb.AppendLine($"[{msg.Role}]: {msg.Text}");
        }

        sb.AppendLine();
        sb.AppendLine("## Agent Response (including tool calls)");
        foreach (var msg in modelResponse.Messages)
        {
            if (msg.Contents is not null)
            {
                foreach (var content in msg.Contents)
                {
                    switch (content)
                    {
                        case TextContent text:
                            sb.AppendLine($"[{msg.Role}] Text: {text.Text}");
                            break;
                        case FunctionCallContent fc:
                            sb.AppendLine($"[{msg.Role}] Tool Call: {fc.Name}({JsonSerializer.Serialize(fc.Arguments)})");
                            break;
                        case FunctionResultContent fr:
                            sb.AppendLine($"[{msg.Role}] Tool Result [{fr.CallId}]: {fr.Result}");
                            break;
                    }
                }
            }
        }

        sb.AppendLine();
        sb.AppendLine("Score the agent's tool usage from 1-5. Respond with JSON.");
        return sb.ToString();
    }
}
