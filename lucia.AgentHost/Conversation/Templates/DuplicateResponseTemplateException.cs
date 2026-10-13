namespace lucia.AgentHost.Conversation.Templates;

/// <summary>
/// Thrown by <see cref="IResponseTemplateRepository.CreateAsync"/> when a template already exists
/// for the same skill and action, whichever database provider is in use.
/// </summary>
public sealed class DuplicateResponseTemplateException(string skillId, string action, Exception? inner = null)
    : Exception($"A template for {skillId}/{action} already exists.", inner)
{
    public string SkillId { get; } = skillId;

    public string Action { get; } = action;
}