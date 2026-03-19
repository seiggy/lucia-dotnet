using lucia.AgentHost;
using lucia.AgentHost.Apis;
using lucia.AgentHost.Auth;
using lucia.AgentHost.Conversation;
using lucia.AgentHost.Conversation.Execution;
using lucia.AgentHost.Conversation.Templates;
using lucia.AgentHost.Extensions;
using lucia.AgentHost.PluginFramework;
using lucia.AgentHost.Services;
using lucia.Agents.Abstractions;
using lucia.Agents.Auth;
using lucia.Agents.Configuration;
using lucia.Agents.Extensions;
using lucia.Agents.Integration;
using lucia.Agents.Orchestration;
using lucia.Agents.PluginFramework;
using lucia.Agents.Training;
using lucia.Agents.Services;
using lucia.Data;
using lucia.Data.Extensions;
using lucia.Data.Sqlite;
using lucia.MusicAgent;
using lucia.TimerAgent;
using lucia.TimerAgent.ScheduledTasks;
using lucia.Wyoming.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.HttpOverrides;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddAntiforgery();

// Determine data provider backends
var dataProviderOptions = new DataProviderOptions();
builder.Configuration.GetSection(DataProviderOptions.SectionName).Bind(dataProviderOptions);
var useRedis = dataProviderOptions.Cache == CacheProviderType.Redis;
var useMongo = dataProviderOptions.Store == StoreProviderType.MongoDB;

if (useRedis)
{
    builder.AddRedisClient(connectionName: "redis");
    builder.Services.AddSingleton<IPromptCacheService, RedisPromptCacheService>();
}

if (useMongo)
{
    // MongoDB for trace capture
    builder.AddMongoDBClient(connectionName: "luciatraces");

    // MongoDB for configuration (shared across services)
    builder.AddMongoDBClient(connectionName: "luciaconfig");

    // MongoDB for task archive
    builder.AddMongoDBClient(connectionName: "luciatasks");

    // Add MongoDB configuration as highest-priority source (overrides appsettings)
    builder.Configuration.AddMongoConfiguration("luciaconfig");
}
else
{
    // SQLite configuration provider (replaces MongoDB config source)
    var sqliteFactory = new SqliteConnectionFactory(dataProviderOptions.SqlitePath);
    builder.Services.AddSingleton(sqliteFactory);
    builder.Configuration.AddSqliteConfiguration(sqliteFactory);
}

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

// Add services to the container.
// Chat and embedding clients are resolved at runtime from the Model Provider system
// (MongoDB-backed) via IChatClientResolver and IEmbeddingProviderResolver.

// Add Lucia multi-agent system
builder.AddLuciaAgents();

// Register lightweight data providers when not using Redis/MongoDB
if (!useRedis)
    builder.AddInMemoryCacheProviders();
if (!useMongo)
    builder.AddSqliteStoreProviders();

// Deployment mode: "standalone" (default) embeds plugin agents in-process,
// "mesh" expects external A2A agent containers to register over the network.
builder.Services.Configure<DeploymentOptions>(
    builder.Configuration.GetSection(DeploymentOptions.SectionName));

var deploymentMode = builder.Configuration
    .GetValue<string>("Deployment:Mode") ?? "standalone";
var isStandalone = deploymentMode.Equals("standalone", StringComparison.OrdinalIgnoreCase);

// Music agent always runs in-process with AgentHost
var musicPlugin = new MusicAgentPlugin();
musicPlugin.ConfigureAgentHost(builder);
builder.Services.AddSingleton<IAgentPlugin>(musicPlugin);
builder.Services.AddSingleton<AgentHostTelemetrySource>();

