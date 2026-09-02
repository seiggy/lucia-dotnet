using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using lucia.AgentHost.Appliance;
using lucia.Agents.Auth;
using lucia.Data.Sqlite;
using lucia.Wyoming.Models;
using StackExchange.Redis;

namespace lucia.AgentHost.Apis;

public static class ApplianceApi
{
    public static void MapApplianceUpdateValidation(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/internal/appliance/update-validation/prepare/{token}",
                async (
                    string token,
                    HttpContext context,
                    IConnectionMultiplexer redis,
                    SqliteConnectionFactory sqlite,
                    CancellationToken cancellationToken) =>
                {
                    if (!IsAuthorizedValidationRequest(context)
                        || !Guid.TryParseExact(token, "D", out _))
                    {
                        return Results.NotFound();
                    }

                    var database = redis.GetDatabase();
                    var key = $"lucia:update-validation:{token}";
                    if (!await database.StringSetAsync(
                            key,
                            token,
                            TimeSpan.FromDays(1))
                        .ConfigureAwait(false))
                    {
                        return Results.Problem(
                            detail: "Redis validation sentinel could not be written.",
                            statusCode: StatusCodes.Status503ServiceUnavailable);
                    }
                    var waitResult = (RedisResult[]?)await database.ExecuteAsync(
                                "WAITAOF",
                                1,
                                0,
                                5000)
                            .ConfigureAwait(false)
                        ?? [];
                    if (waitResult.Length != 2
                        || (long)waitResult[0] < 1)
                    {
                        return Results.Problem(
                            detail:
                                "Redis validation sentinel was not persisted to AOF.",
                            statusCode: StatusCodes.Status503ServiceUnavailable);
                    }

                    await using var connection = sqlite.CreateConnection();
                    await using var command = connection.CreateCommand();
                    command.CommandText =
                        """
                        INSERT INTO configuration (
                            key,
                            value,
                            section,
                            updated_by,
                            is_sensitive
                        ) VALUES (
                            $key,
                            $value,
                            'system',
                            'appliance-update',
                            0
                        )
                        ON CONFLICT(key) DO UPDATE SET
                            value = excluded.value,
                            updated_at = datetime('now'),
                            updated_by = excluded.updated_by;
                        PRAGMA wal_checkpoint(FULL);
                        """;
                    command.Parameters.AddWithValue(
                        "$key",
                        $"appliance-update-validation:{token}");
                    command.Parameters.AddWithValue("$value", token);
                    await command.ExecuteNonQueryAsync(cancellationToken)
                        .ConfigureAwait(false);
                    return Results.Ok(new { Status = "prepared" });
                })
            .ExcludeFromDescription();
        endpoints.MapGet(
                "/internal/appliance/update-validation/{token}",
                async (
                    string token,
                    HttpContext context,
                    IConnectionMultiplexer redis,
                    SqliteConnectionFactory sqlite,
                    OnnxProviderDetector providers,
                    bool consume,
                    CancellationToken cancellationToken) =>
                {
                    if (!IsAuthorizedValidationRequest(context)
                        || !Guid.TryParseExact(token, "D", out _))
                    {
                        return Results.NotFound();
                    }

                    var database = redis.GetDatabase();
                    var sentinelKey = $"lucia:update-validation:{token}";
                    if (await database.StringGetAsync(sentinelKey)
                            .ConfigureAwait(false) != token)
                    {
                        return Results.Problem(
                            detail: "Redis update validation failed.",
                            statusCode: StatusCodes.Status503ServiceUnavailable);
                    }

                    await using var connection = sqlite.CreateConnection();
                    await using var integrityCommand = connection.CreateCommand();
                    integrityCommand.CommandText = "PRAGMA quick_check;";
                    if (Convert.ToString(
                            await integrityCommand
                                .ExecuteScalarAsync(cancellationToken)
                                .ConfigureAwait(false),
                            System.Globalization.CultureInfo.InvariantCulture)
                        != "ok")
                    {
                        return Results.Problem(
                            detail: "SQLite integrity validation failed.",
                            statusCode: StatusCodes.Status503ServiceUnavailable);
                    }
                    await using var sentinelCommand = connection.CreateCommand();
                    sentinelCommand.CommandText =
                        "SELECT value FROM configuration WHERE key = $key;";
                    sentinelCommand.Parameters.AddWithValue(
                        "$key",
                        $"appliance-update-validation:{token}");
                    var sqliteSentinel = Convert.ToString(
                        await sentinelCommand.ExecuteScalarAsync(cancellationToken)
                            .ConfigureAwait(false),
                        System.Globalization.CultureInfo.InvariantCulture);
                    if (sqliteSentinel != token)
                    {
                        return Results.Problem(
                            detail: "SQLite update validation failed.",
                            statusCode: StatusCodes.Status503ServiceUnavailable);
                    }
                    if (providers.BestProvider != "CUDAExecutionProvider")
                    {
                        return Results.Problem(
                            detail: "CUDA update validation failed.",
                            statusCode: StatusCodes.Status503ServiceUnavailable);
                    }

                    if (consume)
                    {
                        _ = await database.KeyDeleteAsync(sentinelKey)
                            .ConfigureAwait(false);
                        await using var deleteCommand = connection.CreateCommand();
                        deleteCommand.CommandText =
                            "DELETE FROM configuration WHERE key = $key;";
                        deleteCommand.Parameters.AddWithValue(
                            "$key",
                            $"appliance-update-validation:{token}");
                        await deleteCommand.ExecuteNonQueryAsync(cancellationToken)
                            .ConfigureAwait(false);
                    }
                    return Results.Ok(new { Status = "healthy" });
                })
            .ExcludeFromDescription();
    }

    private static bool IsLoopback(HttpContext context)
    {
        var remoteAddress = context.Connection.RemoteIpAddress;
        return remoteAddress is not null
            && System.Net.IPAddress.IsLoopback(remoteAddress);
    }

    private static bool IsAuthorizedValidationRequest(HttpContext context)
    {
        if (!IsLoopback(context))
        {
            return false;
        }
        var path = Environment.GetEnvironmentVariable(
                "LUCIA_VALIDATION_CREDENTIAL_PATH")
            ?? "/var/lib/lucia/updates/state/validation.key";
        if (!File.Exists(path))
        {
            return false;
        }
        var supplied = context.Request.Headers[
            "X-Lucia-Update-Credential"].ToString();
        return IsValidValidationCredential(supplied, path);
    }

    internal static bool IsValidValidationCredential(
        string supplied,
        string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }
        var expected = File.ReadAllText(path).Trim();
        return Guid.TryParseExact(expected, "D", out _)
            && supplied.Length == expected.Length
            && CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(supplied),
                Encoding.UTF8.GetBytes(expected));
    }

    public static RouteGroupBuilder MapApplianceApi(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/appliance")
            .WithTags("Appliance")
            .RequireAuthorization(AuthOptions.AdministratorPolicy);

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
                HttpContext context,
                IHostApplicationLifetime lifetime,
                CancellationToken cancellationToken) =>
            {
                if (service == "agenthost")
                {
                    context.Response.OnCompleted(() =>
                    {
                        lifetime.StopApplication();
                        return Task.CompletedTask;
                    });
                    return Results.Accepted();
                }
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
                HttpContext context,
                IHostApplicationLifetime lifetime,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var status = await manager
                        .UpdateTelemetryAsync(request, cancellationToken)
                        .ConfigureAwait(false);
                    context.Response.OnCompleted(() =>
                    {
                        lifetime.StopApplication();
                        return Task.CompletedTask;
                    });
                    return Results.Ok(status);
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
                catch (OperationCanceledException exception) when (
                    !cancellationToken.IsCancellationRequested)
                {
                    return Results.Problem(
                        detail: exception.Message,
                        statusCode: StatusCodes.Status504GatewayTimeout,
                        title: "GitHub update check timed out");
                }
                catch (Exception exception) when (
                    exception is JsonException
                        or InvalidDataException
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
        group.MapPost(
            "/updates/{channel}/install",
            async (
                string channel,
                ApplianceUpdateRequest request,
                ApplianceUpdateService updates,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    return Results.Accepted(
                        value: await updates
                            .InstallAsync(channel, request.Tag, cancellationToken)
                            .ConfigureAwait(false));
                }
                catch (HttpRequestException exception)
                {
                    return ManagerProblem(exception, "Appliance update failed");
                }
                catch (InvalidOperationException exception)
                {
                    return Results.Problem(
                        detail: exception.Message,
                        statusCode: StatusCodes.Status409Conflict,
                        title: "Appliance update is busy");
                }
                catch (Exception exception) when (
                    exception is JsonException
                        or InvalidDataException
                        or KeyNotFoundException
                        or ArgumentException)
                {
                    return Results.Problem(
                        detail: exception.Message,
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "Appliance update is invalid");
                }
            });
        group.MapGet(
            "/updates/operation",
            async (
                ApplianceUpdateService updates,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    return Results.Ok(await updates
                        .GetOperationAsync(cancellationToken)
                        .ConfigureAwait(false));
                }
                catch (HttpRequestException exception)
                {
                    return ManagerProblem(
                        exception,
                        "Update operation status failed");
                }
            });
        group.MapPost(
            "/updates/{channel}/rollback",
            async (
                string channel,
                ApplianceUpdateService updates,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    return Results.Accepted(
                        value: await updates
                            .RollbackAsync(channel, cancellationToken)
                            .ConfigureAwait(false));
                }
                catch (HttpRequestException exception)
                {
                    return ManagerProblem(
                        exception,
                        "Appliance rollback failed");
                }
                catch (InvalidOperationException exception)
                {
                    return Results.Problem(
                        detail: exception.Message,
                        statusCode: StatusCodes.Status409Conflict,
                        title: "Appliance update is busy");
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
