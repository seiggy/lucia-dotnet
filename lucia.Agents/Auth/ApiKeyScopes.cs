namespace lucia.Agents.Auth;

public static class ApiKeyScopes
{
    /// <summary>
    /// Access to every non-administrator API, as before family members existed.
    /// </summary>
    public const string Full = "*";

    /// <summary>
    /// Access only to member-facing APIs, for keys issued to a family member's device.
    /// </summary>
    public const string Member = "member";

    public static string[] Create(bool isAdministrator) =>
        isAdministrator
            ? [Full, AuthOptions.AdministratorScope]
            : [Full];

    public static bool HasFullAccess(IEnumerable<string> scopes) =>
        scopes.Contains(Full, StringComparer.Ordinal);
}
