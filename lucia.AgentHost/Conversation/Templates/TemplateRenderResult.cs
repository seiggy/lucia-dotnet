using lucia.Agents.CommandTracing;

namespace lucia.AgentHost.Conversation.Templates;

/// <summary>
/// Result of rendering a response template, including trace metadata.
/// </summary>
public sealed record TemplateRenderResult
{
    public required string Text { get; init; }
    public required CommandTraceTemplateRender TraceData { get; init; }
}
