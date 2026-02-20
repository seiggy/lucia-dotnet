using lucia.AgentHost.Extensions;
using lucia.Agents.Configuration;
using lucia.Agents.Extensions;
using lucia.Agents.Orchestration;
using lucia.Agents.Training;
using lucia.HomeAssistant.Configuration;
using lucia.Agents.Services;
using lucia.HomeAssistant.Services;
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

// Home Assistant client
builder.Services.Configure<HomeAssistantOptions>(
    builder.Configuration.GetSection(HomeAssistantOptions.SectionName));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddTransient<IHomeAssistantClient, HomeAssistantClient>();

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

app.UseHttpsRedirection();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}
else
{
    app.UseDeveloperExceptionPage();
}
app.MapAgentRegistryApiV1();
app.MapAgentProxyApi();
app.MapAgentDiscovery();
app.MapTraceManagementApi();
app.MapDatasetExportApi();
app.MapConfigurationApi();
app.MapPromptCacheApi();
app.MapTaskManagementApi();
app.MapDefaultEndpoints();

// SPA hosting: serve React dashboard assets in production
if (!app.Environment.IsDevelopment())
{
    app.MapFallbackToFile("index.html");
}

app.Run();
