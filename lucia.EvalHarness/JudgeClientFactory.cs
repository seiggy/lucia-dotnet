using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using lucia.EvalHarness.Configuration;
using Microsoft.Extensions.AI;

namespace lucia.EvalHarness;

public static class JudgeClientFactory
{
    public static IChatClient? Create(AzureOpenAIJudgeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var hasEndpoint = !string.IsNullOrWhiteSpace(settings.Endpoint);
        var hasApiKey = !string.IsNullOrWhiteSpace(settings.ApiKey);

        if (!hasEndpoint && !hasApiKey)
        {
            return null;
        }

        if (!hasEndpoint)
        {
            throw new InvalidOperationException(
                "AzureOpenAI.Endpoint is required when judge configuration is present.");
        }

        if (!Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out var endpoint))
        {
            throw new InvalidOperationException(
                "AzureOpenAI.Endpoint must be a valid absolute URI.");
        }

        if (string.IsNullOrWhiteSpace(settings.JudgeDeployment))
        {
            throw new InvalidOperationException(
                "AzureOpenAI.JudgeDeployment is required when judge configuration is present.");
        }

        var client = hasApiKey
            ? new AzureOpenAIClient(endpoint, new AzureKeyCredential(settings.ApiKey!))
            : new AzureOpenAIClient(endpoint, new AzureCliCredential());

        return client.GetChatClient(settings.JudgeDeployment).AsIChatClient();
    }
}
