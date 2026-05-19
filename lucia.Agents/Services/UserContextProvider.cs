using System.Linq;
using System.Text;

using lucia.Agents.Abstractions;

namespace lucia.Agents.Services;

/// <summary>
/// Formats stored per-user memories for prompt injection.
/// </summary>
public sealed class UserContextProvider
{
    private const int MaxMemories = 50;
    private const int MaxCharacters = 4000;

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

        var memories = await _memoryStore.SearchAsync(userId, null, MaxMemories, ct).ConfigureAwait(false);
        var relevantMemories = memories
            .Where(entry => !entry.Key.StartsWith(ChatHistoryProvider.ChatHistoryKeyPrefix, StringComparison.OrdinalIgnoreCase))
            .Take(MaxMemories)
            .ToList();

        if (relevantMemories.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine("USER MEMORY CONTEXT:");
        var totalCharacters = 0;

        foreach (var memory in relevantMemories)
        {
            var line = $"- {memory.Key}: {memory.Value}";
            if (totalCharacters + line.Length > MaxCharacters)
            {
                break;
            }

            builder.AppendLine(line);
            totalCharacters += line.Length;
        }

        return builder.Length == "USER MEMORY CONTEXT:".Length + Environment.NewLine.Length
            ? string.Empty
            : builder.ToString().TrimEnd();
    }
}
