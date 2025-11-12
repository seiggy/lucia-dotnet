using lucia.AgentHost.Extensions;
using lucia.Agents.Extensions;
using Microsoft.AspNetCore.HttpOverrides;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddRedisClient(connectionName: "redis");


builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.AddChatClient("chat-model");

// Add Lucia multi-agent system
builder.AddLuciaAgents();

builder.Services.AddProblemDetails();

builder.Services.AddOpenApi();

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapOpenApi()
    .CacheOutput();

app.MapScalarApiReference();

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}
else
{
    app.UseDeveloperExceptionPage();
    //app.UseHttpsRedirection(); // prevents having to deal with cert issues from the server.
}
app.MapAgentDiscovery();

app.Run();
