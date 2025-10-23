using lucia_dotnet.APIs;
using lucia.Agents.Extensions;
using lucia.HomeAssistant.Extensions;
using Scalar.AspNetCore;
using Asp.Versioning;
using lucia.HomeAssistant.Configuration;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Hosting.A2A.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.AddServiceDefaults();

builder.AddRedisClient(connectionName: "redis");

builder.Services.Configure<HomeAssistantOptions>(
    builder.Configuration.GetSection("HomeAssistant"));


// A2A service registration is handled in AddLuciaAgents

// Add Lucia multi-agent system
var openAiApiKey = builder.Configuration["OpenAI:ApiKey"] ?? throw new InvalidOperationException("OpenAI:ApiKey is required");
builder.AddLuciaAgents();

builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
    options.ApiVersionReader = ApiVersionReader.Combine(
        new QueryStringApiVersionReader("api-version"),
        new HeaderApiVersionReader("api-version"),
        new UrlSegmentApiVersionReader()
    );
});

builder.Services.AddOpenApi();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.AddChatClient("chat-model");
builder.Services.AddAuthorization();
builder.Services.AddOutputCache();

// Add Home Assistant integration
builder.Services.AddHomeAssistant(options =>
{
    options.BaseUrl = builder.Configuration["HomeAssistant:BaseUrl"] ?? "http://homeassistant.local:8123";
    options.AccessToken = builder.Configuration["HomeAssistant:AccessToken"] ?? throw new InvalidOperationException("HomeAssistant:AccessToken is required");
    options.TimeoutSeconds = 60;
    options.ValidateSSL = false;
});


var app = builder.Build();

app.UseOutputCache();

app.MapOpenApi()
    .CacheOutput();

app.MapScalarApiReference();

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapAgentRegistryApiV1();

app.Run();