using lucia.Agents.Orchestration.Models;

namespace lucia.Tests.TestDoubles;

/// <summary>
/// Builder pattern for creating test AgentChoiceResult instances.
/// </summary>
public class AgentChoiceResultBuilder
{
    private string _agentId = "test-agent";
    private double _confidence = 0.9;
    private string _reasoning = "Test reasoning";
    private List<string>? _additionalAgents;
    
    public AgentChoiceResultBuilder WithAgentId(string agentId)
    {
        _agentId = agentId;
        return this;
    }
    
    public AgentChoiceResultBuilder WithConfidence(double confidence)
    {
        _confidence = confidence;
        return this;
    }
    
    public AgentChoiceResultBuilder WithReasoning(string reasoning)
    {
        _reasoning = reasoning;
        return this;
    }
    
    public AgentChoiceResultBuilder WithAdditionalAgents(params string[] agents)
    {
        _additionalAgents = agents.ToList();
        return this;
    }
    
    public AgentChoiceResult Build() => new()
    {
        AgentId = _agentId,
        Confidence = _confidence,
        Reasoning = _reasoning,
        AdditionalAgents = _additionalAgents
    };
}
