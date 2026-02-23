namespace lucia.AgentHost.Models;

public sealed class MeshNode
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public required string NodeType { get; init; }
    public bool IsRemote { get; init; }
}
