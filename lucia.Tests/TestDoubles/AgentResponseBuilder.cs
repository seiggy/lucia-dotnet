using lucia.Agents.Orchestration.Models;

namespace lucia.Tests.TestDoubles;

/// <summary>
/// Builder pattern for creating test OrchestratorAgentResponse instances.
/// </summary>
public class AgentResponseBuilder
{
    private string _agentId = "test-agent";
    private string _content = "Test response";
    private bool _success = true;
    private string? _errorMessage;
    private long _executionTimeMs = 100;
    private bool _needsInput;
    
    public AgentResponseBuilder WithAgentId(string agentId)
    {
        _agentId = agentId;
        return this;
    }
    
    public AgentResponseBuilder WithContent(string content)
    {
        _content = content;
        return this;
    }
    
    public AgentResponseBuilder WithSuccess(bool success)
    {
        _success = success;
        return this;
    }
    
    public AgentResponseBuilder WithError(string errorMessage)
    {
        _success = false;
        _errorMessage = errorMessage;
        return this;
    }
    
    public AgentResponseBuilder WithErrorMessage(string errorMessage)
    {
        _errorMessage = errorMessage;
        return this;
    }
    
    public AgentResponseBuilder WithExecutionTime(long ms)
    {
        _executionTimeMs = ms;
        return this;
    }

    public AgentResponseBuilder WithNeedsInput(bool needsInput = true)
    {
        _needsInput = needsInput;
        return this;
    }
    
    public OrchestratorAgentResponse Build() => new()
    {
        AgentId = _agentId,
        Content = _content,
        Success = _success,
        ErrorMessage = _errorMessage,
        ExecutionTimeMs = _executionTimeMs,
        NeedsInput = _needsInput
    };
}
