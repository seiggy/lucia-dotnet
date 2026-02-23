using lucia.Agents.Configuration;

namespace lucia.Agents.Mcp;

/// <summary>
/// Repository for CRUD operations on user-configured model providers.
/// </summary>
public interface IModelProviderRepository
{
    Task<List<ModelProvider>> GetAllProvidersAsync(CancellationToken ct = default);
    Task<List<ModelProvider>> GetEnabledProvidersAsync(CancellationToken ct = default);
    Task<ModelProvider?> GetProviderAsync(string id, CancellationToken ct = default);
    Task UpsertProviderAsync(ModelProvider provider, CancellationToken ct = default);
    Task DeleteProviderAsync(string id, CancellationToken ct = default);
}
