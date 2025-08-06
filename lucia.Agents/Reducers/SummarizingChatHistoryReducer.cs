using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Reducers;

/// <summary>
/// Chat history reducer that summarizes older messages when approaching token limits
/// </summary>
public class SummarizingChatHistoryReducer : IChatHistoryReducer
{
    private readonly IChatCompletionService _chatCompletionService;
    private readonly ILogger<SummarizingChatHistoryReducer> _logger;
    private readonly int _maxTokens;
    private readonly double _reductionThreshold;
    private readonly int _messagesToKeep;

    public SummarizingChatHistoryReducer(
        IChatCompletionService chatCompletionService,
        ILogger<SummarizingChatHistoryReducer> logger,
        int maxTokens = 8000,
        double reductionThreshold = 0.8,
        int messagesToKeep = 4)
    {
        _chatCompletionService = chatCompletionService;
        _logger = logger;
        _maxTokens = maxTokens;
        _reductionThreshold = reductionThreshold;
        _messagesToKeep = messagesToKeep;
    }

    public async Task<IEnumerable<ChatMessageContent>?> ReduceAsync(IReadOnlyList<ChatMessageContent> chatHistory, CancellationToken cancellationToken = default)
    {
        // Calculate current token count using proper metadata access
        var currentTokens = GetChatHistoryTokens(chatHistory);
        var thresholdTokens = (int)(_maxTokens * _reductionThreshold);

        _logger.LogDebug("Current tokens: {CurrentTokens}, Threshold: {ThresholdTokens}", currentTokens, thresholdTokens);

        // Check if reduction is needed
        if (currentTokens <= thresholdTokens || chatHistory.Count <= _messagesToKeep)
        {
            return null; // No reduction needed
        }

        _logger.LogInformation("Chat history reduction needed. Current: {CurrentTokens} tokens, reducing...", currentTokens);

        try
        {
            // Separate messages to summarize vs. keep
            var messagesToSummarize = chatHistory.Take(chatHistory.Count - _messagesToKeep).ToList();
            var messagesToPreserve = chatHistory.Skip(chatHistory.Count - _messagesToKeep).ToList();

            // Create summary of older messages
            var summary = await CreateSummaryAsync(messagesToSummarize, cancellationToken);

            // Build the reduced history
            var reducedHistory = new List<ChatMessageContent>();

            // Add the summary as a system message
            var summaryMessage = new ChatMessageContent(
                AuthorRole.System,
                $"[Previous conversation summary: {summary}]");
            
            reducedHistory.Add(summaryMessage);

            // Add back the preserved messages
            reducedHistory.AddRange(messagesToPreserve);

            var newTokenCount = GetChatHistoryTokens(reducedHistory);
            _logger.LogInformation("Chat history reduced from {OriginalTokens} to {NewTokens} tokens ({MessageCount} messages)",
                currentTokens, newTokenCount, reducedHistory.Count);

            return reducedHistory;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reduce chat history");
            return null; // Return null to indicate no reduction occurred
        }
    }

    /// <summary>
    /// Helper method to get total tokens from entire chat history
    /// </summary>
    private static int GetChatHistoryTokens(IReadOnlyList<ChatMessageContent> chatHistory)
    {
        var tokens = 0;

        foreach (var message in chatHistory)
        {
            var messageTokens = GetTokensFromMetadata(message.Metadata);

            if (messageTokens.HasValue)
            {
                tokens += messageTokens.Value;
            }
        }

        return tokens;
    }

    /// <summary>
    /// Helper method to get tokens from message metadata
    /// </summary>
    private static int? GetTokensFromMetadata(IReadOnlyDictionary<string, object?>? metadata)
    {
        if (metadata is not null && metadata.TryGetValue("TokenCount", out var tokenCountObject))
        {
            return (int)(tokenCountObject ?? 0);
        }

        return null;
    }

    private async Task<string> CreateSummaryAsync(IReadOnlyList<ChatMessageContent> messages, CancellationToken cancellationToken)
    {
        if (!messages.Any())
            return "No previous conversation.";

        // Build the conversation text for summarization
        var conversationText = string.Join("\n", messages.Select(m =>
        {
            var authorName = m.AuthorName ?? m.Role.ToString();
            return $"{authorName}: {m.Content}";
        }));

        var summaryPrompt = $"""
            Please create a concise summary of the following conversation, preserving key context, 
            decisions made, and important information that might be needed for future interactions.
            Focus on maintaining continuity for ongoing home automation tasks and user preferences.

            Conversation:
            {conversationText}

            Summary:
            """;

        try
        {
            var summaryResponse = await _chatCompletionService.GetChatMessageContentAsync(
                summaryPrompt,
                new OpenAIPromptExecutionSettings 
                { 
                    MaxTokens = 300,
                    Temperature = 0.1 // Low temperature for consistent summaries
                },
                cancellationToken: cancellationToken);

            return summaryResponse.Content ?? "Unable to create summary.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create conversation summary");
            return "Previous conversation occurred but summary unavailable.";
        }
    }
}