using Azure.AI.OpenAI;
using lucia.AgentHost.Extensions;
using lucia.Agents.Extensions;
using lucia.Agents.Orchestration;
using lucia.Agents.Training;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Azure;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddAntiforgery();
builder.AddRedisClient(connectionName: "redis");

// MongoDB for trace capture
builder.AddMongoDBClient(connectionName: "luciatraces");

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

// Workaround: Aspire's AddKeyedAzureOpenAIClient registers a null non-keyed
// AzureOpenAIClient singleton (to suppress Azure SDK factory errors), which shadows
// the real registration from AddAzureOpenAIClient. Re-register using the Azure
// client factory so the non-keyed resolution chain works correctly.
builder.Services.AddSingleton(
    sp => sp.GetRequiredService<IAzureClientFactory<AzureOpenAIClient>>().CreateClient(string.Empty));

// Add Lucia multi-agent system
builder.AddLuciaAgents();

// Trace capture services
builder.Services.Configure<TraceCaptureOptions>(
    builder.Configuration.GetSection(TraceCaptureOptions.SectionName));
builder.Services.AddSingleton<ITraceRepository, MongoTraceRepository>();
builder.Services.AddSingleton<IOrchestratorObserver, TraceCaptureObserver>();
builder.Services.AddHostedService<TraceRetentionService>();

builder.Services.AddProblemDetails();

builder.Services.AddOpenApi();

// CORS for dashboard dev server
builder.Services.AddCors(options =>
{
    options.AddPolicy("Dashboard", policy =>
    {
        policy.WithOrigins("http://localhost:5173")
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

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}
else
{
    app.UseDeveloperExceptionPage();
    app.UseHttpsRedirection(); // prevents having to deal with cert issues from the server.
}
app.MapAgentRegistryApiV1();
app.MapAgentDiscovery();
app.MapTraceManagementApi();
app.MapDatasetExportApi();
app.MapDefaultEndpoints();

// SPA hosting: serve React dashboard assets in production
if (!app.Environment.IsDevelopment())
{
    app.UseStaticFiles();
    app.MapFallbackToFile("index.html");
}

app.Run();
