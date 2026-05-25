namespace lucia.Agents.Models;

/// <summary>
/// Represents a single user memory entry.
/// </summary>
/// <param name="Key">The memory key.</param>
/// <param name="Value">The stored memory value.</param>
/// <param name="CreatedAt">When the memory was created.</param>
/// <param name="ExpiresAt">When the memory expires, if applicable.</param>
public sealed record MemoryEntry(string Key, string Value, DateTime CreatedAt, DateTime? ExpiresAt);
