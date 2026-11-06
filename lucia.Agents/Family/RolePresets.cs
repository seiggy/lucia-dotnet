namespace lucia.Agents.Family;

/// <summary>
/// Defaults applied when a member has no explicit rule for a capability. Tools are read-only
/// only when their name says so; everything else is treated as a change.
/// </summary>
public static class RolePresets
{
    private static readonly string[] ReadOnlyPrefixes = ["Get", "Find", "List", "Search", "Read", "memory_read", "memory_search"];

    private static readonly HashSet<string> SecurityTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "ArmAlarm",
        "DisarmAlarm",
        "LockDoor",
        "UnlockDoor",
    };

    private const string SecurityAgentId = "security-agent";

    public static ActionRule Evaluate(FamilyRole role, CapabilityKind kind, string capabilityId)
    {
        if (role is FamilyRole.Administrator or FamilyRole.Adult)
        {
            return ActionRule.Allow;
        }

        var id = CapabilityIds.Normalize(capabilityId);
        if (kind == CapabilityKind.Agent)
        {
            return string.Equals(id, SecurityAgentId, StringComparison.OrdinalIgnoreCase)
                ? ActionRule.Deny
                : ActionRule.Allow;
        }

        if (SecurityTools.Contains(id))
        {
            return ActionRule.Deny;
        }

        if (IsReadOnly(id))
        {
            return ActionRule.Allow;
        }

        return role == FamilyRole.Child ? ActionRule.AskGuardian : ActionRule.Deny;
    }

    public static bool IsReadOnly(string toolName)
    {
        var id = CapabilityIds.Normalize(toolName);
        return ReadOnlyPrefixes.Any(prefix =>
            id.StartsWith(prefix, StringComparison.Ordinal)
            && (id.Length == prefix.Length || !char.IsLower(id[prefix.Length])));
    }
}
