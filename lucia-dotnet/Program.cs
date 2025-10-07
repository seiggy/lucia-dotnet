using lucia_dotnet.APIs;
using lucia.Agents.Extensions;
using lucia.HomeAssistant.Extensions;
using Scalar.AspNetCore;
using Asp.Versioning;
using lucia.HomeAssistant.Configuration;
using Microsoft.Agents.AI.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.AddServiceDefaults();

builder.Services.Configure<HomeAssistantOptions>(
    builder.Configuration.GetSection("HomeAssistant"));

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
builder.AddChatClient("chat-model");
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
builder.Services.AddLuciaAgents();

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