using DotNetEnv;

// Load the repo-root .env before CreateBuilder so the values land in
// IConfiguration via AddEnvironmentVariables(). TraversePath() walks up
// from the AppHost working directory until it finds a .env file.
// NoClobber() ensures real process-environment values always win over .env.
Env.NoClobber().TraversePath().Load();

var builder = DistributedApplication.CreateBuilder(args);

// Read DataProvider config to conditionally include infrastructure containers
var dataProviderConfig = builder.Configuration.GetSection("DataProvider");
var cacheProvider = dataProviderConfig["Cache"] ?? "Redis";
var storeProvider = dataProviderConfig["Store"] ?? "MongoDB";
var useRedis = cacheProvider.Equals("Redis", StringComparison.OrdinalIgnoreCase);
var useMongo = storeProvider.Equals("MongoDB", StringComparison.OrdinalIgnoreCase);
var usePostgres = storeProvider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase);

IResourceBuilder<IResourceWithConnectionString>? redis = null;
if (useRedis)
{
    redis = builder.AddRedis("redis")
        .WithDataVolume()
        .WithLifetime(ContainerLifetime.Persistent)
        .WithRedisInsight()
        .WithPersistence()
        .WithContainerName("redis")
        // Aspire 13 auto-enables TLS on the primary Redis endpoint when a dev cert is present,
        // which causes the built-in redis_check health check to fail with an EOF during the
        // TLS handshake (the AppHost-side ConnectionMultiplexer doesn't trust the Aspire dev cert).
        // Opting out of HTTPS certificate configuration reverts Redis to plaintext-only on its
        // primary endpoint so the health check can connect successfully. TLS is still available
        // in production via the infra/Helm Redis chart's own TLS configuration.
        .WithoutHttpsCertificate();
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

IResourceBuilder<PostgresServerResource>? postgres = null;
IResourceBuilder<PostgresDatabaseResource>? pgtracesDb = null, pgconfigDb = null, pgtasksDb = null;
if (usePostgres)
{
    postgres = builder.AddPostgres("postgres")
        .WithDataVolume()
        .WithLifetime(ContainerLifetime.Persistent)
        .WithPgAdmin()
        .WithContainerName("postgres");

    pgtracesDb = postgres.AddDatabase("luciatraces");
    pgconfigDb = postgres.AddDatabase("luciaconfig");
    pgtasksDb = postgres.AddDatabase("luciatasks");
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

// Forward .env seed vars to the AgentHost only when non-empty, so we never
// inject blank strings that could clobber real config values.
// Environment.GetEnvironmentVariable is used here (rather than builder.Configuration)
// to avoid the __ → : normalization that IConfiguration applies to double-underscore
// env var names, preserving the exact key names the AgentHost expects.
string[] seedEnvNames =
[
    "DASHBOARD_API_KEY",
    "LUCIA_HA_API_KEY",
    "HOMEASSISTANT__BASEURL",
    "HOMEASSISTANT__ACCESSTOKEN",
    "MUSICASSISTANT__INTEGRATIONID",
];
foreach (var envName in seedEnvNames)
{
    var value = Environment.GetEnvironmentVariable(envName);
    if (!string.IsNullOrEmpty(value))
        registryApi.WithEnvironment(envName, value);
}

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
if (postgres is not null && pgtracesDb is not null && pgconfigDb is not null && pgtasksDb is not null)
    registryApi
    .WithEnvironment("DataProvider__Store", "PostgreSQL")
    .WithReference(pgtracesDb)
    .WithReference(pgconfigDb)
    .WithReference(pgtasksDb)
    .WaitFor(postgres!);

// Pass DataProvider config as environment variables
registryApi.WithEnvironment("DataProvider__Cache", cacheProvider);
registryApi.WithEnvironment("DataProvider__Store", storeProvider);

var currentDirectory = Environment.CurrentDirectory;
var sep = Path.DirectorySeparatorChar.ToString();

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
