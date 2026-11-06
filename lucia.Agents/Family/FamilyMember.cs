namespace lucia.Agents.Family;

/// <summary>
/// A person in the household. Logins, voice profiles, and devices link to a member through
/// <see cref="MemberIdentityLink"/>; none of them is the person.
/// </summary>
public sealed class FamilyMember
{
    public required string Id { get; init; }

    public required string DisplayName { get; set; }

    public required FamilyRole Role { get; set; }

    /// <summary>
    /// Members who may decide this member's <see cref="ActionRule.AskGuardian"/> requests.
    /// </summary>
    public List<string> GuardianIds { get; set; } = [];

    public int? BirthYear { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// When a time-limited (guest) member stops being able to authenticate.
    /// </summary>
    public DateTime? AccessExpiresAt { get; set; }

    public List<CapabilityRule> CapabilityRules { get; set; } = [];

    public MemberLlmControls LlmControls { get; set; } = new();

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
