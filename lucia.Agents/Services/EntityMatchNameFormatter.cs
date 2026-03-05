namespace lucia.Agents.Services;

internal static class EntityMatchNameFormatter
{
    public static string ResolveName(
        string? name,
        IReadOnlyList<string>? aliases,
        string fallbackId,
        bool stripDomainFromId = false)
    {
        var normalizedName = NormalizeOptional(name);
        if (normalizedName is not null)
        {
            return normalizedName;
        }

        if (aliases is not null)
        {
            foreach (var alias in aliases)
            {
                var normalizedAlias = NormalizeOptional(alias);
                if (normalizedAlias is not null)
                {
                    return normalizedAlias;
                }
            }
        }

        var fallback = string.IsNullOrWhiteSpace(fallbackId) ? string.Empty : fallbackId.Trim();
        if (stripDomainFromId)
        {
            var separatorIndex = fallback.IndexOf('.');
            if (separatorIndex >= 0 && separatorIndex < fallback.Length - 1)
            {
                fallback = fallback[(separatorIndex + 1)..];
            }
        }

        fallback = fallback.Replace('_', ' ');
        var normalizedFallback = NormalizeOptional(fallback);
        if (normalizedFallback is not null)
        {
            return normalizedFallback;
        }

        return string.IsNullOrWhiteSpace(fallbackId) ? "unknown" : fallbackId;
    }

    public static List<string> SanitizeAliases(IReadOnlyList<string>? aliases)
    {
        if (aliases is null || aliases.Count == 0)
        {
            return [];
        }

        var result = new List<string>(aliases.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var alias in aliases)
        {
            var normalizedAlias = NormalizeOptional(alias);
            if (normalizedAlias is null)
            {
                continue;
            }

            if (seen.Add(normalizedAlias))
            {
                result.Add(normalizedAlias);
            }
        }

        return result;
    }

    private static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? null : string.Join(' ', parts);
    }
}
