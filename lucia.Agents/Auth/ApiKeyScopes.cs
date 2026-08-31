namespace lucia.Agents.Auth;

public static class ApiKeyScopes
{
    public static string[] ForName(string name) =>
        string.Equals(name, "Dashboard", StringComparison.Ordinal)
            ? ["*", AuthOptions.AdministratorScope]
            : ["*"];
}
