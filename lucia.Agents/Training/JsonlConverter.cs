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
    /// </summary>
    public static string ConvertTraceToJsonl(ConversationTrace trace, bool includeCorrections)
    {
        var messages = new List<Dictionary<string, string>>();

        // Add system messages from agent executions
        foreach (var execution in trace.AgentExecutions)
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

        // Determine assistant response content
        var assistantContent = includeCorrections && !string.IsNullOrEmpty(trace.Label.CorrectionText)
            ? trace.Label.CorrectionText
            : trace.FinalResponse ?? string.Empty;

        messages.Add(new Dictionary<string, string>
        {
            ["role"] = "assistant",
            ["content"] = assistantContent
        });

        var jsonlObject = new Dictionary<string, object>
        {
            ["messages"] = messages
        };

        return JsonSerializer.Serialize(jsonlObject, JsonlOptions);
    }
}
