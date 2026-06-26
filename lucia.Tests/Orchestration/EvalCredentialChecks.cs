namespace lucia.Tests.Orchestration;

/// <summary>
/// Helpers for inspecting eval test credential values such as API keys.
/// Treats the angle-bracket placeholder convention used in committed
/// <c>appsettings.json</c> (e.g. <c>&lt;YOUR_AZURE_OPENAI_API_KEY&gt;</c>)
/// as "not configured" so eval tests skip cleanly when secrets have not
/// been overridden via user secrets or environment variables.
/// </summary>
public static class EvalCredentialChecks
{
    /// <summary>
    /// Returns <c>true</c> when <paramref name="value"/> is a non-empty
    /// angle-bracket placeholder like <c>&lt;YOUR_KEY&gt;</c>. Empty values
    /// return <c>false</c> — callers can treat them as "use the default
    /// credential" rather than as a placeholder.
    /// </summary>
    public static bool IsPlaceholder(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        return trimmed.Length >= 2 && trimmed[0] == '<' && trimmed[^1] == '>';
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="value"/> is non-empty and not
    /// an angle-bracket placeholder.
    /// </summary>
    public static bool IsUsable(string? value) =>
        !string.IsNullOrWhiteSpace(value) && !IsPlaceholder(value);
}
