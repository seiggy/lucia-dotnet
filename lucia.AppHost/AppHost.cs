using Aspire.Hosting;
using Aspire.Hosting.Azure;
using lucia.AppHost;
using Projects;
using Scalar.Aspire;

var builder = DistributedApplication.CreateBuilder(args);

var openAi = builder.AddConnectionString("chat-model");
var embeddings = builder.AddConnectionString("embeddings-model");

var redis = builder.AddRedis("redis")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent)
    .WithRedisInsight()
    .WithContainerName("redis");

var registryApi = builder.AddProject<Projects.lucia_AgentHost>("lucia-agenthost")
    .WithReference(embeddings)
    .WithReference(openAi)
    .WithReference(redis)
    .WaitFor(embeddings)
    .WaitFor(openAi)
    .WaitFor(redis)
    .WithExternalHttpEndpoints();

var currentDirectory = Environment.CurrentDirectory;

builder.AddProject<Projects.lucia_A2AHost>("music-agent")
    .WithEnvironment("PluginDirectory", $"{currentDirectory}{Path.DirectorySeparatorChar.ToString()}plugins")
    .WithReference(embeddings)
    .WithReference(openAi)
    .WithReference(registryApi)
    .WaitFor(embeddings)
    .WaitFor(openAi)
    .WaitFor(registryApi)
    .WithExternalHttpEndpoints();

builder.Build().Run();
