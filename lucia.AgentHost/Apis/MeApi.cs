using System.Security.Claims;
using System.Text.Json;
using lucia.AgentHost.Auth;
using lucia.AgentHost.Family;
using lucia.Agents.Abstractions;
using lucia.Agents.Auth;
using lucia.Agents.Family;

namespace lucia.AgentHost.Apis;

/// <summary>
/// Endpoints for the signed-in family member's own apps: profile, guardian approvals, the
/// member's own requests, and usage. Conversation is mapped by <see cref="ConversationApi"/>.
/// </summary>
public static class MeApi
{
    public static WebApplication MapMeApi(this WebApplication app)
    {
        var group = app.MapGroup("/api/me")
            .WithTags("Family Member")
            .RequireAuthorization(LuciaAuthorization.MemberPolicy);

        group.MapGet("/", GetAsync);
        group.MapGet("/approvals", ListApprovalsAsync);
        group.MapPost("/approvals/{actionId}/approve", (string actionId, ClaimsPrincipal user, ActionBroker broker, IFamilyMemberStore members, HttpContext http) =>
            DecideAsync(actionId, approve: true, user, broker, members, http));
        group.MapPost("/approvals/{actionId}/deny", (string actionId, ClaimsPrincipal user, ActionBroker broker, IFamilyMemberStore members, HttpContext http) =>
            DecideAsync(actionId, approve: false, user, broker, members, http));
        group.MapGet("/requests", ListRequestsAsync);
        group.MapGet("/usage", GetUsageAsync);

        return app;
    }

    private static string MemberId(ClaimsPrincipal user) => user.FindFirstValue(LuciaClaims.MemberId)!;

    private static async Task<IResult> GetAsync(
        ClaimsPrincipal user,
        IFamilyMemberStore members,
        HttpContext httpContext)
    {
        var member = await members.GetAsync(MemberId(user), httpContext.RequestAborted).ConfigureAwait(false);
        if (member is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(new
        {
            id = member.Id,
            displayName = member.DisplayName,
            role = member.Role,
            guardianIds = member.GuardianIds,
        });
    }

    private static async Task<IResult> ListApprovalsAsync(
        ClaimsPrincipal user,
        IActionRecordStore actions,
        IFamilyMemberStore members,
        TimeProvider timeProvider,
        HttpContext httpContext)
    {
        var ct = httpContext.RequestAborted;
        var pending = await actions.ListAwaitingDecisionAsync(MemberId(user), ct).ConfigureAwait(false);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var views = new List<object>(pending.Count);
        foreach (var record in pending.Where(record => record.ExpiresAt > now))
        {
            views.Add(await ToViewAsync(record, members, ct).ConfigureAwait(false));
        }
        return Results.Ok(views);
    }

    private static async Task<IResult> DecideAsync(
        string actionId,
        bool approve,
        ClaimsPrincipal user,
        ActionBroker broker,
        IFamilyMemberStore members,
        HttpContext httpContext)
    {
        var ct = httpContext.RequestAborted;
        var result = await broker.DecideAsync(actionId, MemberId(user), approve, ct).ConfigureAwait(false);
        return result.Status switch
        {
            ApprovalDecisionStatus.Decided => Results.Ok(await ToViewAsync(result.Record!, members, ct).ConfigureAwait(false)),
            ApprovalDecisionStatus.Expired => Results.Conflict(new { error = "This request has expired." }),
            ApprovalDecisionStatus.AlreadyDecided => Results.Conflict(new { error = "This request has already been decided." }),
            _ => Results.NotFound(),
        };
    }

    private static async Task<IResult> ListRequestsAsync(
        ClaimsPrincipal user,
        int? limit,
        IActionRecordStore actions,
        IFamilyMemberStore members,
        HttpContext httpContext)
    {
        var ct = httpContext.RequestAborted;
        var records = await actions
            .ListForRequesterAsync(MemberId(user), Math.Clamp(limit ?? 25, 1, 100), ct)
            .ConfigureAwait(false);
        var views = new List<object>(records.Count);
        foreach (var record in records)
        {
            views.Add(await ToViewAsync(record, members, ct).ConfigureAwait(false));
        }
        return Results.Ok(views);
    }

    private static async Task<IResult> GetUsageAsync(
        ClaimsPrincipal user,
        int? limit,
        FamilyMemberService family,
        IFamilyMemberStore members,
        HttpContext httpContext)
    {
        var ct = httpContext.RequestAborted;
        var memberId = MemberId(user);
        var member = await members.GetAsync(memberId, ct).ConfigureAwait(false);
        var usage = await family.GetUsageAsync(memberId, limit ?? 25, ct).ConfigureAwait(false);
        if (member is null || usage.Value is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(new
        {
            today = usage.Value.Today,
            month = usage.Value.Month,
            dailyBudgetUsd = member.LlmControls.DailyBudgetUsd,
            monthlyBudgetUsd = member.LlmControls.MonthlyBudgetUsd,
            recent = usage.Value.Recent,
        });
    }

    private static async Task<object> ToViewAsync(ActionRecord record, IFamilyMemberStore members, CancellationToken ct)
    {
        var requester = await members.GetAsync(record.RequesterId, ct).ConfigureAwait(false);
        using var arguments = JsonDocument.Parse(record.ArgumentsJson);
        return new
        {
            id = record.Id,
            requester = new { id = record.RequesterId, displayName = requester?.DisplayName },
            capabilityId = record.CapabilityId,
            agentId = record.AgentId,
            arguments = arguments.RootElement.Clone(),
            summary = record.Summary,
            status = record.Status,
            createdAt = record.CreatedAt,
            expiresAt = record.ExpiresAt,
            decidedBy = record.DecidedBy,
            decidedAt = record.DecidedAt,
        };
    }
}
