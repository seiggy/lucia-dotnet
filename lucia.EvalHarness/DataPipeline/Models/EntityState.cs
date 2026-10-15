namespace lucia.EvalHarness.DataPipeline.Models;

/// <summary>
/// Represents Home Assistant entity state.
/// </summary>
public sealed class EntityState
{
    public required string State { get; set; }

    public Dictionary<string, object> Attributes { get; set; } = [];
}
