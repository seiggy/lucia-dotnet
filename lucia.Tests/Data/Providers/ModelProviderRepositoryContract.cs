using lucia.Agents.Abstractions;
using lucia.Agents.Configuration;
using lucia.Agents.Configuration.UserConfiguration;

namespace lucia.Tests.Data.Providers;

/// <summary>
/// Behaviour every <see cref="IModelProviderRepository"/> provider must share.
/// </summary>
public abstract class ModelProviderRepositoryContract
{
    protected abstract Task<StoreScope<IModelProviderRepository>> CreateAsync();

    [Fact]
    public async Task GetAllProvidersAsync_EmptyStore_ReturnsEmpty()
    {
        await using var scope = await CreateAsync();

        Assert.Empty(await scope.Store.GetAllProvidersAsync());
    }

    [Fact]
    public async Task GetProviderAsync_UnknownId_ReturnsNull()
    {
        await using var scope = await CreateAsync();

        Assert.Null(await scope.Store.GetProviderAsync("missing"));
    }

    [Fact]
    public async Task UpsertProviderAsync_NewProvider_RoundTripsEveryMeaningfulField()
    {
        await using var scope = await CreateAsync();
        var createdAt = new DateTime(2024, 02, 03, 04, 05, 06, DateTimeKind.Utc);
        var updatedAt = createdAt.AddMinutes(2);
        var provider = new ModelProvider
        {
            Id = "provider-gpt4o",
            Name = "GPT-4o",
            ProviderType = ProviderType.OpenAI,
            Purpose = ModelPurpose.Chat,
            Endpoint = "https://api.example.com",
            ModelName = "gpt-4o-mini",
            Auth = new ModelAuthConfig
            {
                AuthType = "api-key",
                ApiKey = "secret-key",
                UseDefaultCredentials = false,
            },
            CopilotMetadata = new CopilotModelMetadata
            {
                SupportsVision = true,
                SupportsReasoningEffort = true,
                MaxPromptTokens = 128000,
                MaxOutputTokens = 16384,
                MaxContextWindowTokens = 128000,
                PolicyState = "enabled",
                PolicyTerms = "https://example.com/terms",
                BillingMultiplier = 1.25,
                SupportedReasoningEfforts = ["low", "medium", "high"],
                DefaultReasoningEffort = "medium",
            },
            Enabled = true,
            IsBuiltIn = true,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
        };

        await scope.Store.UpsertProviderAsync(provider);

        var saved = Assert.Single(await scope.Store.GetAllProvidersAsync());
        Assert.Equal(provider.Id, saved.Id);
        Assert.Equal(provider.Name, saved.Name);
        Assert.Equal(provider.ProviderType, saved.ProviderType);
        Assert.Equal(provider.Purpose, saved.Purpose);
        Assert.Equal(provider.Endpoint, saved.Endpoint);
        Assert.Equal(provider.ModelName, saved.ModelName);
        Assert.NotNull(saved.Auth);
        Assert.Equal(provider.Auth.AuthType, saved.Auth.AuthType);
        Assert.Equal(provider.Auth.ApiKey, saved.Auth.ApiKey);
        Assert.Equal(provider.Auth.UseDefaultCredentials, saved.Auth.UseDefaultCredentials);
        Assert.NotNull(saved.CopilotMetadata);
        Assert.Equal(provider.CopilotMetadata.SupportsVision, saved.CopilotMetadata.SupportsVision);
        Assert.Equal(provider.CopilotMetadata.SupportsReasoningEffort, saved.CopilotMetadata.SupportsReasoningEffort);
        Assert.Equal(provider.CopilotMetadata.MaxPromptTokens, saved.CopilotMetadata.MaxPromptTokens);
        Assert.Equal(provider.CopilotMetadata.MaxOutputTokens, saved.CopilotMetadata.MaxOutputTokens);
        Assert.Equal(provider.CopilotMetadata.MaxContextWindowTokens, saved.CopilotMetadata.MaxContextWindowTokens);
        Assert.Equal(provider.CopilotMetadata.PolicyState, saved.CopilotMetadata.PolicyState);
        Assert.Equal(provider.CopilotMetadata.PolicyTerms, saved.CopilotMetadata.PolicyTerms);
        Assert.Equal(provider.CopilotMetadata.BillingMultiplier, saved.CopilotMetadata.BillingMultiplier);
        Assert.Equal(provider.CopilotMetadata.SupportedReasoningEfforts, saved.CopilotMetadata.SupportedReasoningEfforts);
        Assert.Equal(provider.CopilotMetadata.DefaultReasoningEffort, saved.CopilotMetadata.DefaultReasoningEffort);
        Assert.Equal(provider.Enabled, saved.Enabled);
        Assert.Equal(provider.IsBuiltIn, saved.IsBuiltIn);
        Assert.Equal(createdAt, saved.CreatedAt);
        Assert.True(saved.UpdatedAt >= updatedAt);
    }

