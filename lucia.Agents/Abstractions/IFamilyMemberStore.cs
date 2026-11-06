using lucia.Agents.Family;

namespace lucia.Agents.Abstractions;

/// <summary>
/// Family members and the external identities linked to them.
/// </summary>
public interface IFamilyMemberStore
{
    Task<IReadOnlyList<FamilyMember>> ListAsync(CancellationToken cancellationToken = default);

    Task<FamilyMember?> GetAsync(string memberId, CancellationToken cancellationToken = default);

    Task UpsertAsync(FamilyMember member, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the member and every identity linked to it. Returns false if it did not exist.
    /// </summary>
    Task<bool> DeleteAsync(string memberId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MemberIdentityLink>> ListIdentitiesAsync(
        string memberId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Links an identity. Returns false, without changing anything, if the identity is already
    /// linked to any member.
    /// </summary>
    Task<bool> LinkIdentityAsync(MemberIdentityLink link, CancellationToken cancellationToken = default);

    Task<bool> UnlinkIdentityAsync(
        MemberIdentityKind kind,
        string issuer,
        string subject,
        CancellationToken cancellationToken = default);

    Task<FamilyMember?> FindByIdentityAsync(
        MemberIdentityKind kind,
        string issuer,
        string subject,
        CancellationToken cancellationToken = default);
}
