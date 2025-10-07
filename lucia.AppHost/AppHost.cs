using Aspire.Hosting.Azure;
using lucia.AppHost;
using Projects;
using Scalar.Aspire;

var builder = DistributedApplication.CreateBuilder(args);

var azOpenAiResource = builder.AddParameterFromConfiguration("AzureOpenAIName", "AzureOpenAI:Name");
var azOpenAiResourceGroup = builder.AddParameterFromConfiguration("AzureOpenAIResourceGroup", "AzureOpenAI:ResourceGroup");

var openAi = builder.AddAIModel("chat-model")
    .AsAzureOpenAI("gpt-5-mini", o => o.AsExisting(azOpenAiResource, azOpenAiResourceGroup));

var embeddings = builder.AddAIModel("embeddings-model")
    .AsAzureOpenAI("text-embedding-3-small", o => o.AsExisting(azOpenAiResource, azOpenAiResourceGroup));

var lucia = builder.AddProject<lucia_dotnet>("lucia-dotnet")
    .WithReference(embeddings)
    .WithReference(openAi)
    .WaitFor(embeddings)
    .WaitFor(openAi)
    .WithUrlForEndpoint("https", url =>
        {
            url.DisplayText = "Scalar (HTTPS)";
            url.Url = "/scalar";
        });

builder.AddProject<Projects.lucia_AgentHost>("lucia-agenthost")
    .WithReference(embeddings)
    .WithReference(openAi)
    .WaitFor(embeddings)
    .WaitFor(openAi);

builder.Build().Run();