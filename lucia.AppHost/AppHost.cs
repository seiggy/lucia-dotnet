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

builder.AddProject<Projects.lucia_AgentHost>("lucia-agenthost")
    .WithReference(embeddings)
    .WithReference(openAi)
    .WithReference(redis)
    .WaitFor(embeddings)
    .WaitFor(openAi)
    .WaitFor(redis)
    .WithUrlForEndpoint("https", url =>
    {
        url.DisplayText = "Scalar (HTTPS)";
        url.Url = "/scalar";
    })
    .WithExternalHttpEndpoints();

builder.Build().Run();
