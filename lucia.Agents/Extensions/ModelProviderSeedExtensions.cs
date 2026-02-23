using lucia.Agents.Configuration;
using lucia.Agents.Mcp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Extensions;

/// <summary>
/// Seeds <see cref="ModelProvider"/> documents from environment/connection-string
/// configuration when upgrading from a version that didn't use the provider system.
/// Only runs when setup is complete (not first-run wizard) and no providers exist yet.
/// </summary>
public static class ModelProviderSeedExtensions
{
    private const string DefaultChatProviderId = "default-chat";
    private const string DefaultChatProviderName = "Default Chat Model";

    /// <summary>
    /// If the system has completed onboarding but has no model providers in MongoDB,
    /// creates built-in providers from the environment-configured connection strings.
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

        var existing = await repository.GetAllProvidersAsync(ct).ConfigureAwait(false);
        if (existing.Count > 0)
        {
            logger.LogDebug("Model providers already exist ({Count}) — skipping seed.", existing.Count);
            return;
        }

        logger.LogInformation("No model providers found after setup. Seeding defaults from connection strings...");

        // Seed the default chat provider from the "chat" connection string
        await SeedChatProviderAsync(repository, configuration, logger, ct).ConfigureAwait(false);
    }

    private static async Task SeedChatProviderAsync(
        IModelProviderRepository repository,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken ct)
    {
        var connectionString = configuration.GetConnectionString("chat");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            logger.LogWarning("No 'chat' connection string found — cannot seed default chat provider.");
            return;
        }

        if (!ChatClientConnectionInfo.TryParse(connectionString, out var info))
        {
            logger.LogWarning("Could not parse 'chat' connection string — cannot seed default chat provider.");
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
            logger.LogWarning("Unsupported provider type '{Provider}' in chat connection string — skipping seed.",
                info.Provider);
            return;
        }

        var provider = new ModelProvider
        {
            Id = DefaultChatProviderId,
            Name = DefaultChatProviderName,
            ProviderType = providerType.Value,
            Purpose = ModelPurpose.Chat,
            Endpoint = info.Endpoint?.ToString(),
            ModelName = info.SelectedModel,
            Auth = new ModelAuthConfig
            {
                ApiKey = info.AccessKey
            },
            Enabled = true,
            IsBuiltIn = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await repository.UpsertProviderAsync(provider, ct).ConfigureAwait(false);
        logger.LogInformation(
            "Seeded default chat provider '{Id}' ({ProviderType}, model={Model}, endpoint={Endpoint})",
            provider.Id, provider.ProviderType, provider.ModelName, provider.Endpoint ?? "(default)");
    }
}
