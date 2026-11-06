using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using lucia.Agents.Abstractions;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Family;

/// <summary>
/// Decides whether a family member may invoke an agent or tool. Rules are deterministic:
/// an explicit member rule, otherwise the role preset. Guardian approvals are durable,
/// bound to the exact arguments, and authorize a single execution.
/// </summary>
public sealed partial class ActionBroker(
    IActionRecordStore actions,
    IFamilyMemberStore members,
    TimeProvider timeProvider,
    ILogger<ActionBroker> logger)
{
    public static readonly TimeSpan DecisionLifetime = TimeSpan.FromHours(1);

    private const int RecentRecordScan = 50;

    public ActionRule Evaluate(FamilyMember member, CapabilityKind kind, string capabilityId)
    {
        var rule = member.CapabilityRules.FirstOrDefault(candidate =>
            candidate.Kind == kind && CapabilityIds.AreEqual(candidate.CapabilityId, capabilityId));
        return rule?.Rule ?? RolePresets.Evaluate(member.Role, kind, capabilityId);
    }

    /// <summary>
    /// True when the member's calls can be anything other than allowed, so every tool call
    /// must pass through the broker (and paths that cannot be mediated must refuse).
    /// </summary>
    public static bool RequiresMediation(FamilyMember member) =>
        member.Role is not (FamilyRole.Administrator or FamilyRole.Adult)
        || member.CapabilityRules.Any(rule => rule.Rule != ActionRule.Allow);

    public async Task<BrokerOutcome> AuthorizeToolAsync(
        FamilyMember member,
        string agentId,
        string toolName,
        IEnumerable<KeyValuePair<string, object?>> arguments,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActionBrokerTelemetry.ActivitySource.StartActivity("ActionBroker.AuthorizeTool");
        activity?.SetTag("agent.id", agentId);
        activity?.SetTag("tool.name", toolName);
        activity?.SetTag("member.role", member.Role.ToString());

        var outcome = await DecideToolAsync(member, agentId, toolName, arguments, cancellationToken).ConfigureAwait(false);

        activity?.SetTag("broker.outcome", outcome.Kind.ToString());
        ActionBrokerTelemetry.Decisions.Add(
            1,
            new KeyValuePair<string, object?>("outcome", outcome.Kind.ToString()),
            new KeyValuePair<string, object?>("role", member.Role.ToString()));
        LogToolDecision(logger, member.Id, agentId, toolName, outcome.Kind);
        return outcome;
    }

    /// <summary>
    /// Records the result of an approved action claimed by <see cref="AuthorizeToolAsync"/>.
    /// </summary>
    public async Task CompleteAsync(ActionRecord claimed, bool succeeded, string? outcome, CancellationToken cancellationToken = default)
    {
        claimed.Status = succeeded ? ActionStatus.Succeeded : ActionStatus.Failed;
        claimed.Outcome = outcome is { Length: > 500 } ? outcome[..500] : outcome;
        claimed.UpdatedAt = Now;
        await actions.TryUpdateAsync(claimed, ActionStatus.Executing, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ApprovalDecisionResult> DecideAsync(
        string actionId,
        string approverId,
        bool approve,
        CancellationToken cancellationToken = default)
    {
        var record = await actions.GetAsync(actionId, cancellationToken).ConfigureAwait(false);
        if (record is null || !record.ApproverIds.Contains(approverId, StringComparer.Ordinal))
        {
            return new ApprovalDecisionResult(ApprovalDecisionStatus.NotFound, null);
        }

        var approver = await members.GetAsync(approverId, cancellationToken).ConfigureAwait(false);
        var now = Now;
        if (approver is null || !approver.IsActive || approver.AccessExpiresAt <= now)
        {
            return new ApprovalDecisionResult(ApprovalDecisionStatus.NotFound, null);
        }

        if (record.Status != ActionStatus.AwaitingDecision)
        {
            return new ApprovalDecisionResult(ApprovalDecisionStatus.AlreadyDecided, record);
        }

        if (record.ExpiresAt <= now)
        {
            record.Status = ActionStatus.Expired;
            record.UpdatedAt = now;
            await actions.TryUpdateAsync(record, ActionStatus.AwaitingDecision, cancellationToken).ConfigureAwait(false);
            return new ApprovalDecisionResult(ApprovalDecisionStatus.Expired, record);
        }

        record.Status = approve ? ActionStatus.Approved : ActionStatus.Denied;
        record.DecidedBy = approverId;
        record.DecidedAt = now;
        record.UpdatedAt = now;
        if (!await actions.TryUpdateAsync(record, ActionStatus.AwaitingDecision, cancellationToken).ConfigureAwait(false))
        {
            return new ApprovalDecisionResult(
                ApprovalDecisionStatus.AlreadyDecided,
                await actions.GetAsync(actionId, cancellationToken).ConfigureAwait(false));
        }

        LogApprovalDecision(logger, actionId, approverId, approve);
        return new ApprovalDecisionResult(ApprovalDecisionStatus.Decided, record);
    }

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    private async Task<BrokerOutcome> DecideToolAsync(
        FamilyMember member,
        string agentId,
        string toolName,
        IEnumerable<KeyValuePair<string, object?>> arguments,
        CancellationToken cancellationToken)
    {
        var rule = Strictest(
            Evaluate(member, CapabilityKind.Agent, agentId),
            Evaluate(member, CapabilityKind.Tool, toolName));
        var toolLabel = CapabilityIds.Normalize(toolName);
        if (rule == ActionRule.Deny)
        {
            return BrokerOutcome.Denied($"{member.DisplayName} is not allowed to use {toolLabel}. Do not try another way to do it.");
        }
        if (rule == ActionRule.Allow)
        {
            return BrokerOutcome.Proceed();
        }

        var argumentsJson = CanonicalArguments(arguments);
        var payloadHash = HashPayload(member.Id, agentId, toolLabel, argumentsJson);
        var now = Now;
        var recent = await actions.ListForRequesterAsync(member.Id, RecentRecordScan, cancellationToken).ConfigureAwait(false);
        foreach (var candidate in recent.Where(record => record.PayloadHash == payloadHash && record.ExpiresAt > now))
        {
            if (candidate.Status == ActionStatus.Approved)
            {
                candidate.Status = ActionStatus.Executing;
                candidate.UpdatedAt = now;
                if (await actions.TryUpdateAsync(candidate, ActionStatus.Approved, cancellationToken).ConfigureAwait(false))
                {
                    return BrokerOutcome.Proceed(candidate);
                }
            }
            else if (candidate.Status == ActionStatus.AwaitingDecision)
            {
                return BrokerOutcome.Pending(PendingMessage(candidate), candidate);
            }
        }

        var approverIds = new List<string>();
        foreach (var guardianId in member.GuardianIds)
        {
            var guardian = await members.GetAsync(guardianId, cancellationToken).ConfigureAwait(false);
            if (guardian is { IsActive: true } && (guardian.AccessExpiresAt is null || guardian.AccessExpiresAt > now))
            {
                approverIds.Add(guardian.Id);
            }
        }
        if (approverIds.Count == 0)
        {
            return BrokerOutcome.Denied($"{toolLabel} needs a guardian's approval, but {member.DisplayName} has no active guardian.");
        }

        var created = new ActionRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            RequesterId = member.Id,
            Kind = CapabilityKind.Tool,
            CapabilityId = toolLabel,
            AgentId = agentId,
            ArgumentsJson = argumentsJson,
            PayloadHash = payloadHash,
            Summary = $"{member.DisplayName} asked to run {toolLabel} ({agentId}) with {argumentsJson}",
            ApproverIds = approverIds,
            CreatedAt = now,
            UpdatedAt = now,
            ExpiresAt = now + DecisionLifetime,
        };
        await actions.CreateAsync(created, cancellationToken).ConfigureAwait(false);
        return BrokerOutcome.Pending(PendingMessage(created), created);
    }

    private static string PendingMessage(ActionRecord record) =>
        $"This was not done. It needs a guardian's approval, and request {record.Id} is waiting for their decision. "
        + "Tell the user their guardian has been asked and that they can ask again once it is approved.";

    private static ActionRule Strictest(ActionRule left, ActionRule right) =>
        (ActionRule)Math.Max((int)left, (int)right);

    private static string CanonicalArguments(IEnumerable<KeyValuePair<string, object?>> arguments)
    {
        var sorted = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in arguments)
        {
            sorted[key] = value;
        }
        return JsonSerializer.Serialize(sorted);
    }

    private static string HashPayload(string memberId, string agentId, string toolLabel, string argumentsJson) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join('\n', memberId, agentId.ToUpperInvariant(), toolLabel.ToUpperInvariant(), argumentsJson))));

    [LoggerMessage(Level = LogLevel.Information, Message = "Action broker: member {MemberId} {AgentId}/{Tool} -> {Outcome}")]
    private static partial void LogToolDecision(ILogger logger, string memberId, string agentId, string tool, BrokerOutcomeKind outcome);

    [LoggerMessage(Level = LogLevel.Information, Message = "Action {ActionId} decided by {ApproverId}: approved={Approved}")]
    private static partial void LogApprovalDecision(ILogger logger, string actionId, string approverId, bool approved);
}
