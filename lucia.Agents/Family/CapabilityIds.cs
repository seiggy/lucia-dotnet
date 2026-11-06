namespace lucia.Agents.Family;

/// <summary>
/// Normalizes agent and tool identifiers so rules match regardless of case or an
/// <c>Async</c> method-name suffix.
/// </summary>
public static class CapabilityIds
{
    public static string Normalize(string capabilityId)
    {
        var trimmed = capabilityId.Trim();
        return trimmed.EndsWith("Async", StringComparison.OrdinalIgnoreCase) && trimmed.Length > "Async".Length
            ? trimmed[..^"Async".Length]
            : trimmed;
    }

    public static bool AreEqual(string left, string right) =>
        string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
}
