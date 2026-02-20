using System.Text.Json;
using lucia.Agents.Training.Models;

namespace lucia.Agents.Training;

/// <summary>
/// Utility for converting conversation traces to OpenAI fine-tuning JSONL format.
/// </summary>
public static class JsonlConverter
{
    private static readonly JsonSerializerOptions JsonlOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Converts a conversation trace to an OpenAI fine-tuning JSONL line.
    /// When <paramref name="agentFilter"/> is specified, only messages from that agent's execution are included.
    /// </summary>
    public static string ConvertTraceToJsonl(ConversationTrace trace, bool includeCorrections, string? agentFilter = null)
    {
        var executions = GetFilteredExecutions(trace, agentFilter);
        var messages = new List<Dictionary<string, string>>();

        // Add system messages from agent executions
        foreach (var execution in executions)
        {
            foreach (var message in execution.Messages)
            {
                if (string.Equals(message.Role, "system", StringComparison.OrdinalIgnoreCase))
                {
                    messages.Add(new Dictionary<string, string>
                    {
                        ["role"] = "system",
                        ["content"] = message.Content ?? string.Empty
                    });
                }
            }
        }

        // Add user input
        messages.Add(new Dictionary<string, string>
        {
            ["role"] = "user",
            ["content"] = trace.UserInput
        });

        // Build assistant response: when filtering to a single agent, use that agent's
        // last assistant message rather than the orchestrator's final rollup.
        string assistantContent;
        if (includeCorrections && !string.IsNullOrEmpty(trace.Label.CorrectionText))
        {
            assistantContent = trace.Label.CorrectionText;
        }
        else if (agentFilter is not null && executions.Count > 0)
        {
            assistantContent = ExtractAgentAssistantResponse(executions) ?? trace.FinalResponse ?? string.Empty;
        }
        else
        {
            assistantContent = trace.FinalResponse ?? string.Empty;
        }

        messages.Add(new Dictionary<string, string>
        {
            ["role"] = "assistant",
            ["content"] = assistantContent
        });

        // Include tool calls when exporting a specific agent
        if (agentFilter is not null)
        {
            foreach (var execution in executions)
            {
                if (execution.ToolCalls is { Count: > 0 })
                {
                    foreach (var toolCall in execution.ToolCalls)
                    {
                        messages.Add(new Dictionary<string, string>
                        {
                            ["role"] = "tool",
                            ["content"] = $"{toolCall.ToolName}: {toolCall.Result ?? string.Empty}"
                        });
                    }
                }
            }
        }

        var jsonlObject = new Dictionary<string, object>
        {
            ["messages"] = messages
        };

        return JsonSerializer.Serialize(jsonlObject, JsonlOptions);
    }

    private static List<AgentExecutionRecord> GetFilteredExecutions(ConversationTrace trace, string? agentFilter)
    {
        if (string.IsNullOrWhiteSpace(agentFilter))
        {
            return trace.AgentExecutions;
        }

        return trace.AgentExecutions
            .Where(e => string.Equals(e.AgentId, agentFilter, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static string? ExtractAgentAssistantResponse(List<AgentExecutionRecord> executions)
    {
        // Take the last assistant message from the agent's execution messages
        foreach (var execution in executions)
        {
            for (var i = execution.Messages.Count - 1; i >= 0; i--)
            {
                var msg = execution.Messages[i];
                if (string.Equals(msg.Role, "assistant", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(msg.Content))
                {
                    return msg.Content;
                }
            }
        }

        return null;
    }
}
