using lucia.AgentHost.Auth;
using lucia.AgentHost.Extensions;
using lucia.Agents.Auth;
using lucia.Agents.Configuration;
using lucia.Agents.Extensions;
using lucia.Agents.Orchestration;
using lucia.Agents.Training;
using lucia.Agents.Services;
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
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.AddChatClient("chat");
builder.AddEmbeddingsClient("embeddings");

// Register additional model deployments as keyed IChatClient services
builder.AddKeyedChatClient("phi4");
builder.AddKeyedChatClient("gpt-5-nano");

// Add Lucia multi-agent system
builder.AddLuciaAgents();

// Trace capture services
builder.Services.Configure<TraceCaptureOptions>(
    builder.Configuration.GetSection(TraceCaptureOptions.SectionName));
builder.Services.AddSingleton<ITraceRepository, MongoTraceRepository>();
builder.Services.AddSingleton<IOrchestratorObserver, TraceCaptureObserver>();
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
app.MapTraceManagementApi();
app.MapDatasetExportApi();
app.MapConfigurationApi();
app.MapPromptCacheApi();
app.MapTaskManagementApi();
app.MapMcpServerApi();
app.MapAgentDefinitionApi();
app.MapDefaultEndpoints();

// SPA hosting: serve React dashboard assets in production
if (!app.Environment.IsDevelopment())
{
    app.MapFallbackToFile("index.html");
}

app.Run();
