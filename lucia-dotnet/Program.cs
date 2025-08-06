using lucia_dotnet.APIs;
using lucia.Agents.A2A.Services;
using lucia.Agents.Extensions;
using lucia.HomeAssistant.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.AddServiceDefaults();

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Add Home Assistant integration
builder.Services.AddHomeAssistant(options =>
{
    options.BaseUrl = builder.Configuration["HomeAssistant:BaseUrl"] ?? "http://homeassistant.local:8123";
    options.AccessToken = builder.Configuration["HomeAssistant:AccessToken"] ?? throw new InvalidOperationException("HomeAssistant:AccessToken is required");
    options.TimeoutSeconds = 30;
    options.ValidateSSL = false;
});

builder.Services.AddTransient<IA2AService, A2AService>();

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

app.UseHttpsRedirection();
app.UseCors();
app.UseAuthorization();

app.UseRouting();
app.MapAgentRegistryApiV1();

app.Run();