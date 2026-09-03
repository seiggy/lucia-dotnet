using System.Net;
using System.Net.Http.Json;

using lucia.AgentHost.Apis;
using lucia.Data.Sqlite;
using lucia.Wyoming.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.Sqlite;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace lucia.Tests.Appliance;

public sealed class ApplianceUpdateValidationIntegrationTests
{
    [Fact]
    public async Task ValidationEndpoints_PersistAndVerifyAllContinuitySentinels()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"lucia-update-validation-{Guid.NewGuid():N}");
        var credentialPath = Path.Combine(root, "validation.key");
        var credential = Guid.NewGuid().ToString("D");
        var token = Guid.NewGuid().ToString("D");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(credentialPath, credential);

        await using var redisContainer = new RedisBuilder("redis:7-alpine")
            .WithCommand("--appendonly", "yes", "--appendfsync", "always")
            .Build();
        await redisContainer.StartAsync();
        await using var redis = await ConnectionMultiplexer.ConnectAsync(
            redisContainer.GetConnectionString());
        using var configSqlite = CreateSqlite(root, SqliteDbNames.Config, true);
        using var tracesSqlite = CreateSqlite(root, SqliteDbNames.Traces, false);
        using var tasksSqlite = CreateSqlite(root, SqliteDbNames.Tasks, false);

        try
        {
            var app = CreateApp(
                redis,
                configSqlite,
                tracesSqlite,
                tasksSqlite,
                credentialPath,
                new OnnxProviderDetector(
                    "CUDAExecutionProvider",
                    "cuda"));
            await using var healthyApp = app;
            await app.StartAsync();

            using var client = CreateClient(app);
            using var unauthorized = await client.PostAsync(
                $"/internal/appliance/update-validation/prepare/{token}",
                content: null);
            Assert.Equal(HttpStatusCode.NotFound, unauthorized.StatusCode);

            client.DefaultRequestHeaders.Add(
                "X-Lucia-Update-Credential",
                credential);
            using var prepared = await client.PostAsync(
                $"/internal/appliance/update-validation/prepare/{token}",
                content: null);
            Assert.Equal(HttpStatusCode.OK, prepared.StatusCode);
            Assert.Equal(
                token,
                await redis.GetDatabase().StringGetAsync(
                    "lucia:update-validation"));
            Assert.Null(await redis.GetDatabase().KeyTimeToLiveAsync(
                "lucia:update-validation"));
            AssertSentinel(configSqlite, token);
            AssertSentinel(tracesSqlite, token);
            AssertSentinel(tasksSqlite, token);

            using var healthy = await client.GetAsync(
                $"/internal/appliance/update-validation/{token}");
            Assert.Equal(HttpStatusCode.OK, healthy.StatusCode);
            var response = await healthy.Content.ReadFromJsonAsync<
                Dictionary<string, string>>();
            Assert.Equal("healthy", response!["status"]);

            using var consumed = await client.GetAsync(
                $"/internal/appliance/update-validation/{token}?consume=true");
            Assert.Equal(HttpStatusCode.OK, consumed.StatusCode);
            Assert.True((await redis.GetDatabase().StringGetAsync(
                "lucia:update-validation")).IsNull);
            AssertSentinelMissing(configSqlite);
            AssertSentinelMissing(tracesSqlite);
            AssertSentinelMissing(tasksSqlite);
            AssertConfigurationSentinelMissing(configSqlite);

            using var preparedAgain = await client.PostAsync(
                $"/internal/appliance/update-validation/prepare/{token}",
                content: null);
            Assert.Equal(HttpStatusCode.OK, preparedAgain.StatusCode);
            DeleteSentinel(tasksSqlite);
            using var missing = await client.GetAsync(
                $"/internal/appliance/update-validation/{token}");
            Assert.Equal(
                HttpStatusCode.ServiceUnavailable,
                missing.StatusCode);
            Assert.Contains(
                "SQLite update continuity validation failed.",
                await missing.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);

            using var restored = await client.PostAsync(
                $"/internal/appliance/update-validation/prepare/{token}",
                content: null);
            Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
            await app.StopAsync();

            var cpuApp = CreateApp(
                redis,
                configSqlite,
                tracesSqlite,
                tasksSqlite,
                credentialPath,
                new OnnxProviderDetector(
                    "CPUExecutionProvider",
                    "cpu"));
            await using var rejectedApp = cpuApp;
            await cpuApp.StartAsync();
            using var cpuClient = CreateClient(cpuApp);
            cpuClient.DefaultRequestHeaders.Add(
                "X-Lucia-Update-Credential",
                credential);
            using var rejected = await cpuClient.GetAsync(
                $"/internal/appliance/update-validation/{token}");
            Assert.Equal(
                HttpStatusCode.ServiceUnavailable,
                rejected.StatusCode);
            Assert.Contains(
                "CUDA update validation failed.",
                await rejected.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
        }
        finally
        {
            configSqlite.Dispose();
            tracesSqlite.Dispose();
            tasksSqlite.Dispose();
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    private static WebApplication CreateApp(
        IConnectionMultiplexer redis,
        SqliteConnectionFactory configSqlite,
        SqliteConnectionFactory tracesSqlite,
        SqliteConnectionFactory tasksSqlite,
        string credentialPath,
        OnnxProviderDetector providers)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration["LUCIA_VALIDATION_CREDENTIAL_PATH"] =
            credentialPath;
        builder.Services.AddSingleton(redis);
        builder.Services.AddKeyedSingleton(
            SqliteDbNames.Config,
            configSqlite);
        builder.Services.AddKeyedSingleton(
            SqliteDbNames.Traces,
            tracesSqlite);
        builder.Services.AddKeyedSingleton(
            SqliteDbNames.Tasks,
            tasksSqlite);
        builder.Services.AddSingleton(providers);
        var app = builder.Build();
        app.MapApplianceUpdateValidation();
        return app;
    }

    private static HttpClient CreateClient(WebApplication app)
    {
        var addresses = app.Services
            .GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
            .Features
            .Get<IServerAddressesFeature>();
        var address = Assert.Single(addresses!.Addresses);
        return new HttpClient { BaseAddress = new Uri(address) };
    }

    private static SqliteConnectionFactory CreateSqlite(
        string root,
        string name,
        bool initializeConfiguration)
    {
        var factory = new SqliteConnectionFactory(
            Path.Combine(root, $"{name}.db"));
        if (!initializeConfiguration)
        {
            return factory;
        }

        using var connection = factory.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE configuration (
                key TEXT PRIMARY KEY,
                value TEXT,
                section TEXT,
                updated_at TEXT NOT NULL DEFAULT (datetime('now')),
                updated_by TEXT NOT NULL DEFAULT 'system',
                is_sensitive INTEGER NOT NULL DEFAULT 0
            );
            """;
        command.ExecuteNonQuery();
        return factory;
    }

    private static void AssertSentinel(
        SqliteConnectionFactory factory,
        string token)
    {
        using var connection = factory.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT token
            FROM appliance_update_validation
            WHERE id = 1;
            """;
        Assert.Equal(token, command.ExecuteScalar());
    }

    private static void DeleteSentinel(SqliteConnectionFactory factory)
    {
        using var connection = factory.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM appliance_update_validation;";
        command.ExecuteNonQuery();
    }

    private static void AssertSentinelMissing(
        SqliteConnectionFactory factory)
    {
        using var connection = factory.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(*)
            FROM appliance_update_validation;
            """;
        Assert.Equal(0L, command.ExecuteScalar());
    }

    private static void AssertConfigurationSentinelMissing(
        SqliteConnectionFactory factory)
    {
        using var connection = factory.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(*)
            FROM configuration
            WHERE key = 'appliance-update-validation';
            """;
        Assert.Equal(0L, command.ExecuteScalar());
    }
}
