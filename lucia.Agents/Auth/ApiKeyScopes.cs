namespace lucia.Agents.Auth;

public static class ApiKeyScopes
{
    public static string[] Create(bool isAdministrator) =>
        isAdministrator
            ? ["*", AuthOptions.AdministratorScope]
            : ["*"];
}
