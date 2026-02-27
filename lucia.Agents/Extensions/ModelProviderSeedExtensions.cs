using lucia.Agents.Abstractions;
using lucia.Agents.Configuration;
using lucia.Agents.Mcp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Extensions;

/// <summary>
/// Seeds <see cref="ModelProvider"/> documents from environment/connection-string
/// configuration when upgrading from a version that didn't use the provider system.
/// Checks for each built-in provider by ID so it works regardless of whether the
/// user has already created their own providers.
/// </summary>
public static class ModelProviderSeedExtensions
{
    private const string DefaultChatProviderId = "default-chat";
    private const string DefaultChatProviderName = "Default Chat Model";
    private const string DefaultEmbeddingProviderId = "default-embeddings";
    private const string DefaultEmbeddingProviderName = "Default Embedding Model";

    /// <summary>
    /// Seeds built-in providers from environment/connection-string config.
    /// Runs when setup is complete, or when ConnectionStrings__chat-model exists (headless/Docker).
    /// </summary>
    public static async Task SeedDefaultModelProvidersAsync(
        this IModelProviderRepository repository,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken ct = default)
    {
        var setupComplete = string.Equals(configuration["Auth:SetupComplete"], "true", StringComparison.OrdinalIgnoreCase);
        var hasChatConnection = !string.IsNullOrWhiteSpace(configuration.GetConnectionString("chat-model"))
            || !string.IsNullOrWhiteSpace(configuration.GetConnectionString("chat"));
        if (!setupComplete && !hasChatConnection)
        {
            logger.LogDebug("Setup not complete and no chat-model connection string — skipping model provider seed.");
            return;
        }

        // Seed each built-in provider independently — check by ID so existing
        // user-created providers don't block seeding of the defaults.
        await SeedProviderFromConnectionStringAsync(
            repository, configuration, logger,
            connectionName: "chat-model", // matches ConnectionStrings__chat-model (Docker/env standard)
            providerId: DefaultChatProviderId,
            providerName: DefaultChatProviderName,
            purpose: ModelPurpose.Chat,
            ct).ConfigureAwait(false);

        await SeedProviderFromConnectionStringAsync(
            repository, configuration, logger,
            connectionName: "embeddings",
            providerId: DefaultEmbeddingProviderId,
            providerName: DefaultEmbeddingProviderName,
            purpose: ModelPurpose.Embedding,
            ct).ConfigureAwait(false);
    }

    private static async Task SeedProviderFromConnectionStringAsync(
        IModelProviderRepository repository,
        IConfiguration configuration,
        ILogger logger,
        string connectionName,
        string providerId,
        string providerName,
        ModelPurpose purpose,
        CancellationToken ct)
    {
        // Check if this specific built-in provider already exists
        var existing = await repository.GetProviderAsync(providerId, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            logger.LogDebug("Built-in provider '{Id}' already exists — skipping seed.", providerId);
            return;
        }

        // Prefer "chat-model" (deployment standard); fallback to "chat" for legacy configs
        var connectionString = configuration.GetConnectionString(connectionName)
            ?? (connectionName == "chat-model" ? configuration.GetConnectionString("chat") : null);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            logger.LogWarning(
                "No '{ConnectionName}' connection string found — cannot seed built-in provider '{Id}'.",
                connectionName, providerId);
            return;
        }

        ChatClientConnectionInfo? info = null;
        if (!ChatClientConnectionInfo.TryParse(connectionString, out info))
        {
            // Fallback: try simple Ollama format for common Docker env (Endpoint=...;Model=...;Provider=ollama)
            info = TryParseOllamaFallback(connectionString);
        }

        if (info is null)
        {
            logger.LogWarning(
                "Could not parse '{ConnectionName}' connection string — cannot seed built-in provider '{Id}'.",
                connectionName, providerId);
            return;
        }

        var providerType = info.Provider switch
        {
            ClientChatProvider.Ollama => ProviderType.Ollama,
            ClientChatProvider.OpenAI => ProviderType.OpenAI,
            ClientChatProvider.AzureOpenAI => ProviderType.AzureOpenAI,
            ClientChatProvider.AzureAIInference => ProviderType.AzureAIInference,
            _ => (ProviderType?)null
        };

        if (providerType is null)
        {
            logger.LogWarning(
                "Unsupported provider type '{Provider}' in '{ConnectionName}' connection string — skipping seed.",
                info.Provider, connectionName);
            return;
        }

        var provider = new ModelProvider
        {
            Id = providerId,
            Name = providerName,
            ProviderType = providerType.Value,
            Purpose = purpose,
            Endpoint = info.Endpoint?.ToString(),
            ModelName = info.SelectedModel,
            Auth = new ModelAuthConfig
            {
                ApiKey = info.AccessKey,
                UseDefaultCredentials = string.IsNullOrWhiteSpace(info.AccessKey)
            },
            Enabled = true,
            IsBuiltIn = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await repository.UpsertProviderAsync(provider, ct).ConfigureAwait(false);
        logger.LogInformation(
            "Seeded built-in {Purpose} provider '{Id}' ({ProviderType}, model={Model}, endpoint={Endpoint})",
            purpose, provider.Id, provider.ProviderType, provider.ModelName, provider.Endpoint ?? "(default)");
    }

    /// <summary>
    /// Fallback parser for common Ollama connection string format when ChatClientConnectionInfo fails
    /// (e.g. Uri.TryCreate rejects host.docker.internal on some platforms).
    /// </summary>
    private static ChatClientConnectionInfo? TryParseOllamaFallback(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString) || !connectionString.Contains("Provider=ollama", StringComparison.OrdinalIgnoreCase))
            return null;

        var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string? endpoint = null;
        string? model = null;
        string? accessKey = null;

        foreach (var part in parts)
        {
            var eq = part.IndexOf('=');
            if (eq <= 0) continue;
            var key = part[..eq];
            var value = part[(eq + 1)..];
            if (key.Equals("Endpoint", StringComparison.OrdinalIgnoreCase)) endpoint = value;
            else if (key.Equals("Model", StringComparison.OrdinalIgnoreCase)) model = value;
            else if (key.Equals("AccessKey", StringComparison.OrdinalIgnoreCase)) accessKey = value;
        }

        if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(model))
            return null;

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
            return null;

        return new ChatClientConnectionInfo
        {
            Endpoint = uri,
            SelectedModel = model,
            AccessKey = accessKey,
            Provider = ClientChatProvider.Ollama
        };
    }
}
