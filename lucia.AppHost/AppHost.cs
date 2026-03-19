var builder = DistributedApplication.CreateBuilder(args);

// Read DataProvider config to conditionally include infrastructure containers
var dataProviderConfig = builder.Configuration.GetSection("DataProvider");
var cacheProvider = dataProviderConfig["Cache"] ?? "Redis";
var storeProvider = dataProviderConfig["Store"] ?? "MongoDB";
var useRedis = cacheProvider.Equals("Redis", StringComparison.OrdinalIgnoreCase);
var useMongo = storeProvider.Equals("MongoDB", StringComparison.OrdinalIgnoreCase);

IResourceBuilder<IResourceWithConnectionString>? redis = null;
if (useRedis)
{
    redis = builder.AddRedis("redis")
        .WithDataVolume()
        .WithLifetime(ContainerLifetime.Persistent)
        .WithRedisInsight()
        .WithPersistence()
        .WithContainerName("redis");
}

IResourceBuilder<MongoDBServerResource>? mongodb = null;
IResourceBuilder<MongoDBDatabaseResource>? tracesDb = null, configDb = null, tasksDb = null;
if (useMongo)
{
    mongodb = builder.AddMongoDB("mongodb")
        .WithImageTag("7.0")
        .WithDataVolume()
        .WithLifetime(ContainerLifetime.Persistent)
        .WithMongoExpress()
        .WithContainerName("mongodb");

    tracesDb = mongodb.AddDatabase("luciatraces");
    configDb = mongodb.AddDatabase("luciaconfig");
    tasksDb = mongodb.AddDatabase("luciatasks");
}

// Internal service-to-service authentication token.
// Aspire generates a random secret at startup and injects it into all services
// that need to communicate with the AgentHost registry endpoints.
var internalToken = builder.AddParameter("internal-api-token",
    new GenerateParameterDefault { MinLength = 32, Special = false }, secret: true, persist: true);

var registryApi = builder.AddProject<Projects.lucia_AgentHost>("lucia-agenthost")
    .WithEnvironment("Deployment__Mode", "standalone")
    .WithEnvironment("InternalAuth__Token", internalToken)
    // IDEs inject hot-reload hooks (DOTNET_STARTUP_HOOKS, DOTNET_MODIFIABLE_ASSEMBLIES)
    // that disable tiered JIT, causing 30-50x slowdowns. Clear all of them on the
    // AgentHost so it gets full JIT optimization. Debugging (breakpoints, stepping,
    // variable inspection) still works — only hot-reload is disabled.
    .WithEnvironment("DOTNET_MODIFIABLE_ASSEMBLIES", "")
    .WithEnvironment("COMPLUS_FORCEENC", "0")
    .WithEnvironment("DOTNET_STARTUP_HOOKS", "")
    .WithEnvironment("DOTNET_WATCH_HOTRELOAD_NAMEDPIPE_NAME", "")
    // Reduce OTEL export frequency — Aspire defaults (1s) add measurable per-request overhead
    .WithEnvironment("OTEL_BSP_SCHEDULE_DELAY", "5000")
    .WithEnvironment("OTEL_BLRP_SCHEDULE_DELAY", "5000")
    .WithEnvironment("OTEL_METRIC_EXPORT_INTERVAL", "5000")
    .WithHttpHealthCheck("/health")
    .WithEndpoint(port: 10400, name: "wyoming-tcp", scheme: "tcp", isProxied: false)
    .WithUrlForEndpoint("https", url =>
    {
        url.DisplayText = "Scalar (HTTPS)";
        url.Url = "/scalar";
    })
    .WithExternalHttpEndpoints();

// Conditionally wire infrastructure references
if (redis is not null)
    registryApi.WithReference(redis).WaitFor(redis);
if (tracesDb is not null)
    registryApi.WithReference(tracesDb);
if (configDb is not null)
    registryApi.WithReference(configDb);
if (tasksDb is not null)
    registryApi.WithReference(tasksDb);
if (mongodb is not null)
    registryApi.WaitFor(mongodb);

// Pass DataProvider config as environment variables
registryApi.WithEnvironment("DataProvider__Cache", cacheProvider);
registryApi.WithEnvironment("DataProvider__Store", storeProvider);

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
