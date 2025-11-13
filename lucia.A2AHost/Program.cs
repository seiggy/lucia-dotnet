using lucia.A2AHost.AgentRegistry;
using lucia.A2AHost.Extensions;
using lucia.A2AHost.Services;
using lucia.Agents.Extensions;
using lucia.HomeAssistant.Configuration;
using lucia.HomeAssistant.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

builder.AddChatClient("chat-model");
builder.AddEmbeddingsClient("embeddings-model");

builder.Services.Configure<HomeAssistantOptions>(
    builder.Configuration.GetSection("HomeAssistant"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddTransient<IHomeAssistantClient, GeneratedHomeAssistantClient>();

builder.Services.AddHttpClient<AgentRegistryClient>(options =>
{
    options.BaseAddress = new Uri("https://lucia-agenthost");
});

var pluginDir = builder.Configuration["PluginDirectory"] ?? "/app/plugins";
PluginLoader.LoadAgentPlugins(builder, pluginDir);

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