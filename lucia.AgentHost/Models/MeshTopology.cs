namespace lucia.AgentHost.Models;

public sealed class MeshTopology
{
    public required List<MeshNode> Nodes { get; init; }
    public required List<MeshEdge> Edges { get; init; }
}
