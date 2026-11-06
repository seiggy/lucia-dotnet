using System.Text.Json.Serialization;

namespace lucia.Agents.Family;

/// <summary>
/// What the action broker does when a member invokes a capability.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ActionRule
{
    Allow,
    AskGuardian,
    Deny,
}