if (isStandalone)
{
    var timerPlugin = new TimerAgentPlugin();
    timerPlugin.ConfigureAgentHost(builder);
    builder.Services.AddSingleton<IAgentPlugin>(timerPlugin);

    if (!useMongo)
    {
        builder.Services.AddSingleton<IScheduledTaskRepository, SqliteScheduledTaskRepository>();
        builder.Services.AddSingleton<IAlarmClockRepository, SqliteAlarmClockRepository>();
    }
}
else
{
    // In mesh mode, plugin agents run in separate A2AHost processes. The AgentHost
    // still hosts the dashboard REST APIs (AlarmClockApi, etc.) that need data-access
    // services. Register only the repositories and query services — NOT the execution
    // services (ScheduledTaskService, BackgroundServices) which run in the agent process.
    builder.Services.AddSingleton<ScheduledTaskStore>();
    builder.Services.AddSingleton<CronScheduleService>();
    if (useMongo)
    {
        builder.Services.AddSingleton<IScheduledTaskRepository, MongoScheduledTaskRepository>();
        builder.Services.AddSingleton<IAlarmClockRepository, MongoAlarmClockRepository>();
    }
    else
    {
        builder.Services.AddSingleton<IScheduledTaskRepository, SqliteScheduledTaskRepository>();
        builder.Services.AddSingleton<IAlarmClockRepository, SqliteAlarmClockRepository>();
    }
}

// Plugin directory configuration
var pluginDir = builder.Configuration["PluginDirectory"]
    ?? Path.Combine(AppContext.BaseDirectory, "plugins");

// Plugin change tracking (restart banner)
builder.Services.AddSingleton<PluginChangeTracker>();

// Plugin repository sources (local for dev, git for production)
builder.Services.AddSingleton<IPluginRepositorySource, LocalPluginRepositorySource>();
builder.Services.AddSingleton<IPluginRepositorySource, GitPluginRepositorySource>();

// Plugin management (repository CRUD, install, enable/disable)
if (useMongo)
{
    builder.Services.AddSingleton<IPluginManagementRepository, MongoPluginManagementRepository>();
}
builder.Services.AddSingleton(sp =>
{
    return new PluginManagementService(
        sp.GetRequiredService<IPluginManagementRepository>(),
        sp.GetRequiredService<PluginChangeTracker>(),
        sp.GetServices<IPluginRepositorySource>(),
        sp.GetRequiredService<ILogger<PluginManagementService>>(),
        pluginDir);
});

// Auto-discover ILuciaPlugin implementations (scripts + legacy DLLs)
using var earlyLoggerFactory = LoggerFactory.Create(b => b.AddConsole());
var pluginLogger = earlyLoggerFactory.CreateLogger("lucia.PluginLoader");
var luciaPlugins = await PluginLoader.LoadPluginsAsync(pluginDir, pluginLogger)
    .ConfigureAwait(false);

// Register discovered plugins in DI so AgentInitializationService can invoke OnSystemReadyAsync
foreach (var plugin in luciaPlugins)
{
    plugin.ConfigureServices(builder);
    builder.Services.AddSingleton<ILuciaPlugin>(plugin);
}

// Wyoming voice protocol server (Phase 1)
builder.AddWyomingServer();

// Trace capture services
builder.Services.Configure<TraceCaptureOptions>(
    builder.Configuration.GetSection(TraceCaptureOptions.SectionName));
if (useMongo)
{
    builder.Services.AddSingleton<ITraceRepository, MongoTraceRepository>();
}
else
{
    builder.Services.AddSingleton<ITraceRepository, lucia.Data.Sqlite.SqliteTraceRepository>();
}
builder.Services.AddSingleton<LiveActivityChannel>();
builder.Services.AddSingleton<SpanCollectorProcessor>();
builder.Services.AddSingleton<ISpanCollector>(sp => sp.GetRequiredService<SpanCollectorProcessor>());
builder.Services.AddSingleton<TraceCaptureObserver>();
builder.Services.AddSingleton<LiveActivityObserver>();
builder.Services.AddSingleton<IOrchestratorObserver>(sp =>
    new CompositeOrchestratorObserver([
        sp.GetRequiredService<TraceCaptureObserver>(),
        sp.GetRequiredService<LiveActivityObserver>(),
    ]));
builder.Services.AddHostedService<TraceRetentionService>();

// Conversation fast-path command processing
if (useMongo)
{
    builder.Services.AddSingleton<IResponseTemplateRepository, MongoResponseTemplateRepository>();
}
else
{
    builder.Services.AddSingleton<IResponseTemplateRepository, SqliteResponseTemplateRepository>();
}
builder.Services.AddSingleton<ResponseTemplateRenderer>();
builder.Services.AddSingleton<IDirectSkillExecutor, DirectSkillExecutor>();
builder.Services.AddSingleton<ContextReconstructor>();
builder.Services.AddSingleton<ConversationTelemetry>();
builder.Services.AddSingleton<ConversationCommandProcessor>();

