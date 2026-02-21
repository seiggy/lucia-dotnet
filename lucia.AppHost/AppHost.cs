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

// Primary models — default SkuCapacity is 1 (1K TPM), far too low for multi-agent orchestration
var chatModel = foundry.AddDeployment("chat", AIFoundryModel.OpenAI.Gpt4o)
    .WithProperties(d => d.SkuCapacity = 100);
var embeddingsModel = foundry.AddDeployment("embeddings", AIFoundryModel.OpenAI.TextEmbedding3Large)
    .WithProperties(d => d.SkuCapacity = 100);

// Additional models for eval benchmarking
var chatMini = foundry.AddDeployment("chat-mini", AIFoundryModel.OpenAI.Gpt4oMini)
    .WithProperties(d => d.SkuCapacity = 100);
var phi4 = foundry.AddDeployment("phi4", AIFoundryModel.Microsoft.Phi4MiniInstruct)
    .WithProperties(d => d.SkuCapacity = 100);
//var gptOss120b = foundry.AddDeployment("gpt-oss-120b", "gpt-oss-120b", "1", "OpenAI");
var gpt5Nano = foundry.AddDeployment("gpt-5-nano", AIFoundryModel.OpenAI.Gpt5Nano)
    .WithProperties(d => d.SkuCapacity = 100);

var redis = builder.AddRedis("redis")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent)
    .WithRedisInsight()
        .WithPersistence()
    .WithContainerName("redis");

var mongodb = builder.AddMongoDB("mongodb")
    .WithImageTag("7.0")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent)
    .WithMongoExpress()
    .WithContainerName("mongodb");
var tracesDb = mongodb.AddDatabase("luciatraces");
var configDb = mongodb.AddDatabase("luciaconfig");
var tasksDb = mongodb.AddDatabase("luciatasks");

var registryApi = builder.AddProject<Projects.lucia_AgentHost>("lucia-agenthost")
    .WithReference(embeddingsModel)
    .WithReference(chatModel)
    .WithReference(chatMini)
    .WithReference(phi4)
    //.WithReference(gptOss120b)
    .WithReference(gpt5Nano)
    .WithReference(redis)
    .WaitFor(redis)
    .WithReference(tracesDb)
    .WithReference(configDb)
    .WithReference(tasksDb)
    .WaitFor(mongodb)
    .WithUrlForEndpoint("https", url =>
    {
        url.DisplayText = "Scalar (HTTPS)";
        url.Url = "/scalar";
    })
    .WithExternalHttpEndpoints();

var currentDirectory = Environment.CurrentDirectory;
var sep = Path.DirectorySeparatorChar.ToString();

builder.AddProject<Projects.lucia_A2AHost>("music-agent")
    .WithEnvironment("PluginDirectory", $"{currentDirectory}{sep}plugins{sep}music-agent")
    .WithReference(embeddingsModel)
    .WithReference(chatModel)
    .WithReference(redis)
    .WaitFor(redis)
    .WithReference(registryApi)
    .WaitFor(registryApi)
    .WithReference(tracesDb)
    .WithReference(configDb)
    .WaitFor(mongodb)
    .WithExternalHttpEndpoints();

builder.AddProject<Projects.lucia_A2AHost>("timer-agent")
    .WithEnvironment("PluginDirectory", $"{currentDirectory}{sep}plugins{sep}timer-agent")
    .WithReference(embeddingsModel)
    .WithReference(chatModel)
    .WithReference(redis)
    .WaitFor(redis)
    .WithReference(registryApi)
    .WaitFor(registryApi)
    .WithReference(tracesDb)
    .WithReference(configDb)
    .WaitFor(mongodb)
    .WithExternalHttpEndpoints();

builder.AddViteApp("lucia-dashboard", "../lucia-dashboard")
    .WithReference(registryApi)
    .WithExternalHttpEndpoints()
    .WithNpm();

builder.Build().Run();
