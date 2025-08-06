namespace lucia.Agents.A2A;

public class AgentSkill
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string Description { get; set; }
    public required string[] Tags { get; set; } = [];
    public string[]? Examples { get; set; }
    public string[]? InputModes { get; set; }
    public string[]? OutputModes { get; set; }
}