using lucia.Agents.Family;

namespace lucia.AgentHost.Family;

public sealed record MemberUsageSummary(LlmUsageTotals Today, LlmUsageTotals Month, IReadOnlyList<LlmUsageRecord> Recent);
