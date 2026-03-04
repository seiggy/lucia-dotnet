using lucia.Agents.PluginFramework;
using Microsoft.AspNetCore.Http.HttpResults;

namespace lucia.AgentHost.Apis;

/// <summary>
/// System-level endpoints for restart signaling.
/// </summary>
public static class SystemApi
{
    public static RouteGroupBuilder MapSystemApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/system")
            .WithTags("System")
            .RequireAuthorization();

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
