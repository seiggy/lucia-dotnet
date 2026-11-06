using System.Text.Json.Serialization;

namespace lucia.Agents.Family;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MemberIdentityKind
{
    ApiKey,
    SpeakerProfile,
    Oidc,
}