// Register span collector as an OTEL processor so captured Lucia.* spans
// can be attached to conversation traces for the waterfall timeline.
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddProcessor<SpanCollectorProcessor>());

// Task archive services
builder.Services.Configure<TaskArchiveOptions>(
    builder.Configuration.GetSection(TaskArchiveOptions.SectionName));
if (useMongo)
{
    builder.Services.AddSingleton<ITaskArchiveStore, MongoTaskArchiveStore>();
}
// Note: SQLite ITaskArchiveStore is registered by AddSqliteStoreProviders()
builder.Services.AddHostedService<TaskArchivalService>();

// Configuration seeder — copies appsettings to MongoDB on first run
builder.Services.AddHostedService<ConfigSeeder>();

// API key authentication
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
if (useMongo)
{
    builder.Services.AddSingleton<MongoApiKeyService>();
    builder.Services.AddSingleton<IApiKeyService>(sp =>
        new CachedApiKeyService(
            sp.GetRequiredService<MongoApiKeyService>(),
            sp.GetRequiredService<ILogger<CachedApiKeyService>>()));
    builder.Services.AddSingleton<IConfigStoreWriter, ConfigStoreWriter>();
}
// Note: SQLite IApiKeyService and IConfigStoreWriter registered by AddSqliteStoreProviders()
builder.Services.AddSingleton<ISessionService, HmacSessionService>();

// Bind internal token options (injected by Aspire/K8s as env var InternalAuth__Token)
builder.Services.Configure<InternalTokenOptions>(
    builder.Configuration.GetSection(InternalTokenOptions.SectionName));

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = "MultiScheme";
        options.DefaultChallengeScheme = "MultiScheme";
    })
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
        AuthOptions.AuthenticationScheme, _ => { })
    .AddScheme<AuthenticationSchemeOptions, InternalTokenAuthenticationHandler>(
        InternalTokenDefaults.AuthenticationScheme, _ => { })
    .AddPolicyScheme("MultiScheme", "API Key or Internal Token", options =>
    {
        // Route to the correct handler based on the request
        options.ForwardDefaultSelector = context =>
        {
            // Bearer token → internal token handler
            var authHeader = context.Request.Headers.Authorization.ToString();
            if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return InternalTokenDefaults.AuthenticationScheme;
            }

            // Everything else → API key handler (which also checks session cookies)
            return AuthOptions.AuthenticationScheme;
        };
    });

builder.Services.AddAuthorization(options =>
{
    // Internal-only: only platform-injected token (agent → registry)
    options.AddPolicy("InternalOnly", policy =>
        policy.AddAuthenticationSchemes(InternalTokenDefaults.AuthenticationScheme)
              .RequireAuthenticatedUser());

    // External or internal: either API key/session OR internal token
    options.AddPolicy("ExternalOrInternal", policy =>
        policy.AddAuthenticationSchemes(
                AuthOptions.AuthenticationScheme,
                InternalTokenDefaults.AuthenticationScheme)
              .RequireAuthenticatedUser());
});

