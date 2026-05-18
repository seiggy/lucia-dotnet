namespace lucia.AgentHost.Conversation.Templates;

/// <summary>
/// In-memory fallback implementation of <see cref="IResponseTemplateRepository"/> used when
/// the selected durable store does not yet support response templates.
/// </summary>
public sealed class InMemoryResponseTemplateRepository : IResponseTemplateRepository
{
    private readonly Dictionary<string, ResponseTemplate> _templates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();

    public Task<IReadOnlyList<ResponseTemplate>> GetAllAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            IReadOnlyList<ResponseTemplate> templates = _templates.Values
                .OrderBy(template => template.SkillId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(template => template.Action, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return Task.FromResult(templates);
        }
    }

    public Task<ResponseTemplate?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _templates.TryGetValue(id, out var template);
            return Task.FromResult(template);
        }
    }

    public Task<ResponseTemplate?> GetBySkillAndActionAsync(string skillId, string action, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var template = _templates.Values.FirstOrDefault(existing =>
                string.Equals(existing.SkillId, skillId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(existing.Action, action, StringComparison.OrdinalIgnoreCase));

            return Task.FromResult(template);
        }
    }

    public Task<ResponseTemplate> CreateAsync(ResponseTemplate template, CancellationToken ct = default)
    {
        template.Id ??= Guid.NewGuid().ToString("N");

        lock (_lock)
        {
            _templates[template.Id] = template;
        }

        return Task.FromResult(template);
    }

    public Task<ResponseTemplate> UpdateAsync(string id, ResponseTemplate template, CancellationToken ct = default)
    {
        template.Id = id;
        template.UpdatedAt = DateTime.UtcNow;

        lock (_lock)
        {
            _templates[id] = template;
        }

        return Task.FromResult(template);
    }

    public Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_templates.Remove(id));
        }
    }

    public Task ResetToDefaultsAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            _templates.Clear();
            foreach (var template in DefaultResponseTemplates.GetDefaults())
            {
                template.Id ??= Guid.NewGuid().ToString("N");
                _templates[template.Id] = template;
            }
        }

        return Task.CompletedTask;
    }
}
