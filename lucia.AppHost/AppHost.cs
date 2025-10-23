using Aspire.Hosting.Azure;
using lucia.AppHost;
using Projects;
using Scalar.Aspire;

var builder = DistributedApplication.CreateBuilder(args);

var azOpenAiResource = builder.AddParameterFromConfiguration("AzureOpenAIName", "AzureOpenAI:Name");
var azOpenAiResourceGroup = builder.AddParameterFromConfiguration("AzureOpenAIResourceGroup", "AzureOpenAI:ResourceGroup");

var openAi = builder.AddAIModel("chat-model")
    .AsAzureOpenAI("gpt-4.1-nano", o => o.AsExisting(azOpenAiResource, azOpenAiResourceGroup));

var embeddings = builder.AddAIModel("embeddings-model")
    .AsAzureOpenAI("text-embedding-3-small", o => o.AsExisting(azOpenAiResource, azOpenAiResourceGroup));

var redis = builder.AddRedis("redis")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent)
    .WithRedisInsight()
    .WithContainerName("redis");

var lucia = builder.AddProject<lucia_dotnet>("lucia-dotnet")
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
