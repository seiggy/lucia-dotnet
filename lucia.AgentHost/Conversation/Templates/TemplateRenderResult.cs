using lucia.Agents.CommandTracing;

namespace lucia.AgentHost.Conversation.Templates;

/// <summary>
/// Result of rendering a response template, including trace metadata.
/// </summary>
public sealed record TemplateRenderResult
{
    public required string Text { get; init; }
    public required CommandTraceTemplateRender TraceData { get; init; }

    /// <summary>
    /// Builds a result from a skill's live response text for query patterns, whose answers
    /// (sensor readings, door states) cannot be expressed by capture-only confirmation templates.
    /// </summary>
    public static TemplateRenderResult FromSkillResponse(string skillId, string action, string text) => new()
    {
        Text = text,
        TraceData = new CommandTraceTemplateRender
        {
            TemplateKey = $"{skillId}::{action}",
            RawTemplate = text,
            RenderedText = text,
            VariantCount = 1,
            SelectedIndex = 0,
            IsFallback = false,
        },
    };
}
