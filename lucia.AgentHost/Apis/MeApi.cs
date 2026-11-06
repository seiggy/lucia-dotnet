using System.Security.Claims;
using lucia.AgentHost.Auth;
using lucia.Agents.Abstractions;
using lucia.Agents.Auth;

namespace lucia.AgentHost.Apis;

/// <summary>
/// Endpoints for the signed-in family member's own apps.
/// </summary>
public static class MeApi
{
    public static WebApplication MapMeApi(this WebApplication app)
    {
        var group = app.MapGroup("/api/me")
            .WithTags("Family Member")
            .RequireAuthorization(LuciaAuthorization.MemberPolicy);

        group.MapGet("/", GetAsync);

        return app;
    }

    private static async Task<IResult> GetAsync(
        ClaimsPrincipal user,
        IFamilyMemberStore members,
        HttpContext httpContext)
    {
        var memberId = user.FindFirstValue(LuciaClaims.MemberId);
        var member = memberId is null
            ? null
            : await members.GetAsync(memberId, httpContext.RequestAborted).ConfigureAwait(false);
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
}
