namespace lucia.Agents.Orchestration;

/// <summary>
/// Aggregation options for the result aggregator executor.
/// Bound from appsettings.json "ResultAggregator" section.
/// </summary>
public sealed class ResultAggregatorOptions
{
    /// <summary>
    /// Defines the priority order for agents when combining responses.
    /// </summary>
    public IList<string> AgentPriority { get; set; } = new List<string> 
    { 
        "light-agent", 
        "music-agent", 
        "climate-agent", 
        "security-agent", 
        "general-assistant" 
    };

    /// <summary>
    /// Template used when an agent succeeds but returns no content.
    /// </summary>
    public string DefaultSuccessTemplate { get; set; } = "{0} completed successfully.";

    /// <summary>
    /// Fallback message when no responses are available.
    /// </summary>
    public string DefaultFallbackMessage { get; set; } = "I'm still working on that request.";
    
    /// <summary>
    /// Fallback error message when agent failure provides no reason.
    /// </summary>
    public string DefaultFailureMessage { get; set; } = "Unknown error";
    
    /// <summary>
    /// Message template for partial failures.
    /// Placeholders: {0} = success message, {1} = failure message.
    /// </summary>
    public string PartialFailureTemplate { get; set; } = 
        "{0} However, {1}";
    
    /// <summary>
    /// Enable natural language joining for multiple responses.
    /// </summary>
    public bool EnableNaturalLanguageJoining { get; set; } = true;
}
