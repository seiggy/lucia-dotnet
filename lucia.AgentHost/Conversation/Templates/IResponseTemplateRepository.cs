namespace lucia.AgentHost.Conversation.Templates;

/// <summary>
/// Persistence layer for <see cref="ResponseTemplate"/> documents.
/// </summary>
public interface IResponseTemplateRepository
{
    /// <summary>Returns every stored template.</summary>
    Task<IReadOnlyList<ResponseTemplate>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Returns a single template by its MongoDB ObjectId.</summary>
    Task<ResponseTemplate?> GetByIdAsync(string id, CancellationToken ct = default);

    /// <summary>Returns the template registered for a specific skill and action pair.</summary>
    Task<ResponseTemplate?> GetBySkillAndActionAsync(string skillId, string action, CancellationToken ct = default);

    /// <summary>Inserts a new template document.</summary>
    Task<ResponseTemplate> CreateAsync(ResponseTemplate template, CancellationToken ct = default);

    /// <summary>Replaces an existing template identified by <paramref name="id"/>.</summary>
    Task<ResponseTemplate> UpdateAsync(string id, ResponseTemplate template, CancellationToken ct = default);

    /// <summary>Deletes a template by its ObjectId. Returns <c>true</c> when a document was removed.</summary>
    Task<bool> DeleteAsync(string id, CancellationToken ct = default);

    /// <summary>Drops all templates and re-seeds the collection with <see cref="DefaultResponseTemplates"/>.</summary>
    Task ResetToDefaultsAsync(CancellationToken ct = default);
}
