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
                    [FromKeyedServices(SqliteDbNames.Config)]
                    SqliteConnectionFactory configSqlite,
                    [FromKeyedServices(SqliteDbNames.Traces)]
                    SqliteConnectionFactory tracesSqlite,
                    [FromKeyedServices(SqliteDbNames.Tasks)]
                    SqliteConnectionFactory tasksSqlite,
                    CancellationToken cancellationToken) =>
                {
                    if (!IsAuthorizedValidationRequest(context)
                        || !Guid.TryParseExact(token, "D", out _))
                    {
                        return Results.NotFound();
                    }

                    var database = redis.GetDatabase();
                    const string Key = "lucia:update-validation";
                    if (!await database.StringSetAsync(
                            Key,
                            token)
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

                    foreach (var sqlite in new[]
                             {
                                 configSqlite,
                                 tracesSqlite,
                                 tasksSqlite,
                             })
                    {
                        await using var sentinelConnection =
                            sqlite.CreateConnection();
                        await using var sentinelCommand =
                            sentinelConnection.CreateCommand();
                        sentinelCommand.CommandText =
                            """
                            CREATE TABLE IF NOT EXISTS appliance_update_validation (
                                id INTEGER PRIMARY KEY CHECK (id = 1),
                                token TEXT NOT NULL
                            );
                            INSERT INTO appliance_update_validation (id, token)
                            VALUES (1, $token)
                            ON CONFLICT(id) DO UPDATE SET token = excluded.token;
                            PRAGMA wal_checkpoint(FULL);
                            """;
                        sentinelCommand.Parameters.AddWithValue("$token", token);
                        await sentinelCommand.ExecuteNonQueryAsync(
                                cancellationToken)
                            .ConfigureAwait(false);
                    }

                    await using var connection =
                        configSqlite.CreateConnection();
                    await using var command = connection.CreateCommand();
                    command.CommandText =
                        """
                        DELETE FROM configuration
                        WHERE key = 'appliance-update-validation-consumed';
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
                        "appliance-update-validation");
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
                    bool? consume,
                    HttpContext context,
                    IConnectionMultiplexer redis,
                    [FromKeyedServices(SqliteDbNames.Config)]
                    SqliteConnectionFactory configSqlite,
                    [FromKeyedServices(SqliteDbNames.Traces)]
                    SqliteConnectionFactory tracesSqlite,
                    [FromKeyedServices(SqliteDbNames.Tasks)]
                    SqliteConnectionFactory tasksSqlite,
                    OnnxProviderDetector providers,
                    CancellationToken cancellationToken) =>
                {
                    if (!IsAuthorizedValidationRequest(context)
                        || !Guid.TryParseExact(token, "D", out _))
                    {
                        return Results.NotFound();
                    }

                    var database = redis.GetDatabase();
                    const string SentinelKey = "lucia:update-validation";
                    var cleanupPending = false;
                    if (consume is true)
                    {
                        await using var cleanupStateConnection =
                            configSqlite.CreateConnection();
                        await using var cleanupStateCommand =
                            cleanupStateConnection.CreateCommand();
                        cleanupStateCommand.CommandText =
                            """
                            SELECT value
                            FROM configuration
                            WHERE key = 'appliance-update-validation-consumed';
                            """;
                        cleanupPending = Convert.ToString(
                                await cleanupStateCommand.ExecuteScalarAsync(
                                        cancellationToken)
                                    .ConfigureAwait(false),
                                System.Globalization.CultureInfo.InvariantCulture)
                            == token;
                    }
                    if (await database.StringGetAsync(SentinelKey)
                            .ConfigureAwait(false) != token
                        && !cleanupPending)
                    {
                        return Results.Problem(
                            detail: "Redis update validation failed.",
                            statusCode: StatusCodes.Status503ServiceUnavailable);
                    }

                    foreach (var sqlite in new[]
                             {
                                 configSqlite,
                                 tracesSqlite,
                                 tasksSqlite,
                             })
                    {
                        await using var integrityConnection =
                            sqlite.CreateConnection();
                        await using var integrityCommand =
                            integrityConnection.CreateCommand();
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
                        await using var continuityCommand =
                            integrityConnection.CreateCommand();
                        continuityCommand.CommandText =
                            """
                            SELECT token
                            FROM appliance_update_validation
                            WHERE id = 1;
                            """;
                        if (Convert.ToString(
                                await continuityCommand.ExecuteScalarAsync(
                                        cancellationToken)
                                    .ConfigureAwait(false),
                                System.Globalization.CultureInfo.InvariantCulture)
                            != token
                            && !cleanupPending)
                        {
                            return Results.Problem(
                                detail: "SQLite update continuity validation failed.",
                                statusCode: StatusCodes.Status503ServiceUnavailable);
                        }
                    }
                    await using var connection =
                        configSqlite.CreateConnection();
                    await using var sentinelCommand = connection.CreateCommand();
                    sentinelCommand.CommandText =
                        "SELECT value FROM configuration WHERE key = $key;";
                    sentinelCommand.Parameters.AddWithValue(
                        "$key",
                        "appliance-update-validation");
                    var sqliteSentinel = Convert.ToString(
                        await sentinelCommand.ExecuteScalarAsync(cancellationToken)
                            .ConfigureAwait(false),
                        System.Globalization.CultureInfo.InvariantCulture);
                    if (sqliteSentinel != token && !cleanupPending)
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

                    if (consume is true)
                    {
                        if (!cleanupPending)
                        {
                            await using var cleanupStateConnection =
                                configSqlite.CreateConnection();
                            await using var cleanupStateCommand =
                                cleanupStateConnection.CreateCommand();
                            cleanupStateCommand.CommandText =
                                """
                                INSERT INTO configuration (
                                    key,
                                    value,
                                    section,
                                    updated_by,
                                    is_sensitive
                                ) VALUES (
                                    'appliance-update-validation-consumed',
                                    $token,
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
                            cleanupStateCommand.Parameters.AddWithValue(
                                "$token",
                                token);
                            await cleanupStateCommand.ExecuteNonQueryAsync(
                                    cancellationToken)
                                .ConfigureAwait(false);
                        }
                        foreach (var sqlite in new[]
                                 {
                                     configSqlite,
                                     tracesSqlite,
                                     tasksSqlite,
                                 })
                        {
                            await using var cleanupConnection =
                                sqlite.CreateConnection();
                            await using var cleanupCommand =
                                cleanupConnection.CreateCommand();
                            cleanupCommand.CommandText =
                                """
                                DELETE FROM appliance_update_validation
                                WHERE id = 1 AND token = $token;
                                PRAGMA wal_checkpoint(FULL);
                                """;
                            cleanupCommand.Parameters.AddWithValue(
                                "$token",
                                token);
                            await cleanupCommand.ExecuteNonQueryAsync(
                                    cancellationToken)
                                .ConfigureAwait(false);
                        }
                        await using var cleanupConfigConnection =
                            configSqlite.CreateConnection();
                        await using var cleanupConfigCommand =
                            cleanupConfigConnection.CreateCommand();
                        cleanupConfigCommand.CommandText =
                            """
                            DELETE FROM configuration
                            WHERE key = $key AND value = $token;
                            PRAGMA wal_checkpoint(FULL);
                            """;
                        cleanupConfigCommand.Parameters.AddWithValue(
                            "$key",
                            "appliance-update-validation");
                        cleanupConfigCommand.Parameters.AddWithValue(
                            "$token",
                            token);
                        await cleanupConfigCommand.ExecuteNonQueryAsync(
                                cancellationToken)
                            .ConfigureAwait(false);
                        await database.KeyDeleteAsync(SentinelKey)
                            .ConfigureAwait(false);
                        var cleanupWait =
                            (RedisResult[]?)await database.ExecuteAsync(
                                    "WAITAOF",
                                    1,
                                    0,
                                    5000)
                                .ConfigureAwait(false)
                            ?? [];
                        if (cleanupWait.Length != 2
                            || (long)cleanupWait[0] < 1)
                        {
                            return Results.Problem(
                                detail:
                                    "Redis validation sentinel cleanup was not persisted to AOF.",
                                statusCode:
                                    StatusCodes.Status503ServiceUnavailable);
                        }
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
        var path = context.RequestServices
                .GetRequiredService<IConfiguration>()[
                    "LUCIA_VALIDATION_CREDENTIAL_PATH"]
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
                ApplianceUpdateService updates,
                HttpContext context,
                IHostApplicationLifetime lifetime,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    if (await GetUpdateConflictAsync(
                            updates,
                            cancellationToken)
                        .ConfigureAwait(false) is { } conflict)
                    {
                        return conflict;
                    }
                    if (service == "agenthost")
                    {
                        context.Response.OnCompleted(() =>
                        {
                            lifetime.StopApplication();
                            return Task.CompletedTask;
                        });
                        return Results.Accepted();
                    }
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
                ApplianceUpdateService updates,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    if (await GetUpdateConflictAsync(
                            updates,
                            cancellationToken)
                        .ConfigureAwait(false) is { } conflict)
                    {
                        return conflict;
                    }
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
                ApplianceUpdateService updates,
                HttpContext context,
                IHostApplicationLifetime lifetime,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    if (await GetUpdateConflictAsync(
                            updates,
                            cancellationToken)
                        .ConfigureAwait(false) is { } conflict)
                    {
                        return conflict;
                    }
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
        group.MapGet(
            "/updates/operations/{operationId}",
            async (
                string operationId,
                ApplianceUpdateService updates,
                CancellationToken cancellationToken) =>
            {
                if (!Guid.TryParseExact(operationId, "D", out _))
                {
                    return Results.NotFound();
                }
                try
                {
                    return Results.Ok(await updates
                        .GetOperationAsync(cancellationToken, operationId)
                        .ConfigureAwait(false));
                }
                catch (HttpRequestException exception)
                    when (exception.StatusCode
                        == System.Net.HttpStatusCode.NotFound)
                {
                    return Results.NotFound();
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

    internal static bool IsUpdateInProgress(string status) =>
        status is "queued" or "running";

    private static async Task<IResult?> GetUpdateConflictAsync(
        ApplianceUpdateService updates,
        CancellationToken cancellationToken)
    {
        var operation = await updates
            .GetOperationAsync(cancellationToken)
            .ConfigureAwait(false);
        return IsUpdateInProgress(operation.Status)
            ? Results.Conflict(new
            {
                Error = "An appliance update is in progress.",
            })
            : null;
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
