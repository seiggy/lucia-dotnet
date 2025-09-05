using lucia_dotnet.APIs;
using lucia.Agents.Services;
using lucia.Agents.Extensions;
using lucia.HomeAssistant.Extensions;
using Scalar.AspNetCore;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.AddServiceDefaults();

builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ApiVersionReader = ApiVersionReader.Combine(
        new QueryStringApiVersionReader("api-version"),
        new HeaderApiVersionReader("api-version"),
        new UrlSegmentApiVersionReader()
    );
});
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddAuthorization();
builder.Services.AddOutputCache();

// Add Home Assistant integration
builder.Services.AddHomeAssistant(options =>
{
    options.BaseUrl = builder.Configuration["HomeAssistant:BaseUrl"] ?? "http://homeassistant.local:8123";
    options.AccessToken = builder.Configuration["HomeAssistant:AccessToken"] ?? throw new InvalidOperationException("HomeAssistant:AccessToken is required");
    options.TimeoutSeconds = 30;
    options.ValidateSSL = false;
});

// A2A service registration is handled in AddLuciaAgents

// Add Lucia multi-agent system
var openAiApiKey = builder.Configuration["OpenAI:ApiKey"] ?? throw new InvalidOperationException("OpenAI:ApiKey is required");
builder.Services.AddLuciaAgents(
    openAiApiKey: openAiApiKey,
    chatModelId: builder.Configuration["OpenAI:ChatModel"] ?? "gpt-4o",
    embeddingModelId: builder.Configuration["OpenAI:EmbeddingModel"] ?? "text-embedding-3-small",
    maxTokens: int.Parse(builder.Configuration["Lucia:MaxTokens"] ?? "8000")
);

var app = builder.Build();

app.UseOutputCache();

app.MapOpenApi()
    .CacheOutput();

app.MapScalarApiReference();

app.UseHttpsRedirection();

app.UseAuthorization();

app.UseRouting();
app.MapAgentRegistryApiV1();
app.MapA2AJsonRpcApiV1();

app.Run();