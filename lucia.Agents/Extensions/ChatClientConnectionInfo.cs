using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace lucia.Agents.Extensions;

public class ChatClientConnectionInfo
{
    public Uri? Endpoint { get; init; }
    public required string SelectedModel { get; init; }

    public ClientChatProvider Provider { get; init; }
    public string? AccessKey { get; init; }

    /// <summary>
    /// The AI Inference endpoint from Azure AI Foundry, if available.
    /// </summary>
    public Uri? EndpointAIInference { get; init; }

    // Example connection strings:
    // Custom:   Endpoint=https://localhost:4523;Model=phi3.5;AccessKey=1234;Provider=ollama;
    // Foundry:  Endpoint=https://xxx.cognitiveservices.azure.com/;EndpointAIInference=https://xxx.services.ai.azure.com/models;Deployment=chat
    public static bool TryParse(string? connectionString, [NotNullWhen(true)] out ChatClientConnectionInfo? settings)
    {
        if (string.IsNullOrEmpty(connectionString))
        {
            settings = null;
            return false;
        }

        var connectionBuilder = new DbConnectionStringBuilder
        {
            ConnectionString = connectionString
        };

        // Detect Azure AI Foundry format: has Deployment key (no Provider/Model)
        if (connectionBuilder.ContainsKey("Deployment"))
        {
            return TryParseFoundryFormat(connectionBuilder, out settings);
        }

        return TryParseCustomFormat(connectionBuilder, out settings);
    }

    private static bool TryParseFoundryFormat(
        DbConnectionStringBuilder connectionBuilder,
        [NotNullWhen(true)] out ChatClientConnectionInfo? settings)
    {
        Uri? endpoint = null;
        if (connectionBuilder.ContainsKey("Endpoint"))
        {
            Uri.TryCreate(connectionBuilder["Endpoint"].ToString(), UriKind.Absolute, out endpoint);
        }

        Uri? endpointAIInference = null;
        if (connectionBuilder.ContainsKey("EndpointAIInference"))
        {
            Uri.TryCreate(connectionBuilder["EndpointAIInference"].ToString(), UriKind.Absolute, out endpointAIInference);
        }

        var deployment = (string)connectionBuilder["Deployment"];

        if (endpoint is null || string.IsNullOrEmpty(deployment))
        {
            settings = null;
            return false;
        }

        string? accessKey = null;
        if (connectionBuilder.ContainsKey("Key"))
        {
            accessKey = (string)connectionBuilder["Key"];
        }

        settings = new ChatClientConnectionInfo
        {
            Endpoint = endpoint,
            EndpointAIInference = endpointAIInference,
            SelectedModel = deployment,
            AccessKey = accessKey,
            Provider = ClientChatProvider.AzureOpenAI
        };

        return true;
    }

    private static bool TryParseCustomFormat(
        DbConnectionStringBuilder connectionBuilder,
        [NotNullWhen(true)] out ChatClientConnectionInfo? settings)
    {
        Uri? endpoint = null;
        if (connectionBuilder.ContainsKey("Endpoint") && Uri.TryCreate(connectionBuilder["Endpoint"].ToString(), UriKind.Absolute, out endpoint))
        {
        }

        string? model = null;
        if (connectionBuilder.ContainsKey("Model"))
        {
            model = (string)connectionBuilder["Model"];
        }

        string? accessKey = null;
        if (connectionBuilder.ContainsKey("AccessKey"))
        {
            accessKey = (string)connectionBuilder["AccessKey"];
        }

        var provider = ClientChatProvider.Unknown;
        if (connectionBuilder.ContainsKey("Provider"))
        {
            var providerValue = (string)connectionBuilder["Provider"];
            Enum.TryParse(providerValue, ignoreCase: true, out provider);
        }

        if (endpoint is null && provider != ClientChatProvider.OpenAI || model is null || provider == ClientChatProvider.Unknown)
        {
            settings = null;
            return false;
        }

        settings = new ChatClientConnectionInfo
        {
            Endpoint = endpoint,
            SelectedModel = model,
            AccessKey = accessKey,
            Provider = provider
        };

        return true;
    }
}

public enum ClientChatProvider
{
    Unknown,
    Ollama,
    OpenAI,
    ONNX,
    AzureOpenAI,
    AzureAIInference,
}