using System.Text.Json.Serialization;

namespace lucia.Agents.Family;

/// <summary>
/// The kind of capability a <see cref="CapabilityRule"/> targets.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CapabilityKind
{
    Agent,
    Tool,
}
