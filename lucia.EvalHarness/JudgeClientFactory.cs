using System.ClientModel;
using System.ClientModel.Primitives;
using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using lucia.EvalHarness.Configuration;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Responses;

namespace lucia.EvalHarness;

public static class JudgeClientFactory
{
    public static IChatClient? Create(AzureOpenAIJudgeSettings settings) => Create(settings, null);

    internal static IChatClient? Create(AzureOpenAIJudgeSettings settings, HttpClient? httpClient)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var hasEndpoint = !string.IsNullOrWhiteSpace(settings.Endpoint);
        var hasApiKey = !string.IsNullOrWhiteSpace(settings.ApiKey);
        var hasDeployment = !string.IsNullOrWhiteSpace(settings.JudgeDeployment);

        if (!hasEndpoint && !hasApiKey && !hasDeployment)
        {
            return null;
        }

        if (!hasEndpoint)
        {
            throw new InvalidOperationException(
                "AzureOpenAI.Endpoint is required when judge configuration is present.");
        }

        if (!Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme is not ("https" or "http") ||
            !string.IsNullOrEmpty(endpoint.Query) ||
            !string.IsNullOrEmpty(endpoint.Fragment) ||
            !string.IsNullOrEmpty(endpoint.UserInfo) ||
            endpoint.AbsolutePath.TrimEnd('/') is not ("" or "/openai/v1"))
        {
            throw new InvalidOperationException(
                "AzureOpenAI.Endpoint must be an HTTP(S) resource URL or /openai/v1/ base URL, " +
                "without credentials, query, or fragment. Do not use a Foundry project URL.");
        }

        if (!hasDeployment)
        {
            throw new InvalidOperationException(
                "AzureOpenAI.JudgeDeployment is required when judge configuration is present.");
        }

        if (!settings.UseResponsesApi)
        {
            var legacyOptions = new AzureOpenAIClientOptions();
            if (httpClient is not null)
            {
                legacyOptions.Transport = new HttpClientPipelineTransport(httpClient);
            }

            var resourceEndpoint = new Uri(endpoint.GetLeftPart(UriPartial.Authority));
            var legacyClient = hasApiKey
                ? new AzureOpenAIClient(resourceEndpoint, new AzureKeyCredential(settings.ApiKey!), legacyOptions)
                : new AzureOpenAIClient(resourceEndpoint, new AzureCliCredential(), legacyOptions);

            return legacyClient.GetChatClient(settings.JudgeDeployment).AsIChatClient();
        }

        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(endpoint.GetLeftPart(UriPartial.Authority) + "/openai/v1/")
        };
        if (httpClient is not null)
        {
            clientOptions.Transport = new HttpClientPipelineTransport(httpClient);
        }

        // The installed Responses SDK and IChatClient adapter still require this opt-in.
#pragma warning disable OPENAI001
        var client = hasApiKey
            ? new ResponsesClient(new ApiKeyCredential(settings.ApiKey!), clientOptions)
            : new ResponsesClient(
                new BearerTokenPolicy(new AzureCliCredential(), "https://ai.azure.com/.default"),
                clientOptions);

        return client.AsIChatClient(settings.JudgeDeployment)
            .AsBuilder()
            .ConfigureOptions(options =>
            {
                // Reasoning models reject sampling overrides supplied by older evaluators.
                options.Temperature = null;
                options.TopP = null;
                options.RawRepresentationFactory = _ => new CreateResponseOptions
                {
                    StoredOutputEnabled = false
                };
            })
            .Build();
#pragma warning restore OPENAI001
    }
}
