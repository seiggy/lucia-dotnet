using lucia.AgentHost;
using lucia.AgentHost.Extensions;
using lucia.Agents.Agents;
using lucia.Agents.Extensions;
using lucia.Agents.Skills;
using lucia.HomeAssistant.Configuration;
using Microsoft.Agents.AI.Hosting.A2A.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.Configure<HomeAssistantOptions>(
    builder.Configuration.GetSection("HomeAssistant"));

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.AddChatClient("chat-model");

// Add Lucia multi-agent system
builder.Services.AddLuciaAgents();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapAgentDiscovery();

app.Run();