builder.Services.AddHttpClient("AgentProxy");
builder.Services.AddHttpClient("OllamaModels", client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddHttpClient("ProviderModelCatalog", client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.AddSingleton<ProviderModelCatalogService>();

// Skill optimizer job manager
builder.Services.AddSingleton<SkillOptimizerJobManager>();

builder.Services.AddProblemDetails();

builder.Services.AddOpenApi();

// CORS for dashboard dev server
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:5173"];
builder.Services.AddCors(options =>
{
    options.AddPolicy("Dashboard", policy =>
    {
        policy.WithOrigins(corsOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

// When using SQLite, run schema migrations before any data access (seed, config load, etc.)
if (!useMongo)
{
    await using var migrationScope = app.Services.CreateAsyncScope();
    var migrationRunner = migrationScope.ServiceProvider.GetRequiredService<lucia.Data.Sqlite.SqliteMigrationRunner>();
    await migrationRunner.StartAsync(CancellationToken.None).ConfigureAwait(false);
}

// Headless seed: run before app accepts requests so env-based setup is in the config store
// before OnboardingMiddleware or config provider are first read
await using (var seedScope = app.Services.CreateAsyncScope())
{
    var apiKeyService = seedScope.ServiceProvider.GetRequiredService<IApiKeyService>();
    var configStore = seedScope.ServiceProvider.GetRequiredService<IConfigStoreWriter>();
    var config = seedScope.ServiceProvider.GetRequiredService<IConfiguration>();
    var seedLogger = seedScope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Lucia.HeadlessSeed");
    await apiKeyService.SeedSetupFromEnvAsync(configStore, config, seedLogger, CancellationToken.None).ConfigureAwait(false);
}

app.MapOpenApi()
    .CacheOutput();

app.MapScalarApiReference();

app.UseForwardedHeaders();
app.UseAntiforgery();
app.UseCors("Dashboard");
app.UseStaticFiles();

// Onboarding: redirect to /setup if first-run
app.UseMiddleware<OnboardingMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}
else
{
    app.UseDeveloperExceptionPage();
}
app.MapAuthApi();
app.MapSetupApi();
app.MapApiKeyManagementApi();
app.MapAgentRegistryApiV1();
app.MapAgentProxyApi();
app.MapAgentDiscovery();

// In standalone mode, map plugin agent A2A endpoints in-process
if (isStandalone)
{
    foreach (var plugin in app.Services.GetServices<IAgentPlugin>())
        plugin.MapAgentEndpoints(app);
}

app.MapTraceManagementApi();
app.MapDatasetExportApi();
app.MapConfigurationApi();
app.MapPromptCacheApi();
app.MapEntityLocationCacheApi();
app.MapEntityVisibilityApi();
app.MapMatcherDebugApi();
app.MapTaskManagementApi();
app.MapMcpServerApi();
app.MapAgentDefinitionApi();
app.MapModelProviderApi();
app.MapActivityApi();
app.MapAlarmClockApi();
app.MapResponseTemplateApi();
app.MapConversationApi();
app.MapListsApi();
app.MapPresenceApi();
app.MapSkillOptimizerApi();
app.MapPluginRepositoryApi();
app.MapPluginStoreApi();
app.MapInstalledPluginApi();
app.MapBackgroundTaskEndpoints();
app.MapWyomingModelEndpoints();
app.MapWyomingStatusEndpoints();
app.MapWyomingSessionEndpoints();
app.MapTranscriptHistoryEndpoints();
app.MapVoiceConfigEndpoints();
app.MapOnboardingEndpoints();
app.MapVoiceClipEndpoints();
app.MapSystemApi();
app.MapDefaultEndpoints();

// Bootstrap plugin repository into MongoDB
var pluginMgmt = app.Services.GetRequiredService<PluginManagementService>();
if (app.Environment.IsDevelopment())
{
    var repoRoot = builder.Configuration["PluginRegistryPath"] is { } regPath
        ? Path.GetDirectoryName(Path.GetFullPath(regPath))!
        : AppContext.BaseDirectory;

    await pluginMgmt.EnsureRepositoryAsync(new PluginRepositoryDefinition
    {
        Id = "local-dev",
        Name = "Local Development",
        Type = "local",
        Url = repoRoot,
        ManifestPath = "lucia-plugins.json",
        Enabled = true,
    }).ConfigureAwait(false);
}
else
{
    await pluginMgmt.EnsureRepositoryAsync(new PluginRepositoryDefinition
    {
        Id = "lucia-official",
        Name = "Lucia Official Plugins",
        Type = "git",
        Url = "https://github.com/seiggy/lucia-dotnet",
        Branch = "master",
        BlobSource = "release",
        ManifestPath = "lucia-plugins.json",
        Enabled = true,
    }).ConfigureAwait(false);
}

// Let auto-discovered plugins run their startup logic
foreach (var plugin in luciaPlugins)
    await plugin.ExecuteAsync(app.Services)
        .ConfigureAwait(false);

// Let auto-discovered plugins map their endpoints
foreach (var plugin in luciaPlugins)
    plugin.MapEndpoints(app);

// SPA hosting: serve React dashboard assets in production
if (!app.Environment.IsDevelopment())
{
    app.MapFallbackToFile("index.html");
}

app.Run();
