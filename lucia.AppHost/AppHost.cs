using Aspire.Hosting;
using Aspire.Hosting.Azure;
using lucia.AppHost;
using Projects;
using Scalar.Aspire;

var builder = DistributedApplication.CreateBuilder(args);

// Azure AI Foundry — reference existing project
var existingFoundryName = builder.AddParameter("existingFoundryName");
var existingFoundryResourceGroup = builder.AddParameter("existingFoundryResourceGroup");

var foundry = builder.AddAzureAIFoundry("foundry")
    .AsExisting(existingFoundryName, existingFoundryResourceGroup);

// Primary models
var chatModel = foundry.AddDeployment("chat", AIFoundryModel.OpenAI.Gpt4o);
var embeddingsModel = foundry.AddDeployment("embeddings", AIFoundryModel.OpenAI.TextEmbedding3Large);

// Additional models for eval benchmarking
var chatMini = foundry.AddDeployment("chat-mini", AIFoundryModel.OpenAI.Gpt4oMini);
var phi4 = foundry.AddDeployment("phi4", AIFoundryModel.Microsoft.Phi4MiniInstruct);
var gptOss120b = foundry.AddDeployment("gpt-oss-120b", AIFoundryModel.OpenAI.GptOss120b);
var gpt5Nano = foundry.AddDeployment("gpt-5-nano", AIFoundryModel.OpenAI.Gpt5Nano);

var redis = builder.AddRedis("redis")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent)
    .WithRedisInsight()
    .WithContainerName("redis");

var mongodb = builder.AddMongoDB("mongodb")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent)
    .WithMongoExpress()
    .WithContainerName("mongodb");
var tracesDb = mongodb.AddDatabase("luciatraces");

var registryApi = builder.AddProject<Projects.lucia_AgentHost>("lucia-agenthost")
    .WithReference(embeddingsModel)
    .WithReference(chatModel)
    .WithReference(chatMini)
    .WithReference(phi4)
    .WithReference(gptOss120b)
    .WithReference(gpt5Nano)
    .WithReference(redis)
    .WaitFor(redis)
    .WithReference(tracesDb)
    .WaitFor(mongodb)
    .WithExternalHttpEndpoints();

var currentDirectory = Environment.CurrentDirectory;

builder.AddProject<Projects.lucia_A2AHost>("music-agent")
    .WithEnvironment("PluginDirectory", $"{currentDirectory}{Path.DirectorySeparatorChar.ToString()}plugins")
    .WithReference(embeddingsModel)
    .WithReference(chatModel)
    .WithReference(registryApi)
    .WaitFor(registryApi)
    .WithExternalHttpEndpoints();

builder.AddViteApp("lucia-dashboard", "../lucia-dashboard")
    .WithExternalHttpEndpoints()
    .WithNpm();

builder.Build().Run();
