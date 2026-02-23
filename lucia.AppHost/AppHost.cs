using Aspire.Hosting.Azure;

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
var phi4 = foundry.AddDeployment("phi4", AIFoundryModel.Microsoft.Phi4MiniInstruct);
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

// Internal service-to-service authentication token.
// Aspire generates a random secret at startup and injects it into all services
// that need to communicate with the AgentHost registry endpoints.
var internalToken = builder.AddParameter("internal-api-token",
    new GenerateParameterDefault { MinLength = 32, Special = false }, secret: true, persist: true);

var registryApi = builder.AddProject<Projects.lucia_AgentHost>("lucia-agenthost")
    .WithEnvironment("InternalAuth__Token", internalToken)
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
    .WithHttpHealthCheck("/health")
    .WithUrlForEndpoint("https", url =>
    {
        url.DisplayText = "Scalar (HTTPS)";
        url.Url = "/scalar";
    })
    .WithExternalHttpEndpoints();

var currentDirectory = Environment.CurrentDirectory;
var sep = Path.DirectorySeparatorChar.ToString();

var musicAgent = builder.AddProject<Projects.lucia_A2AHost>("music-agent")
    .WithEnvironment("PluginDirectory", $"{currentDirectory}{sep}plugins{sep}music-agent")
    .WithEnvironment("InternalAuth__Token", internalToken)
    .WithReference(embeddingsModel)
    .WithReference(chatModel)
    .WithReference(redis)
    .WaitFor(redis)
    .WithReference(registryApi)
    .WaitFor(registryApi)
    .WithReference(tracesDb)
    .WithReference(configDb)
    .WaitFor(mongodb)
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();
// Aspire service discovery uses the resource name as hostname — no port needed
musicAgent.WithEnvironment("services__selfUrl", "http://music-agent/music");

var timerAgent = builder.AddProject<Projects.lucia_A2AHost>("timer-agent")
    .WithEnvironment("PluginDirectory", $"{currentDirectory}{sep}plugins{sep}timer-agent")
    .WithEnvironment("InternalAuth__Token", internalToken)
    .WithReference(embeddingsModel)
    .WithReference(chatModel)
    .WithReference(redis)
    .WaitFor(redis)
    .WithReference(registryApi)
    .WaitFor(registryApi)
    .WithReference(tracesDb)
    .WithReference(configDb)
    .WaitFor(mongodb)
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();
// Aspire service discovery uses the resource name as hostname — no port needed
timerAgent.WithEnvironment("services__selfUrl", "http://timer-agent/timers");

// AgentHost needs service discovery for A2A agents so it can fetch their agent
// cards during registration. WithReference only adds endpoint resolution — it
// does NOT create a startup dependency (that's WaitFor), so no circular dependency.
registryApi
    .WithReference(musicAgent)
    .WithReference(timerAgent);

builder.AddViteApp("lucia-dashboard", "../lucia-dashboard")
    .WithReference(registryApi)
    .WithExternalHttpEndpoints()
    .WithNpm();

builder.Build().Run();
