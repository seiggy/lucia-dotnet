namespace lucia.AgentHost.Conversation;

/// <summary>Discriminator for how the API layer should respond.</summary>
public enum ProcessingKind
{
    /// <summary>Request handled locally, including onboarding. Return instant JSON.</summary>
    CommandHandled,

    /// <summary>LLM completed synchronously — JSON response.</summary>
    LlmCompleted,

    /// <summary>LLM fallback pending — API should handle streaming.</summary>
    LlmFallback,
}
