namespace lucia.Agents.Services;

using A2A;
using lucia.Agents.Configuration;
using lucia.Agents.Registry;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

/// <summary>
/// Extracts contextual information from conversation history to populate metadata
/// for context-aware routing and agent coordination in multi-turn conversations.
/// Uses data-driven domain capabilities from AgentRegistry instead of hard-coded keywords.
/// Locations from RoomConfiguration, agent domains from hashtags in descriptions.
/// </summary>
public sealed class ContextExtractor
{
    private readonly IAgentRegistry _agentRegistry;
    private volatile IReadOnlyDictionary<string, List<string>>? _domainAgentMap;

    public ContextExtractor(IAgentRegistry agentRegistry)
    {
        _agentRegistry = agentRegistry ?? throw new ArgumentNullException(nameof(agentRegistry));
    }

    /// <summary>
    /// Lazy-loads and caches the domain-to-agent mapping from the agent registry.
    /// Agents declare domains in their descriptions using #hashtags.
    /// Uses Interlocked.CompareExchange for thread-safe initialization.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, List<string>>> GetDomainAgentMapAsync()
    {
        var current = _domainAgentMap;
        if (current is not null)
            return current;

        var allAgents = new List<AgentCard>();
        await foreach (var agent in _agentRegistry.GetEnumerableAgentsAsync())
        {
            allAgents.Add(agent);
        }

        var newMap = DomainCapabilitiesExtractor.BuildDomainAgentMap(allAgents);

        // Atomically set if still null — if another thread won the race, use theirs
        var winner = Interlocked.CompareExchange(ref _domainAgentMap, newMap, null);
        return winner ?? newMap;
    }

    /// <summary>
    /// Extracts contextual metadata from an AgentTask's message history.
    /// Populates metadata dictionary with: location, previousAgents, conversationTopic
    /// </summary>
    /// <param name="task">The task with message history to analyze</param>
    /// <returns>Dictionary of extracted metadata</returns>
    public async Task<Dictionary<string, JsonElement>> ExtractMetadataAsync(AgentTask task)
    {
        var metadata = new Dictionary<string, object>();

        if (task.History is null || task.History.Count == 0)
        {
            return ConvertToJsonElements(metadata);
        }

        var location = ExtractLocation(task.History);
        if (!string.IsNullOrEmpty(location))
        {
            metadata["location"] = location;
        }

        var previousAgents = await ExtractPreviousAgentsAsync(task.History);
        if (previousAgents.Count > 0)
        {
            metadata["previousAgents"] = previousAgents;
        }

        var domainAgentMap = await GetDomainAgentMapAsync();
        var topic = await ExtractConversationTopicAsync(task.History, domainAgentMap);
        if (!string.IsNullOrEmpty(topic))
        {
            metadata["conversationTopic"] = topic;
        }

        return ConvertToJsonElements(metadata);
    }

    /// <summary>
    /// Extracts the primary location mentioned in the conversation history.
    /// Uses RoomConfiguration for known room normalization.
    /// </summary>
    /// <param name="history">Message history to analyze</param>
    /// <returns>Extracted location or null</returns>
    private static string? ExtractLocation(List<AgentMessage> history)
    {
        var combinedText = CombineMessageText(history);
        
        // Try exact matches against known rooms
        foreach (var room in RoomConfiguration.KnownRooms)
        {
            if (combinedText.Contains(room, StringComparison.OrdinalIgnoreCase))
            {
                return room;
            }
        }

        // Try to extract location using regex pattern "the [adjective] [location]"
        var locationPattern = @"the\s+(\w+\s+)?(\w+(?:\s+room)?)";
        var match = Regex.Match(combinedText, locationPattern, RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var possibleLocation = match.Groups[2].Value;
            var normalized = RoomConfiguration.NormalizeRoom(possibleLocation);
            if (!string.IsNullOrEmpty(normalized))
            {
                return normalized;
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts text content from a Part, handling different part types.
    /// </summary>
    /// <param name="part">The part to extract text from</param>
    /// <returns>Text content or empty string</returns>
    private static string GetTextFromPart(Part part)
    {
        if (part is TextPart textPart)
        {
            return textPart.Text ?? string.Empty;
        }
        return string.Empty;
    }

    /// <summary>
    /// Extracts list of agent IDs that have participated in the conversation.
    /// Extracts from metadata when available, infers from content using domain agent map.
    /// </summary>
    /// <param name="history">Message history to analyze</param>
    /// <returns>List of unique agent identifiers</returns>
    private async Task<List<string>> ExtractPreviousAgentsAsync(List<AgentMessage> history)
    {
        var agents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var message in history)
        {
            // Extract agent ID from message metadata if present
            if (message.Metadata is not null && message.Metadata.TryGetValue("agentId", out var agentIdElement))
            {
                if (agentIdElement.ValueKind == JsonValueKind.String)
                {
                    var agentId = agentIdElement.GetString();
                    if (!string.IsNullOrEmpty(agentId))
                    {
                        agents.Add(agentId);
                    }
                }
            }

            // Infer agent from message parts using domain agent map
            if (message.Parts != null && message.Parts.Count > 0)
            {
                foreach (var part in message.Parts)
                {
                    var text = GetTextFromPart(part);
                    if (!string.IsNullOrEmpty(text))
                    {
                        var inferredAgents = await InferAgentsFromContentAsync(text);
                        foreach (var agent in inferredAgents)
                        {
                            agents.Add(agent);
                        }
                    }
                }
            }
        }

        return agents.ToList();
    }

    /// <summary>
    /// Infers potential agents from message content by extracting keywords and looking up in domain agent map.
    /// Uses lazy-loaded domain-to-agent mapping built from agent catalog capabilities.
    /// </summary>
    /// <param name="content">Message content to analyze</param>
    /// <returns>List of potential agent IDs</returns>
    private async Task<List<string>> InferAgentsFromContentAsync(string content)
    {
        var agents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var keywords = ExtractKeywords(content);
        var domainAgentMap = await GetDomainAgentMapAsync();

        // For each keyword, find matching domains and their agents
        foreach (var keyword in keywords)
        {
            foreach (var domain in domainAgentMap.Keys)
            {
                // Simple substring match for now, could be enhanced with fuzzy matching
                if (domain.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var agent in domainAgentMap[domain])
                    {
                        agents.Add(agent);
                    }
                }
            }
        }

        return agents.ToList();
    }

    /// <summary>
    /// Extracts keywords from content by splitting on whitespace and punctuation.
    /// Returns significant words (excluding common stop words).
    /// </summary>
    /// <param name="content">Text content to extract keywords from</param>
    /// <returns>List of extracted keywords</returns>
    private static List<string> ExtractKeywords(string content)
    {
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "and", "or", "but", "in", "on", "at", "to", "for", "of", "is", "are", "was", "be", "i", "you", "he", "she", "it", "we", "they"
        };

        var keywords = Regex.Split(content, @"[\s\p{P}]+")
            .Where(w => !string.IsNullOrWhiteSpace(w) && w.Length > 2 && !stopWords.Contains(w))
            .Select(w => w.ToLowerInvariant())
            .Distinct()
            .ToList();

        return keywords;
    }

