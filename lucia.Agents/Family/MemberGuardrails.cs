namespace lucia.Agents.Family;

/// <summary>
/// Optional AgentEval guardrails applied to a member's conversations, in addition to the
/// blocked-topic gate that <see cref="MemberLlmControls.BlockedTopics"/> enables.
/// </summary>
public sealed class MemberGuardrails
{
    /// <summary>
    /// Refuse requests containing known prompt-injection markers.
    /// </summary>
    public bool BlockPromptInjection { get; init; }

    /// <summary>
    /// Redact email addresses, phone numbers, and similar personal data from responses.
    /// </summary>
    public bool RedactPersonalInfo { get; init; }
}