    [Fact]
    public async Task UpsertProviderAsync_SameId_ReplacesInsteadOfDuplicating()
    {
        await using var scope = await CreateAsync();
        await scope.Store.UpsertProviderAsync(new ModelProvider { Id = "provider-a", Name = "Alpha", ProviderType = ProviderType.OpenAI, Purpose = ModelPurpose.Chat, ModelName = "gpt-4o", Enabled = true });

        await scope.Store.UpsertProviderAsync(new ModelProvider { Id = "provider-a", Name = "Beta", ProviderType = ProviderType.Anthropic, Purpose = ModelPurpose.Embedding, ModelName = "claude-sonnet-4", Enabled = false });

        var providers = await scope.Store.GetAllProvidersAsync();
        var provider = Assert.Single(providers);
        Assert.Equal("provider-a", provider.Id);
        Assert.Equal("Beta", provider.Name);
        Assert.Equal(ProviderType.Anthropic, provider.ProviderType);
        Assert.Equal(ModelPurpose.Embedding, provider.Purpose);
        Assert.False(provider.Enabled);
    }

    [Fact]
    public async Task DeleteProviderAsync_RemovesOnlyNamedProvider()
    {
        await using var scope = await CreateAsync();
        await scope.Store.UpsertProviderAsync(new ModelProvider { Id = "provider-alpha", Name = "Alpha", ProviderType = ProviderType.OpenAI, Purpose = ModelPurpose.Chat, ModelName = "gpt-4o", Enabled = true });
        await scope.Store.UpsertProviderAsync(new ModelProvider { Id = "provider-beta", Name = "Beta", ProviderType = ProviderType.Ollama, Purpose = ModelPurpose.Chat, ModelName = "llama3", Enabled = true });

        await scope.Store.DeleteProviderAsync("provider-alpha");

        var providers = await scope.Store.GetAllProvidersAsync();
        var provider = Assert.Single(providers);
        Assert.Equal("provider-beta", provider.Id);
    }

    [Fact]
    public async Task GetEnabledProvidersAsync_ReturnsOnlyEnabledProvidersInNameOrder()
    {
        await using var scope = await CreateAsync();
        await scope.Store.UpsertProviderAsync(CreateProvider("beta", "Beta", false));
        await scope.Store.UpsertProviderAsync(CreateProvider("alpha", "Alpha", true));
        await scope.Store.UpsertProviderAsync(CreateProvider("gamma", "Gamma", true));

        var providers = await scope.Store.GetEnabledProvidersAsync();

        Assert.Equal(["alpha", "gamma"], providers.Select(p => p.Id).ToArray());
    }

    [Fact]
    public async Task UpsertProviderAsync_RepeatSameRecord_IsIdempotent()
    {
        await using var scope = await CreateAsync();
        var provider = CreateProvider("provider-ok", "Provider OK", true);

        await scope.Store.UpsertProviderAsync(provider);
        await scope.Store.UpsertProviderAsync(new ModelProvider
        {
            Id = provider.Id,
            Name = provider.Name,
            ProviderType = provider.ProviderType,
            Purpose = provider.Purpose,
            ModelName = provider.ModelName,
            Enabled = provider.Enabled,
            Auth = provider.Auth,
            Endpoint = provider.Endpoint,
            IsBuiltIn = provider.IsBuiltIn,
            CreatedAt = provider.CreatedAt,
            UpdatedAt = provider.UpdatedAt,
        });

        var providers = await scope.Store.GetAllProvidersAsync();
        Assert.Single(providers);
        Assert.Equal("provider-ok", providers[0].Id);
    }

    private static ModelProvider CreateProvider(string id, string name, bool enabled)
        => new()
        {
            Id = id,
            Name = name,
            ProviderType = ProviderType.OpenAI,
            Purpose = ModelPurpose.Chat,
            ModelName = "gpt-4o",
            Enabled = enabled,
            Auth = new ModelAuthConfig { AuthType = "none" },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
}
