namespace lucia.Agents.Training.Models;

/// <summary>
/// A single agent instruction record stored in a routing decision trace.
/// </summary>
public sealed class AgentInstructionRecord
{
    public required string AgentId { get; set; }
    public required string Instruction { get; set; }
}
