using AgentEval.Guardrails;
using AgentEval.Guardrails.Gates;
using lucia.Agents.Abstractions;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Family;

/// <summary>
/// Deterministic checks at the edges of a member's conversation: quiet hours, budgets, and
/// AgentEval guardrail gates on the request and on the final response.
/// </summary>
public sealed partial class MemberConversationPolicy(
    ILlmUsageStore usage,
    TimeProvider timeProvider,
    ILogger<MemberConversationPolicy> logger)
{
    private const string TopicRefusal = "Sorry, that's a topic I can't help with.";

    /// <summary>
    /// Returns a refusal to speak instead of handling the request, or null to continue.
    /// </summary>
    public async Task<string?> CheckRequestAsync(FamilyMember member, string text, CancellationToken cancellationToken = default)
    {
        var controls = member.LlmControls;
        if (controls.QuietHours is { } quiet && QuietHoursEvaluator.IsActive(quiet, timeProvider.GetUtcNow(), timeProvider.LocalTimeZone))
        {
            return Refuse(member, "quiet_hours", "It's quiet hours right now, so I can't help with that until they end.");
        }

        if (controls.DailyBudgetUsd is { } daily)
        {
            var today = await usage.GetTotalsAsync(member.Id, UsagePeriods.StartOfDayUtc(timeProvider), cancellationToken).ConfigureAwait(false);
            if (today.CostUsd >= daily)
            {
                return Refuse(member, "daily_budget", "You've reached today's daily limit. Try again tomorrow.");
            }
        }

        if (controls.MonthlyBudgetUsd is { } monthly)
        {
            var month = await usage.GetTotalsAsync(member.Id, UsagePeriods.StartOfMonthUtc(timeProvider), cancellationToken).ConfigureAwait(false);
            if (month.CostUsd >= monthly)
            {
                return Refuse(member, "monthly_budget", "You've reached this month's monthly limit.");
            }
        }

        if (await BlocksAsync(TopicGate(controls), text, cancellationToken).ConfigureAwait(false))
        {
            return Refuse(member, "blocked_topic", TopicRefusal);
        }

        if (controls.Guardrails.BlockPromptInjection
            && await BlocksAsync(new TokenInjectionGate(), text, cancellationToken).ConfigureAwait(false))
        {
            return Refuse(member, "prompt_injection", "Sorry, I can't help with that request.");
        }

        return null;
    }

    /// <summary>
    /// Applies response gates to the text that will be shown or spoken to the member.
    /// </summary>
    public async Task<string> FilterResponseAsync(FamilyMember member, string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var controls = member.LlmControls;
        if (await BlocksAsync(TopicGate(controls), text, cancellationToken).ConfigureAwait(false))
        {
            Record(member, "blocked_topic_response");
            return TopicRefusal;
        }

        if (controls.Guardrails.RedactPersonalInfo)
        {
            var verdict = await new RegexPiiGate().InspectAsync(text, cancellationToken).ConfigureAwait(false);
            if (verdict.Action == GateAction.Block)
            {
                Record(member, "personal_info_redacted");
                return verdict.RedactedText ?? "Sorry, I can't share that.";
            }
        }

        return text;
    }

    private static IChatGate? TopicGate(MemberLlmControls controls) =>
        controls.BlockedTopics.Count == 0
            ? null
            : new KeywordOracleGate(controls.BlockedTopics.Select(topic => topic.ToLowerInvariant()), "lucia.blocked_topics");

    private static async Task<bool> BlocksAsync(IChatGate? gate, string text, CancellationToken cancellationToken)
    {
        if (gate is null)
        {
            return false;
        }

        // Normalize case on both sides so matching does not depend on the gate's comparison.
        var input = gate is KeywordOracleGate ? text.ToLowerInvariant() : text;
        var verdict = await gate.InspectAsync(input, cancellationToken).ConfigureAwait(false);
        return verdict.Action == GateAction.Block;
    }

    private string Refuse(FamilyMember member, string reason, string message)
    {
        Record(member, reason);
        return message;
    }

    private void Record(FamilyMember member, string reason)
    {
        ActionBrokerTelemetry.Decisions.Add(1, new KeyValuePair<string, object?>("outcome", reason));
        LogPolicyApplied(logger, member.Id, reason);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Member conversation policy for {MemberId}: {Reason}")]
    private static partial void LogPolicyApplied(ILogger logger, string memberId, string reason);
}
