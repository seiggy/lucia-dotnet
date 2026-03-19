using System.Collections.Concurrent;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Logging;

namespace lucia.AgentHost.Conversation.Templates;

/// <summary>
/// Renders a human-friendly response by looking up a <see cref="ResponseTemplate"/>
/// for the given skill/action and substituting <c>{placeholder}</c> tokens with
/// captured values. Templates are cached in-memory and refreshed on CRUD operations.
/// </summary>
public sealed partial class ResponseTemplateRenderer
{
    private const string FallbackResponse = "Done.";

    private readonly IResponseTemplateRepository _repository;
    private readonly ILogger<ResponseTemplateRenderer> _logger;
    private readonly ConcurrentDictionary<string, ResponseTemplate?> _cache = new();
    private volatile bool _cacheLoaded;

    public ResponseTemplateRenderer(
        IResponseTemplateRepository repository,
        ILogger<ResponseTemplateRenderer> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Invalidates the in-memory template cache. Call after CRUD operations.
    /// </summary>
    public void InvalidateCache()
    {
        _cache.Clear();
        _cacheLoaded = false;
    }

    /// <summary>
    /// Looks up a template for <paramref name="skillId"/>/<paramref name="action"/>,
    /// picks one at random, and replaces <c>{placeholder}</c> tokens with values from
    /// <paramref name="captures"/>. Returns a generic fallback when no template is found.
    /// </summary>
    public async Task<string> RenderAsync(
        string skillId,
        string action,
        IReadOnlyDictionary<string, string> captures,
        CancellationToken ct = default)
    {
        var template = await GetCachedTemplateAsync(skillId, action, ct).ConfigureAwait(false);

        if (template is null || template.Templates.Length == 0)
        {
            LogTemplateMiss(skillId, action);
            return FallbackResponse;
        }

        // Use sub-millisecond tick count for entropy — avoids the deterministic
        // sequence that Random.Shared produces when called at similar points in
        // the request pipeline.
        var ticks = Environment.TickCount64;
        var index = (int)(ticks % template.Templates.Length);
        var selected = template.Templates[index];

        LogTemplateSelected(skillId, action, index, template.Templates.Length);

        return PlaceholderPattern().Replace(selected, match =>
        {
            var key = match.Groups[1].Value;
            return captures.TryGetValue(key, out var value) ? value : string.Empty;
        });
    }

    private async Task<ResponseTemplate?> GetCachedTemplateAsync(
        string skillId, string action, CancellationToken ct)
    {
        if (!_cacheLoaded)
        {
            await WarmCacheAsync(ct).ConfigureAwait(false);
        }

        var key = $"{skillId}::{action}";
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        // Cache miss for a new skill/action added after warm-up
        var template = await _repository.GetBySkillAndActionAsync(skillId, action, ct)
            .ConfigureAwait(false);
        _cache[key] = template;
        return template;
    }

    private async Task WarmCacheAsync(CancellationToken ct)
    {
        var all = await _repository.GetAllAsync(ct).ConfigureAwait(false);
        foreach (var t in all)
        {
            _cache[$"{t.SkillId}::{t.Action}"] = t;
        }
        _cacheLoaded = true;
    }

    [GeneratedRegex(@"\{(\w+)\}", RegexOptions.Compiled)]
    private static partial Regex PlaceholderPattern();

    [LoggerMessage(Level = LogLevel.Debug, Message = "No response template found for {SkillId}/{Action}, using fallback")]
    private partial void LogTemplateMiss(string skillId, string action);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Template {SkillId}/{Action}: selected index {Index} of {Count} variants")]
    private partial void LogTemplateSelected(string skillId, string action, int index, int count);
}
