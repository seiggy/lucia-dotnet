using System.Runtime.InteropServices;
using Microsoft.Extensions.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// JetBrains Toolbox launches Rider at nice 9 on Linux, and all child processes
// inherit that priority. Renice to 0 so the Aspire host and its children get
// normal CPU scheduling during development.
if (builder.Environment.IsDevelopment() && RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
{
    try
    {
        var pid = Environment.ProcessId;
        using var proc = System.Diagnostics.Process.Start("renice", $"-n 0 -p {pid}");
        proc?.WaitForExit(1000);
    }
    catch
    {
        // Best-effort — works without sudo on own processes
    }
}

var redis = builder.AddRedis("redis")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent)
    .WithRedisInsight()
        .WithPersistence()
    .WithContainerName("redis");

var mongodb = builder.AddMongoDB("mongodb")
    .WithImageTag("7.0")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent)
    .WithMongoExpress()
    .WithContainerName("mongodb");
var tracesDb = mongodb.AddDatabase("luciatraces");
var configDb = mongodb.AddDatabase("luciaconfig");
var tasksDb = mongodb.AddDatabase("luciatasks");

// Internal service-to-service authentication token.
// Aspire generates a random secret at startup and injects it into all services
// that need to communicate with the AgentHost registry endpoints.
var internalToken = builder.AddParameter("internal-api-token",
    new GenerateParameterDefault { MinLength = 32, Special = false }, secret: true, persist: true);

var registryApi = builder.AddProject<Projects.lucia_AgentHost>("lucia-agenthost")
    .WithEnvironment("Deployment__Mode", "standalone")
    .WithEnvironment("InternalAuth__Token", internalToken)
    // Reduce OTEL export frequency — Aspire defaults (1s) add measurable per-request overhead
    .WithEnvironment("OTEL_BSP_SCHEDULE_DELAY", "5000")
    .WithEnvironment("OTEL_BLRP_SCHEDULE_DELAY", "5000")
    .WithEnvironment("OTEL_METRIC_EXPORT_INTERVAL", "5000")
    .WithReference(redis)
    .WaitFor(redis)
    .WithReference(tracesDb)
    .WithReference(configDb)
    .WithReference(tasksDb)
    .WaitFor(mongodb)
    .WithHttpHealthCheck("/health")
    .WithEndpoint(port: 10400, name: "wyoming-tcp", scheme: "tcp", isProxied: false)
    .WithUrlForEndpoint("https", url =>
    {
        url.DisplayText = "Scalar (HTTPS)";
        url.Url = "/scalar";
    })
    .WithExternalHttpEndpoints();

var currentDirectory = Environment.CurrentDirectory;
var sep = Path.DirectorySeparatorChar.ToString();
//
// var timerAgent = builder.AddProject<Projects.lucia_A2AHost>("timer-agent")
//     .WithEnvironment("PluginDirectory", $"{currentDirectory}{sep}plugins{sep}timer-agent")
//     .WithEnvironment("InternalAuth__Token", internalToken)
//     .WithReference(redis)
//     .WaitFor(redis)
//     .WithReference(registryApi)
//     .WaitFor(registryApi)
//     .WithReference(tracesDb)
//     .WithReference(configDb)
//     .WithReference(tasksDb)
//     .WaitFor(mongodb)
//     .WithHttpHealthCheck("/health")
//     .WithExternalHttpEndpoints();
// Aspire service discovery uses the resource name as hostname — no port needed
//timerAgent.WithEnvironment("services__selfUrl", "http://timer-agent/timers");

// AgentHost needs service discovery for A2A agents so it can fetch their agent
// cards during registration. WithReference only adds endpoint resolution — it
// does NOT create a startup dependency (that's WaitFor), so no circular dependency.
// registryApi
//     .WithReference(timerAgent);

builder.AddViteApp("lucia-dashboard", "../lucia-dashboard")
    .WithReference(registryApi)
    .WaitFor(registryApi)
    .WithExternalHttpEndpoints()
    .WithNpm()
    .WithEndpoint("http", endpoint =>
    {
        endpoint.Port = 7233;
    });

builder.Build().Run();
