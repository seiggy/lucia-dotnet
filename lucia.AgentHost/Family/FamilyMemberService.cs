using lucia.Agents.Abstractions;
using lucia.Agents.Auth;
using lucia.Agents.Family;
using lucia.Wyoming.Diarization;

namespace lucia.AgentHost.Family;

/// <summary>
/// Administrator operations on family members. Enforces the household invariants the stores
/// do not: guardianship, role constraints, valid identity links, and valid LLM controls.
/// </summary>
public sealed class FamilyMemberService(
    IFamilyMemberStore members,
    IApiKeyService apiKeys,
    ILlmUsageStore usage,
    IModelProviderRepository providers,
    ISpeakerProfileStore? speakerProfiles,
    TimeProvider timeProvider)
{
    public const int MaxDisplayNameLength = 64;
    public const int MaxSystemPromptLength = 4000;
    public const int MaxBlockedTopics = 50;
    public const int MaxBlockedTopicLength = 100;

    public Task<IReadOnlyList<FamilyMember>> ListAsync(CancellationToken cancellationToken = default) =>
        members.ListAsync(cancellationToken);

    public async Task<FamilyOperationResult<FamilyMember>> GetAsync(string memberId, CancellationToken cancellationToken = default)
    {
        var member = await members.GetAsync(memberId, cancellationToken).ConfigureAwait(false);
        return member is null ? FamilyOperationResult<FamilyMember>.NotFound() : FamilyOperationResult<FamilyMember>.Ok(member);
    }

    public async Task<FamilyOperationResult<FamilyMember>> CreateAsync(
        FamilyMemberRequest request,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var member = new FamilyMember
        {
            Id = Guid.NewGuid().ToString("N"),
            DisplayName = string.Empty,
            Role = request.Role,
            CreatedAt = now,
        };
        var error = await ApplyProfileAsync(member, request, now, cancellationToken).ConfigureAwait(false);
        if (error is not null)
        {
            return FamilyOperationResult<FamilyMember>.Invalid(error);
        }

        await members.UpsertAsync(member, cancellationToken).ConfigureAwait(false);
        return FamilyOperationResult<FamilyMember>.Ok(member);
    }

    public async Task<FamilyOperationResult<FamilyMember>> UpdateAsync(
        string memberId,
        FamilyMemberRequest request,
        CancellationToken cancellationToken = default)
    {
        var member = await members.GetAsync(memberId, cancellationToken).ConfigureAwait(false);
        if (member is null)
        {
            return FamilyOperationResult<FamilyMember>.NotFound();
        }

        if (!CanBeGuardian(request.Role))
        {
            var all = await members.ListAsync(cancellationToken).ConfigureAwait(false);
            if (all.Any(other => other.GuardianIds.Contains(memberId, StringComparer.Ordinal)))
            {
                return FamilyOperationResult<FamilyMember>.Invalid(
                    "This member is a guardian. Assign another guardian before changing them to a child or guest.");
            }
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var error = await ApplyProfileAsync(member, request, now, cancellationToken).ConfigureAwait(false);
        if (error is not null)
        {
            return FamilyOperationResult<FamilyMember>.Invalid(error);
        }

        await members.UpsertAsync(member, cancellationToken).ConfigureAwait(false);
        return FamilyOperationResult<FamilyMember>.Ok(member);
    }

    public async Task<FamilyOperationResult<bool>> DeleteAsync(string memberId, CancellationToken cancellationToken = default)
    {
        var member = await members.GetAsync(memberId, cancellationToken).ConfigureAwait(false);
        if (member is null)
        {
            return FamilyOperationResult<bool>.NotFound();
        }

        var wards = (await members.ListAsync(cancellationToken).ConfigureAwait(false))
            .Where(other => other.GuardianIds.Contains(memberId, StringComparer.Ordinal))
            .ToList();
        var orphan = wards.FirstOrDefault(ward => ward.Role == FamilyRole.Child && ward.GuardianIds.Count == 1);
        if (orphan is not null)
        {
            return FamilyOperationResult<bool>.Conflict(
                $"{member.DisplayName} is {orphan.DisplayName}'s only guardian. Assign another guardian first.");
        }

        var identities = await members.ListIdentitiesAsync(memberId, cancellationToken).ConfigureAwait(false);
        foreach (var link in identities.Where(link => link.Kind == MemberIdentityKind.ApiKey))
        {
            var revoked = await RevokeKeyAsync(link.Subject, cancellationToken).ConfigureAwait(false);
            if (revoked is not null)
            {
                return FamilyOperationResult<bool>.Conflict(revoked);
            }
        }

        foreach (var ward in wards)
        {
            ward.GuardianIds.Remove(memberId);
            ward.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
            await members.UpsertAsync(ward, cancellationToken).ConfigureAwait(false);
        }

        await members.DeleteAsync(memberId, cancellationToken).ConfigureAwait(false);
        return FamilyOperationResult<bool>.Ok(true);
    }

    public async Task<FamilyOperationResult<FamilyMember>> SetCapabilityRulesAsync(
        string memberId,
        IReadOnlyList<CapabilityRule> rules,
        CancellationToken cancellationToken = default)
    {
        var member = await members.GetAsync(memberId, cancellationToken).ConfigureAwait(false);
        if (member is null)
        {
            return FamilyOperationResult<FamilyMember>.NotFound();
        }

        var normalized = new List<CapabilityRule>(rules.Count);
        var seen = new HashSet<(CapabilityKind, string)>();
        foreach (var rule in rules)
        {
            var id = rule.CapabilityId?.Trim();
            if (string.IsNullOrEmpty(id))
            {
                return FamilyOperationResult<FamilyMember>.Invalid("Every rule needs an agent or tool ID.");
            }
            if (!seen.Add((rule.Kind, id.ToUpperInvariant())))
            {
                return FamilyOperationResult<FamilyMember>.Invalid($"There is more than one rule for {rule.Kind} '{id}'.");
            }
            normalized.Add(new CapabilityRule { Kind = rule.Kind, CapabilityId = id, Rule = rule.Rule });
        }

        member.CapabilityRules = normalized;
        member.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        await members.UpsertAsync(member, cancellationToken).ConfigureAwait(false);
        return FamilyOperationResult<FamilyMember>.Ok(member);
    }

    public async Task<FamilyOperationResult<FamilyMember>> SetLlmControlsAsync(
        string memberId,
        MemberLlmControls controls,
        CancellationToken cancellationToken = default)
    {
        var member = await members.GetAsync(memberId, cancellationToken).ConfigureAwait(false);
        if (member is null)
        {
            return FamilyOperationResult<FamilyMember>.NotFound();
        }

        var allowed = controls.AllowedProviderIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        foreach (var providerId in allowed)
        {
            if (await providers.GetProviderAsync(providerId, cancellationToken).ConfigureAwait(false) is null)
            {
                return FamilyOperationResult<FamilyMember>.Invalid($"Model provider '{providerId}' does not exist.");
            }
        }

        var preferred = string.IsNullOrWhiteSpace(controls.PreferredProviderId) ? null : controls.PreferredProviderId.Trim();
        if (preferred is not null)
        {
            if (allowed.Count > 0 && !allowed.Contains(preferred, StringComparer.Ordinal))
            {
                return FamilyOperationResult<FamilyMember>.Invalid("The preferred model provider must be one of the allowed providers.");
            }
            if (await providers.GetProviderAsync(preferred, cancellationToken).ConfigureAwait(false) is null)
            {
                return FamilyOperationResult<FamilyMember>.Invalid($"Model provider '{preferred}' does not exist.");
            }
        }

        if (controls.DailyBudgetUsd < 0 || controls.MonthlyBudgetUsd < 0)
        {
            return FamilyOperationResult<FamilyMember>.Invalid("Budgets cannot be negative.");
        }

        var systemPrompt = string.IsNullOrWhiteSpace(controls.SystemPrompt) ? null : controls.SystemPrompt.Trim();
        if (systemPrompt?.Length > MaxSystemPromptLength)
        {
            return FamilyOperationResult<FamilyMember>.Invalid($"The system prompt is limited to {MaxSystemPromptLength} characters.");
        }

        var topics = controls.BlockedTopics
            .Where(topic => !string.IsNullOrWhiteSpace(topic))
            .Select(topic => topic.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (topics.Count > MaxBlockedTopics || topics.Any(topic => topic.Length > MaxBlockedTopicLength))
        {
            return FamilyOperationResult<FamilyMember>.Invalid(
                $"Use at most {MaxBlockedTopics} blocked topics of up to {MaxBlockedTopicLength} characters each.");
        }

        if (controls.QuietHours is { } quiet)
        {
            if (quiet.Start == quiet.End)
            {
                return FamilyOperationResult<FamilyMember>.Invalid("Quiet hours must start and end at different times.");
            }
            if (quiet.TimeZoneId is not null && !TimeZoneInfo.TryFindSystemTimeZoneById(quiet.TimeZoneId, out _))
            {
                return FamilyOperationResult<FamilyMember>.Invalid($"Unknown time zone '{quiet.TimeZoneId}'.");
            }
        }

        member.LlmControls = new MemberLlmControls
        {
            AllowedProviderIds = allowed,
            PreferredProviderId = preferred,
            DailyBudgetUsd = controls.DailyBudgetUsd,
            MonthlyBudgetUsd = controls.MonthlyBudgetUsd,
            SystemPrompt = systemPrompt,
            BlockedTopics = topics,
            QuietHours = controls.QuietHours,
            Guardrails = controls.Guardrails ?? new MemberGuardrails(),
        };
        member.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        await members.UpsertAsync(member, cancellationToken).ConfigureAwait(false);
        return FamilyOperationResult<FamilyMember>.Ok(member);
    }

    public async Task<FamilyOperationResult<IReadOnlyList<MemberIdentityLink>>> ListIdentitiesAsync(
        string memberId,
        CancellationToken cancellationToken = default)
    {
        if (await members.GetAsync(memberId, cancellationToken).ConfigureAwait(false) is null)
        {
            return FamilyOperationResult<IReadOnlyList<MemberIdentityLink>>.NotFound();
        }

        var identities = await members.ListIdentitiesAsync(memberId, cancellationToken).ConfigureAwait(false);
        return FamilyOperationResult<IReadOnlyList<MemberIdentityLink>>.Ok(identities);
    }

    public async Task<FamilyOperationResult<MemberIdentityLink>> LinkIdentityAsync(
        string memberId,
        LinkIdentityRequest request,
        CancellationToken cancellationToken = default)
    {
        var member = await members.GetAsync(memberId, cancellationToken).ConfigureAwait(false);
        if (member is null)
        {
            return FamilyOperationResult<MemberIdentityLink>.NotFound();
        }

        var subject = request.Subject?.Trim();
        if (string.IsNullOrEmpty(subject))
        {
            return FamilyOperationResult<MemberIdentityLink>.Invalid("An identity subject is required.");
        }

        var issuer = string.Empty;
        switch (request.Kind)
        {
            case MemberIdentityKind.Oidc:
                issuer = request.Issuer?.Trim() ?? string.Empty;
                if (issuer.Length == 0)
                {
                    return FamilyOperationResult<MemberIdentityLink>.Invalid("OIDC links need the token issuer.");
                }
                break;
            case MemberIdentityKind.SpeakerProfile:
                var profile = speakerProfiles is null
                    ? null
                    : await speakerProfiles.GetAsync(subject, cancellationToken).ConfigureAwait(false);
                if (profile is null || profile.IsProvisional)
                {
                    return FamilyOperationResult<MemberIdentityLink>.Invalid("Speaker profile not found or not enrolled.");
                }
                break;
            case MemberIdentityKind.ApiKey:
                var key = await apiKeys.GetKeyAsync(subject, cancellationToken).ConfigureAwait(false);
                if (key is null || key.IsRevoked || key.ExpiresAt <= timeProvider.GetUtcNow().UtcDateTime)
                {
                    return FamilyOperationResult<MemberIdentityLink>.Invalid("API key not found or not active.");
                }
                if (ApiKeyScopes.HasFullAccess(key.Scopes) && member.Role != FamilyRole.Administrator)
                {
                    return FamilyOperationResult<MemberIdentityLink>.Invalid(
                        "Full-access API keys can be linked only to administrator members. Pair a device instead.");
                }
                break;
        }

        var link = new MemberIdentityLink
        {
            MemberId = memberId,
            Kind = request.Kind,
            Issuer = issuer,
            Subject = subject,
            Label = string.IsNullOrWhiteSpace(request.Label) ? null : request.Label.Trim(),
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
        };
        return await members.LinkIdentityAsync(link, cancellationToken).ConfigureAwait(false)
            ? FamilyOperationResult<MemberIdentityLink>.Ok(link)
            : FamilyOperationResult<MemberIdentityLink>.Conflict("That identity is already linked to a family member.");
    }

    public async Task<FamilyOperationResult<bool>> UnlinkIdentityAsync(
        string memberId,
        MemberIdentityKind kind,
        string? issuer,
        string subject,
        CancellationToken cancellationToken = default)
    {
        var normalizedIssuer = kind == MemberIdentityKind.Oidc ? issuer?.Trim() ?? string.Empty : string.Empty;
        var owner = await members.FindByIdentityAsync(kind, normalizedIssuer, subject, cancellationToken).ConfigureAwait(false);
        if (owner?.Id != memberId)
        {
            return FamilyOperationResult<bool>.NotFound("That identity is not linked to this member.");
        }

        if (kind == MemberIdentityKind.ApiKey)
        {
            var revoked = await RevokeKeyAsync(subject, cancellationToken).ConfigureAwait(false);
            if (revoked is not null)
            {
                return FamilyOperationResult<bool>.Conflict(revoked);
            }
        }

        await members.UnlinkIdentityAsync(kind, normalizedIssuer, subject, cancellationToken).ConfigureAwait(false);
        return FamilyOperationResult<bool>.Ok(true);
    }

    public async Task<FamilyOperationResult<MemberUsageSummary>> GetUsageAsync(
        string memberId,
        int recentLimit,
        CancellationToken cancellationToken = default)
    {
        if (await members.GetAsync(memberId, cancellationToken).ConfigureAwait(false) is null)
        {
            return FamilyOperationResult<MemberUsageSummary>.NotFound();
        }

        var today = await usage.GetTotalsAsync(memberId, UsagePeriods.StartOfDayUtc(timeProvider), cancellationToken).ConfigureAwait(false);
        var month = await usage.GetTotalsAsync(memberId, UsagePeriods.StartOfMonthUtc(timeProvider), cancellationToken).ConfigureAwait(false);
        var recent = await usage.ListRecentAsync(memberId, Math.Clamp(recentLimit, 1, 200), cancellationToken).ConfigureAwait(false);
        return FamilyOperationResult<MemberUsageSummary>.Ok(new MemberUsageSummary(today, month, recent));
    }

    private async Task<string?> ApplyProfileAsync(
        FamilyMember member,
        FamilyMemberRequest request,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var name = request.DisplayName?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > MaxDisplayNameLength)
        {
            return $"A display name of 1 to {MaxDisplayNameLength} characters is required.";
        }

        if (request.BirthYear is { } year && (year < 1900 || year > now.Year))
        {
            return "Birth year is out of range.";
        }

        var guardianIds = (request.GuardianIds ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (guardianIds.Contains(member.Id, StringComparer.Ordinal))
        {
            return "A member cannot be their own guardian.";
        }
        if (request.Role == FamilyRole.Child && guardianIds.Count == 0)
        {
            return "A child needs at least one guardian.";
        }
        foreach (var guardianId in guardianIds)
        {
            var guardian = await members.GetAsync(guardianId, cancellationToken).ConfigureAwait(false);
            if (guardian is null || !CanBeGuardian(guardian.Role) || !guardian.IsActive)
            {
                return "Guardians must be active adult or administrator members.";
            }
        }

        member.DisplayName = name;
        member.Role = request.Role;
        member.GuardianIds = guardianIds;
        member.BirthYear = request.BirthYear;
        member.IsActive = request.IsActive ?? member.IsActive;
        member.AccessExpiresAt = request.AccessExpiresAt?.ToUniversalTime();
        member.UpdatedAt = now;
        return null;
    }

    private async Task<string?> RevokeKeyAsync(string keyId, CancellationToken cancellationToken)
    {
        try
        {
            await apiKeys.RevokeKeyAsync(keyId, cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (InvalidOperationException exception)
        {
            return exception.Message;
        }
    }

    private static bool CanBeGuardian(FamilyRole role) => role is FamilyRole.Adult or FamilyRole.Administrator;
}
