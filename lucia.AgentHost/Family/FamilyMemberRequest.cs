using lucia.Agents.Family;

namespace lucia.AgentHost.Family;

/// <summary>
/// Profile fields an administrator sets when creating or updating a family member.
/// </summary>
public sealed record FamilyMemberRequest(
    string DisplayName,
    FamilyRole Role,
    List<string>? GuardianIds,
    int? BirthYear,
    bool? IsActive,
    DateTime? AccessExpiresAt,
    bool? UseAssistantHarness = null);
