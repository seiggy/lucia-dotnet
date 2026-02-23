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
    /// If the system has completed onboarding, checks for missing built-in providers
    /// and creates them from the environment-configured connection strings.
    /// </summary>
    public static async Task SeedDefaultModelProvidersAsync(
        this IModelProviderRepository repository,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken ct = default)
    {
        // Only seed when setup is complete (we're upgrading, not first-run)
        var setupComplete = configuration["Auth:SetupComplete"];
        if (!string.Equals(setupComplete, "true", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogDebug("Setup not complete — skipping model provider seed.");
            return;
        }

        // Seed each built-in provider independently — check by ID so existing
        // user-created providers don't block seeding of the defaults.
        await SeedProviderFromConnectionStringAsync(
            repository, configuration, logger,
            connectionName: "chat",
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

        var connectionString = configuration.GetConnectionString(connectionName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            logger.LogWarning(
                "No '{ConnectionName}' connection string found — cannot seed built-in provider '{Id}'.",
                connectionName, providerId);
            return;
        }

        if (!ChatClientConnectionInfo.TryParse(connectionString, out var info))
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
}
