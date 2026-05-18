using System.Linq;

using lucia.Agents.Abstractions;

namespace lucia.Agents.Services;

/// <summary>
/// Stores and retrieves recent per-user conversation turns from <see cref="IMemoryStore"/>.
/// </summary>
public sealed class ChatHistoryProvider
{
    /// <summary>
    /// Prefix used for stored chat history keys.
    /// </summary>
    public const string ChatHistoryKeyPrefix = "chat_history:";

    private readonly IMemoryStore _memoryStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatHistoryProvider"/> class.
    /// </summary>
    public ChatHistoryProvider(IMemoryStore memoryStore)
    {
        _memoryStore = memoryStore;
    }

    /// <summary>
    /// Appends a conversation turn for a user.
    /// </summary>
    public async Task AppendTurnAsync(string userId, string userMessage, string assistantResponse, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return;
        }

        var key = $"{ChatHistoryKeyPrefix}{DateTime.UtcNow.Ticks:D19}:{Guid.NewGuid():N}";
        var value = $"User: {Sanitize(userMessage)}{Environment.NewLine}Assistant: {Sanitize(assistantResponse)}";

        await _memoryStore.StoreAsync(userId, key, value, ct: ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets recent conversation turns for a user in chronological order.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetRecentHistoryAsync(string userId, int maxTurns = 10, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || maxTurns <= 0)
        {
            return [];
        }

        var memories = await _memoryStore.GetAllAsync(userId, ct).ConfigureAwait(false);
        return memories
            .Where(entry => entry.Key.StartsWith(ChatHistoryKeyPrefix, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(entry => entry.CreatedAt)
            .Take(maxTurns)
            .OrderBy(entry => entry.CreatedAt)
            .Select(entry => entry.Value)
            .ToList();
    }

    private static string Sanitize(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.ReplaceLineEndings(" ").Trim();
    }
}
