using lucia.Agents.Configuration;
using lucia.Agents.Configuration.UserConfiguration;

namespace lucia.Agents.Abstractions;

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
