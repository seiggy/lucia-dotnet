using A2A;

namespace lucia.Tests.TestDoubles;

/// <summary>
/// Builder pattern for creating test AgentCard instances.
/// </summary>
public class AgentCardBuilder
{
    private string _url = "/a2a/test-agent";
    private string _name = "test-agent";
    private string _description = "A test agent";
    private string _version = "1.0.0";
    private AgentCapabilities _capabilities = new();
    private List<AgentSkill> _skills = new();
    
    public AgentCardBuilder WithUrl(string url)
    {
        _url = url;
        return this;
    }
    
    public AgentCardBuilder WithName(string name)
    {
        _name = name;
        return this;
    }
    
    public AgentCardBuilder WithDescription(string description)
    {
        _description = description;
        return this;
    }
    
    public AgentCardBuilder WithVersion(string version)
    {
        _version = version;
        return this;
    }
    
    public AgentCardBuilder WithCapabilities(AgentCapabilities capabilities)
    {
        _capabilities = capabilities;
        return this;
    }
    
    public AgentCardBuilder WithSkill(AgentSkill skill)
    {
        _skills.Add(skill);
        return this;
    }
    
    public AgentCard Build() => new()
    {
        Url = _url,
        Name = _name,
        Description = _description,
        Version = _version,
        Capabilities = _capabilities,
        DefaultInputModes = ["text"],
        DefaultOutputModes = ["text"],
        Skills = _skills
    };
}
