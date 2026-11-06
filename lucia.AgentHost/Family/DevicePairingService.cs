using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using lucia.AgentHost.Auth;
using lucia.Agents.Abstractions;
using lucia.Agents.Family;

namespace lucia.AgentHost.Family;

/// <summary>
/// Issues one-time pairing codes and exchanges them for member-only device keys.
/// </summary>
public sealed class DevicePairingService(
    IDevicePairingStore pairings,
    IFamilyMemberStore members,
    IApiKeyService apiKeys,
    TimeProvider timeProvider)
{
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(10);

    public static readonly TimeSpan MaxLifetime = TimeSpan.FromDays(1);

    public const int MaxDeviceNameLength = 64;

    public async Task<PairingCode?> CreateAsync(
        string memberId,
        TimeSpan? lifetime,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var member = await members.GetAsync(memberId, cancellationToken).ConfigureAwait(false);
        if (member is null || !MemberPrincipalResolver.CanAuthenticate(member, now))
        {
            return null;
        }

        var validFor = lifetime is { } requested && requested > TimeSpan.Zero
            ? (requested < MaxLifetime ? requested : MaxLifetime)
            : DefaultLifetime;
        var code = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var pairing = new DevicePairing
        {
            Id = Guid.NewGuid().ToString("N"),
            MemberId = member.Id,
            CodeHash = Hash(code),
            CreatedAt = now,
            ExpiresAt = now + validFor,
        };
        await pairings.CreateAsync(pairing, cancellationToken).ConfigureAwait(false);
        return new PairingCode(pairing, code);
    }

    public async Task<PairingRedemption?> RedeemAsync(
        string code,
        string? deviceName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var pairing = await pairings.TryRedeemAsync(Hash(code.Trim()), now, cancellationToken).ConfigureAwait(false);
        if (pairing is null)
        {
            return null;
        }

        var member = await members.GetAsync(pairing.MemberId, cancellationToken).ConfigureAwait(false);
        if (member is null || !MemberPrincipalResolver.CanAuthenticate(member, now))
        {
            return null;
        }

        var name = string.IsNullOrWhiteSpace(deviceName)
            ? $"{member.DisplayName}'s device"
            : deviceName.Trim();
        if (name.Length > MaxDeviceNameLength)
        {
            name = name[..MaxDeviceNameLength];
        }

        var key = await apiKeys.CreateMemberKeyAsync(name, cancellationToken).ConfigureAwait(false);
        var linked = await members.LinkIdentityAsync(
                new MemberIdentityLink
                {
                    MemberId = member.Id,
                    Kind = MemberIdentityKind.ApiKey,
                    Subject = key.Id,
                    Label = name,
                    CreatedAt = now,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (!linked)
        {
            await apiKeys.RevokeKeyAsync(key.Id, cancellationToken).ConfigureAwait(false);
            return null;
        }

        await pairings.SetApiKeyAsync(pairing.Id, key.Id, cancellationToken).ConfigureAwait(false);
        return new PairingRedemption(key.Key, key.Id, member);
    }

    private static string Hash(string code) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(code)));
}
