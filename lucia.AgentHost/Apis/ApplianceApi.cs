using System.Text.Json;
using lucia.AgentHost.Appliance;

namespace lucia.AgentHost.Apis;

public static class ApplianceApi
{
    public static RouteGroupBuilder MapApplianceApi(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/appliance")
            .WithTags("Appliance")
            .RequireAuthorization();

        group.MapGet(
            "/status",
            async (
                ApplianceManagerClient manager,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    return Results.Ok(await manager
                        .GetStatusAsync(cancellationToken)
                        .ConfigureAwait(false));
                }
                catch (HttpRequestException exception)
                {
                    return ManagerProblem(exception, "Appliance status failed");
                }
            });
        group.MapPost(
            "/services/{service}/restart",
            async (
                string service,
                ApplianceManagerClient manager,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    await manager.RestartServiceAsync(service, cancellationToken)
                        .ConfigureAwait(false);
                    return Results.Accepted();
                }
                catch (HttpRequestException exception)
                {
                    return ManagerProblem(exception, "Service restart failed");
                }
            });
        group.MapPost(
            "/host/reboot",
            async (
                ApplianceManagerClient manager,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    await manager.RebootHostAsync(cancellationToken)
                        .ConfigureAwait(false);
                    return Results.Accepted();
                }
                catch (HttpRequestException exception)
                {
                    return ManagerProblem(exception, "Jetson reboot failed");
                }
            });
        group.MapGet(
            "/telemetry",
            async (
                ApplianceManagerClient manager,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    return Results.Ok(await manager
                        .GetTelemetryAsync(cancellationToken)
                        .ConfigureAwait(false));
                }
                catch (HttpRequestException exception)
                {
                    return ManagerProblem(
                        exception,
                        "Telemetry status failed");
                }
            });
        group.MapPut(
            "/telemetry",
            async (
                ApplianceTelemetryConfigurationRequest request,
                ApplianceManagerClient manager,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    return Results.Ok(await manager
                        .UpdateTelemetryAsync(request, cancellationToken)
                        .ConfigureAwait(false));
                }
                catch (HttpRequestException exception)
                {
                    return ManagerProblem(
                        exception,
                        "Telemetry configuration failed");
                }
            });
        group.MapGet(
            "/updates",
            async (
                ApplianceUpdateService updates,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    return Results.Ok(await updates
                        .CheckAsync(cancellationToken)
                        .ConfigureAwait(false));
                }
                catch (HttpRequestException exception)
                {
                    return Results.Problem(
                        detail: exception.Message,
                        statusCode: StatusCodes.Status502BadGateway,
                        title: "GitHub update check failed");
                }
                catch (Exception exception) when (
                    exception is JsonException
                        or KeyNotFoundException
                        or InvalidOperationException
                        or FormatException
                        or OverflowException)
                {
                    return Results.Problem(
                        detail: exception.Message,
                        statusCode: StatusCodes.Status502BadGateway,
                        title: "GitHub appliance manifest is invalid");
                }
            });

        return group;
    }

    private static IResult ManagerProblem(
        HttpRequestException exception,
        string title) =>
        Results.Problem(
            detail: exception.Message,
            statusCode: exception.StatusCode is null
                ? StatusCodes.Status502BadGateway
                : (int)exception.StatusCode,
            title: title);
}
