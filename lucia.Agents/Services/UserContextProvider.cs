using System.Linq;
using System.Text;

using lucia.Agents.Abstractions;

namespace lucia.Agents.Services;

/// <summary>
/// Formats stored per-user memories for prompt injection.
/// </summary>
public sealed class UserContextProvider
{
    private readonly IMemoryStore _memoryStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserContextProvider"/> class.
    /// </summary>
    public UserContextProvider(IMemoryStore memoryStore)
    {
        _memoryStore = memoryStore;
    }

    /// <summary>
    /// Gets formatted user memory context for prompt construction.
    /// </summary>
    public async Task<string> GetUserContextAsync(string userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return string.Empty;
        }

        var memories = await _memoryStore.GetAllAsync(userId, ct).ConfigureAwait(false);
        var relevantMemories = memories
            .Where(entry => !entry.Key.StartsWith(ChatHistoryProvider.ChatHistoryKeyPrefix, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(entry => entry.CreatedAt)
            .ToList();

        if (relevantMemories.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine("USER MEMORY CONTEXT:");
        foreach (var memory in relevantMemories)
        {
            builder.Append("- ");
            builder.Append(memory.Key);
            builder.Append(": ");
            builder.AppendLine(memory.Value);
        }

        return builder.ToString().TrimEnd();
    }
}
