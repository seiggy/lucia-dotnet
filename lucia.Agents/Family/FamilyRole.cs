using System.Text.Json.Serialization;

namespace lucia.Agents.Family;

/// <summary>
/// Setup preset for a family member. Explicit capability rules and guardian links make the
/// actual decisions; the role only supplies defaults.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FamilyRole
{
    Administrator,
    Adult,
    Child,
    Guest,
}
