using System.Text.Json.Serialization;

namespace lucia.Agents.Family;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ActionStatus
{
    AwaitingDecision,
    Approved,
    Denied,
    Expired,
    Cancelled,
    Executing,
    Succeeded,
    Failed,
    OutcomeUnknown,
}
