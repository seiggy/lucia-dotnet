using lucia.Agents.PluginFramework;
using lucia.Agents.Auth;
using Microsoft.AspNetCore.Http.HttpResults;

namespace lucia.AgentHost.Apis;

/// <summary>
/// System-level endpoints for restart signaling.
/// </summary>
public static class SystemApi
{
    public static RouteGroupBuilder MapSystemApi(
        this IEndpointRouteBuilder endpoints,
        bool requireAdministrator)
    {
        var group = endpoints.MapGroup("/api/system")
            .WithTags("System");
        if (requireAdministrator)
        {
            group.RequireAuthorization(AuthOptions.AdministratorPolicy);
        }
        else
        {
            group.RequireAuthorization();
        }

        group.MapGet("/restart-required", GetRestartRequired);
        group.MapPost("/restart", TriggerRestart);

        return group;
    }

    private static Ok<object> GetRestartRequired(PluginChangeTracker tracker) =>
        TypedResults.Ok<object>(new { RestartRequired = tracker.IsRestartRequired });

    private static Ok TriggerRestart(
        IHostApplicationLifetime lifetime,
        PluginChangeTracker tracker)
    {
        tracker.ClearRestartRequired();
        lifetime.StopApplication();
        return TypedResults.Ok();
    }

}
