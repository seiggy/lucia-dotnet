using System.Text.RegularExpressions;

using Microsoft.Extensions.Logging;

namespace lucia.AgentHost.Conversation.Templates;

/// <summary>
/// Renders a human-friendly response by looking up a <see cref="ResponseTemplate"/>
/// for the given skill/action and substituting <c>{placeholder}</c> tokens with
/// captured values.
/// </summary>
public sealed partial class ResponseTemplateRenderer
{
    private const string FallbackResponse = "Done.";

    private readonly IResponseTemplateRepository _repository;
    private readonly ILogger<ResponseTemplateRenderer> _logger;

    public ResponseTemplateRenderer(
        IResponseTemplateRepository repository,
        ILogger<ResponseTemplateRenderer> logger)
    {
        _repository = repository;
        _logger = logger;
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
        var template = await _repository
            .GetBySkillAndActionAsync(skillId, action, ct)
            .ConfigureAwait(false);

        if (template is null || template.Templates.Length == 0)
        {
            LogTemplateMiss(skillId, action);
            return FallbackResponse;
        }

        var selected = template.Templates[Random.Shared.Next(template.Templates.Length)];

        return PlaceholderPattern().Replace(selected, match =>
        {
            var key = match.Groups[1].Value;
            return captures.TryGetValue(key, out var value) ? value : string.Empty;
        });
    }

    [GeneratedRegex(@"\{(\w+)\}", RegexOptions.Compiled)]
    private static partial Regex PlaceholderPattern();

    [LoggerMessage(Level = LogLevel.Debug, Message = "No response template found for {SkillId}/{Action}, using fallback")]
    private partial void LogTemplateMiss(string skillId, string action);
}
