using lucia.A2AHost.AgentRegistry;
using lucia.A2AHost.Extensions;
using lucia.A2AHost.Services;
using lucia.Agents.Configuration;
using lucia.Agents.Extensions;
using lucia.Agents.Services;
using lucia.HomeAssistant.Configuration;
using lucia.HomeAssistant.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddRedisClient(connectionName: "redis");

// MongoDB for shared configuration
builder.AddMongoDBClient(connectionName: "luciaconfig");

// MongoDB for trace capture (per-agent training data)
builder.AddMongoDBClient(connectionName: "luciatraces");
builder.Services.AddSingleton<lucia.Agents.Training.ITraceRepository, lucia.Agents.Training.MongoTraceRepository>();

// Add MongoDB configuration as highest-priority source (overrides appsettings)
builder.Configuration.AddMongoConfiguration("luciaconfig");

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

builder.AddChatClient("chat");
builder.AddEmbeddingsClient("embeddings");

builder.Services.Configure<HomeAssistantOptions>(
    builder.Configuration.GetSection("HomeAssistant"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddTransient<IHomeAssistantClient, HomeAssistantClient>();
builder.Services.AddSingleton<IDeviceCacheService, RedisDeviceCacheService>();

builder.Services.AddHttpClient<AgentRegistryClient>(options =>
{
    options.BaseAddress = new Uri("https://lucia-agenthost");
});

var pluginDir = builder.Configuration["PluginDirectory"] ?? "/app/plugins";
PluginLoader.LoadAgentPlugins(builder, pluginDir);

// Wrap the music agent's IChatClient with tracing after plugin registration
lucia.Agents.Extensions.ServiceCollectionExtensions.WrapAgentChatClientWithTracing(
    builder.Services,
    lucia.Agents.Orchestration.OrchestratorServiceKeys.MusicModel,
    "music-agent");

builder.Services.AddHostedService<AgentHostService>();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    app.UseHttpsRedirection();
}

app.MapScalarApiReference();
app.UseForwardedHeaders();

app.MapAgentPlugins();

app.Run();