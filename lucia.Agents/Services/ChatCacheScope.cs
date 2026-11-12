namespace lucia.Agents.Services;

/// <summary>
/// Scopes semantic chat-cache matches to a single agent by comparing system-instruction hashes.
/// An entry is only eligible when both sides carry a hash and they are equal — legacy entries
/// stored before the property existed (null/empty) and lookups without instructions must never
/// match semantically.
/// </summary>
public static class ChatCacheScope
{
    /// <summary>
    /// Returns true only when both hashes are present and identical (ordinal comparison).
    /// </summary>
    public static bool Matches(string? storedInstructionsHash, string? expectedInstructionsHash)
    {
        if (string.IsNullOrWhiteSpace(storedInstructionsHash))
            return false;

        if (string.IsNullOrWhiteSpace(expectedInstructionsHash))
            return false;

        return string.Equals(storedInstructionsHash, expectedInstructionsHash, StringComparison.Ordinal);
    }
}
