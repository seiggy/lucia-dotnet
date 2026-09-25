using lucia.AgentHost.Appliance;
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
        group.MapGet("/version", GetVersion);
        group.MapPost("/restart", TriggerRestartAsync);

        return group;
    }

    private static Ok<object> GetRestartRequired(PluginChangeTracker tracker) =>
        TypedResults.Ok<object>(new { RestartRequired = tracker.IsRestartRequired });

    private static Ok<object> GetVersion(IConfiguration configuration) =>
        TypedResults.Ok<object>(new
        {
            Version = string.IsNullOrWhiteSpace(configuration["LUCIA_VERSION"])
                ? "development"
                : configuration["LUCIA_VERSION"],
        });

    private static async Task<IResult> TriggerRestartAsync(
        IHostApplicationLifetime lifetime,
        PluginChangeTracker tracker,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        var updates = services.GetService<ApplianceUpdateService>();
        if (updates is not null)
        {
            try
            {
                var operation = await updates
                    .GetOperationAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (ApplianceApi.IsUpdateInProgress(operation.Status))
                {
                    return Results.Conflict(new
                    {
                        Error = "An appliance update is in progress.",
                    });
                }
            }
            catch (HttpRequestException exception)
            {
                return Results.Problem(
                    detail: exception.Message,
                    statusCode: exception.StatusCode is null
                        ? StatusCodes.Status502BadGateway
                        : (int)exception.StatusCode,
                    title: "Update operation status failed");
            }
        }
        tracker.ClearRestartRequired();
        lifetime.StopApplication();
        return TypedResults.Ok();
    }

}
