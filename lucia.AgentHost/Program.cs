using lucia.AgentHost.Auth;
using lucia.AgentHost.Extensions;
using lucia.Agents.Abstractions;
using lucia.Agents.Auth;
using lucia.Agents.Configuration;
using lucia.Agents.Extensions;
using lucia.Agents.Orchestration;
using lucia.Agents.Training;
using lucia.Agents.Services;
using lucia.MusicAgent;
using lucia.TimerAgent;
using lucia.TimerAgent.ScheduledTasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.HttpOverrides;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddAntiforgery();
builder.AddRedisClient(connectionName: "redis");

// Register prompt cache services
builder.Services.AddSingleton<IPromptCacheService, RedisPromptCacheService>();

// MongoDB for trace capture
builder.AddMongoDBClient(connectionName: "luciatraces");

// MongoDB for configuration (shared across services)
builder.AddMongoDBClient(connectionName: "luciaconfig");

// MongoDB for task archive
builder.AddMongoDBClient(connectionName: "luciatasks");

// Add MongoDB configuration as highest-priority source (overrides appsettings)
builder.Configuration.AddMongoConfiguration("luciaconfig");

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

// Deployment mode: "standalone" (default) embeds plugin agents in-process,
// "mesh" expects external A2A agent containers to register over the network.
builder.Services.Configure<DeploymentOptions>(
    builder.Configuration.GetSection(DeploymentOptions.SectionName));

var deploymentMode = builder.Configuration
    .GetValue<string>("Deployment:Mode") ?? "standalone";
var isStandalone = deploymentMode.Equals("standalone", StringComparison.OrdinalIgnoreCase);

if (isStandalone)
{
    var musicPlugin = new MusicAgentPlugin();
    musicPlugin.ConfigureAgentHost(builder);
    builder.Services.AddSingleton<IAgentPlugin>(musicPlugin);

    var timerPlugin = new TimerAgentPlugin();
    timerPlugin.ConfigureAgentHost(builder);
    builder.Services.AddSingleton<IAgentPlugin>(timerPlugin);
}
else
{
    // In mesh mode, plugin agents run in separate A2AHost processes. The AgentHost
    // still hosts the dashboard REST APIs (AlarmClockApi, etc.) that need data-access
    // services. Register only the repositories and query services — NOT the execution
    // services (ScheduledTaskService, BackgroundServices) which run in the agent process.
    builder.Services.AddSingleton<ScheduledTaskStore>();
    builder.Services.AddSingleton<CronScheduleService>();
    builder.Services.AddSingleton<IScheduledTaskRepository, MongoScheduledTaskRepository>();
    builder.Services.AddSingleton<IAlarmClockRepository, MongoAlarmClockRepository>();
}

// Trace capture services
builder.Services.Configure<TraceCaptureOptions>(
    builder.Configuration.GetSection(TraceCaptureOptions.SectionName));
builder.Services.AddSingleton<ITraceRepository, MongoTraceRepository>();
builder.Services.AddSingleton<LiveActivityChannel>();
builder.Services.AddSingleton<TraceCaptureObserver>();
builder.Services.AddSingleton<LiveActivityObserver>();
builder.Services.AddSingleton<IOrchestratorObserver>(sp =>
    new CompositeOrchestratorObserver([
        sp.GetRequiredService<TraceCaptureObserver>(),
        sp.GetRequiredService<LiveActivityObserver>(),
    ]));
builder.Services.AddHostedService<TraceRetentionService>();

// Task archive services
builder.Services.Configure<TaskArchiveOptions>(
    builder.Configuration.GetSection(TaskArchiveOptions.SectionName));
builder.Services.AddSingleton<ITaskArchiveStore, MongoTaskArchiveStore>();
builder.Services.AddHostedService<TaskArchivalService>();

// Configuration seeder — copies appsettings to MongoDB on first run
builder.Services.AddHostedService<ConfigSeeder>();

// API key authentication
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
builder.Services.AddSingleton<IApiKeyService, MongoApiKeyService>();
builder.Services.AddSingleton<ISessionService, HmacSessionService>();
builder.Services.AddSingleton<ConfigStoreWriter>();

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
app.MapTaskManagementApi();
app.MapMcpServerApi();
app.MapAgentDefinitionApi();
app.MapModelProviderApi();
app.MapActivityApi();
app.MapAlarmClockApi();
app.MapListsApi();
app.MapPresenceApi();
app.MapSkillOptimizerApi();
app.MapDefaultEndpoints();

// SPA hosting: serve React dashboard assets in production
if (!app.Environment.IsDevelopment())
{
    app.MapFallbackToFile("index.html");
}

app.Run();
