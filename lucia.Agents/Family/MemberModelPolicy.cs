namespace lucia.Agents.Family;

public static class MemberModelPolicy
{
    /// <summary>
    /// Returns the provider that must handle a member's call instead of
    /// <paramref name="currentProviderId"/>, or null to keep it. The preferred provider wins;
    /// otherwise a disallowed provider is replaced by the first allowed one.
    /// </summary>
    public static string? SelectProvider(MemberLlmControls controls, string currentProviderId)
    {
        if (controls.PreferredProviderId is { Length: > 0 } preferred)
        {
            return string.Equals(preferred, currentProviderId, StringComparison.Ordinal) ? null : preferred;
        }

        return controls.AllowedProviderIds.Count == 0
            || controls.AllowedProviderIds.Contains(currentProviderId, StringComparer.Ordinal)
                ? null
                : controls.AllowedProviderIds[0];
    }

    public static bool IsAllowed(MemberLlmControls controls, string providerId) =>
        controls.AllowedProviderIds.Count == 0 || controls.AllowedProviderIds.Contains(providerId, StringComparer.Ordinal);

    /// <summary>
    /// True when the member's model calls must go through Lucia's metered clients, so agents
    /// that call models some other way (Copilot, remote agents) cannot serve them.
    /// </summary>
    public static bool RequiresManagedModels(MemberLlmControls controls) =>
        controls.AllowedProviderIds.Count > 0
        || controls.PreferredProviderId is { Length: > 0 }
        || controls.DailyBudgetUsd is not null
        || controls.MonthlyBudgetUsd is not null;
}
