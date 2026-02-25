using A2A;
using lucia.A2AHost.AgentRegistry;
using lucia.A2AHost.Extensions;
using lucia.A2AHost.Services;
using lucia.Agents.Configuration;
using lucia.Agents.Extensions;
using lucia.Agents.Mcp;
using lucia.Agents.Services;
using lucia.HomeAssistant.Configuration;
using lucia.HomeAssistant.Services;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using lucia.Agents.Abstractions;
using lucia.Agents.Providers;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddRedisClient(connectionName: "redis");

// MongoDB for shared configuration
builder.AddMongoDBClient(connectionName: "luciaconfig");

// MongoDB for trace capture (per-agent training data)
builder.AddMongoDBClient(connectionName: "luciatraces");
builder.Services.AddSingleton<lucia.Agents.Training.ITraceRepository, lucia.Agents.Training.MongoTraceRepository>();

// MongoDB for task persistence (scheduled tasks, alarm clocks)
builder.AddMongoDBClient(connectionName: "luciatasks");
builder.Services.AddSingleton<lucia.Agents.Services.TracingChatClientFactory>();

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

// Chat and embedding clients are resolved at runtime from the Model Provider system
// (MongoDB-backed) via IChatClientResolver and IEmbeddingProviderResolver.
builder.Services.AddSingleton<IModelProviderRepository, MongoModelProviderRepository>();
builder.Services.AddSingleton<IModelProviderResolver, ModelProviderResolver>();
builder.Services.AddSingleton<IEmbeddingProviderResolver, EmbeddingProviderResolver>();
builder.Services.AddSingleton<IChatClientResolver, ChatClientResolver>();

// Register agent definition repository so plugins can load AgentDefinition for model resolution
builder.Services.AddSingleton<IAgentDefinitionRepository, MongoAgentDefinitionRepository>();

builder.Services.Configure<HomeAssistantOptions>(
    builder.Configuration.GetSection("HomeAssistant"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpClient<IHomeAssistantClient, HomeAssistantClient>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<HomeAssistantOptions>>().Value;
    if (!string.IsNullOrWhiteSpace(options.BaseUrl))
    {
        client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/'));
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {options.AccessToken}");
        client.DefaultRequestHeaders.Add("Accept", "application/json");
        client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
    }
})
.ConfigurePrimaryHttpMessageHandler(sp =>
{
    var options = sp.GetRequiredService<IOptions<HomeAssistantOptions>>().Value;
    var handler = new HttpClientHandler();

    if (!options.ValidateSSL)
    {
        handler.ServerCertificateCustomValidationCallback =
            (HttpRequestMessage message, X509Certificate2? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors) => true;
    }

    return handler;
});
builder.Services.AddSingleton<IDeviceCacheService, RedisDeviceCacheService>();
builder.Services.AddSingleton<IEmbeddingSimilarityService, EmbeddingSimilarityService>();
builder.Services.AddSingleton<IEntityLocationService, EntityLocationService>();

// Register Redis task store for A2A task persistence (used by TimerAgent)
builder.Services.AddSingleton<ITaskStore, RedisTaskStore>();

builder.Services.AddHttpClient<AgentRegistryClient>(options =>
{
    var registryUrl = builder.Configuration["services:registryApi"] ?? "https://lucia-agenthost";
    options.BaseAddress = new Uri(registryUrl);
});

var pluginDir = builder.Configuration["PluginDirectory"] ?? "/app/plugins";
PluginLoader.LoadAgentPlugins(builder, pluginDir);

builder.Services.AddHostedService<AgentHostService>();
builder.Services.AddProblemDetails();

// Health check that reports agent registration status as structured data and
// auto-re-registers if the registry was restarted. Uses a diagnostic-only tag
// so it doesn't affect the /health readiness endpoint used by Aspire/K8s.
builder.Services.AddHealthChecks()
    .AddCheck<AgentRegistrationHealthCheck>("agent-registration", tags: ["registration"]);

var app = builder.Build();

app.MapDefaultEndpoints();

// Separate diagnostic endpoint for registration status — not used by Aspire/K8s probes
app.MapHealthChecks("/health/registration", new HealthCheckOptions
{
    Predicate = r => r.Tags.Contains("registration")
});

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