    /// <summary>
    /// Extracts the primary conversation topic/domain from the message history.
    /// Analyzes keywords and matches against registered agent domains.
    /// </summary>
    /// <param name="history">Message history to analyze</param>
    /// <param name="domainAgentMap">Domain to agent mapping from the agent catalog</param>
    /// <returns>Extracted topic/domain or null</returns>
    private static Task<string?> ExtractConversationTopicAsync(List<AgentMessage> history, IReadOnlyDictionary<string, List<string>> domainAgentMap)
    {
        var combinedText = CombineMessageText(history);
        var keywords = ExtractKeywords(combinedText);
        var domain = DomainCapabilitiesExtractor.FindDomainForKeywords(keywords, domainAgentMap);
        return Task.FromResult(domain);
    }

    /// <summary>
    /// Combines all message parts into a single string for analysis.
    /// </summary>
    private static string CombineMessageText(List<AgentMessage> history)
    {
        var textParts = new List<string>();
        
        foreach (var message in history)
        {
            if (message.Parts != null && message.Parts.Count > 0)
            {
                foreach (var part in message.Parts)
                {
                    var text = GetTextFromPart(part);
                    if (!string.IsNullOrEmpty(text))
                    {
                        textParts.Add(text);
                    }
                }
            }
        }

        return string.Join(" ", textParts);
    }

    /// <summary>
    /// Counts occurrences of a keyword in text (case-insensitive, word boundary).
    /// </summary>
    private static int CountOccurrences(string text, string keyword)
    {
        var pattern = $@"\b{Regex.Escape(keyword)}\b";
        return Regex.Matches(text, pattern, RegexOptions.IgnoreCase).Count;
    }

    /// <summary>
    /// Converts object dictionary to JsonElement dictionary for AgentTask serialization.
    /// </summary>
    private static Dictionary<string, JsonElement> ConvertToJsonElements(Dictionary<string, object> metadata)
    {
        var result = new Dictionary<string, JsonElement>();

        foreach (var kvp in metadata)
        {
            JsonElement element = kvp.Value switch
            {
                // For strings, create a JSON string
                string strValue => CreateJsonStringElement(strValue),
                // For lists, create a JSON array
                List<string> listValue => CreateJsonArrayElement(listValue),
                // Default: convert via JSON parse
                _ => CreateJsonElementFromObject(kvp.Value)
            };

            result[kvp.Key] = element;
        }

        return result;
    }

    /// <summary>
    /// Creates a JsonElement from a string value.
    /// </summary>
    private static JsonElement CreateJsonStringElement(string value)
    {
        // Escape quotes and other JSON special characters
        var escaped = value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        using var doc = JsonDocument.Parse($"\"{escaped}\"");
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// Creates a JsonElement from a list of strings (JSON array).
    /// </summary>
    private static JsonElement CreateJsonArrayElement(List<string> values)
    {
        var jsonItems = values.Select(v =>
        {
            var escaped = v.Replace("\\", "\\\\").Replace("\"", "\\\"");
            return $"\"{escaped}\"";
        });
        var jsonArray = "[" + string.Join(",", jsonItems) + "]";
        using var doc = JsonDocument.Parse(jsonArray);
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// Creates a JsonElement from an object using JSON parse.
    /// </summary>
    private static JsonElement CreateJsonElementFromObject(object value)
    {
        // Fallback for other types - try to parse as JSON
        var jsonStr = value?.ToString() ?? "null";
        try
        {
            using var doc = JsonDocument.Parse(jsonStr);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            // If parsing fails, treat as string
            var escaped = jsonStr.Replace("\\", "\\\\").Replace("\"", "\\\"");
            using var doc = JsonDocument.Parse($"\"{escaped}\"");
            return doc.RootElement.Clone();
        }
    }
}
