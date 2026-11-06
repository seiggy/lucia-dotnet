using lucia.AgentHost.Family;
using lucia.Agents.Auth;
using lucia.Agents.Family;
using Microsoft.AspNetCore.Mvc;

namespace lucia.AgentHost.Apis;

/// <summary>
/// Administrator management of family members, their permissions, LLM controls, linked
/// identities, and usage.
/// </summary>
public static class FamilyApi
{
    public static WebApplication MapFamilyApi(this WebApplication app, bool requireAdministrator)
    {
        var group = app.MapGroup("/api/family/members")
            .WithTags("Family Members");
        if (requireAdministrator)
        {
            group.RequireAuthorization(AuthOptions.AdministratorPolicy);
        }
        else
        {
            group.RequireAuthorization();
        }

        group.MapGet("/", async (FamilyMemberService family, HttpContext http) =>
            Results.Ok(await family.ListAsync(http.RequestAborted).ConfigureAwait(false)));
        group.MapPost("/", async ([FromBody] FamilyMemberRequest request, FamilyMemberService family, HttpContext http) =>
            (await family.CreateAsync(request, http.RequestAborted).ConfigureAwait(false))
                .ToHttpResult(member => Results.Created($"/api/family/members/{member.Id}", member)));
        group.MapGet("/{memberId}", async (string memberId, FamilyMemberService family, HttpContext http) =>
            (await family.GetAsync(memberId, http.RequestAborted).ConfigureAwait(false)).ToHttpResult());
        group.MapPut("/{memberId}", async (string memberId, [FromBody] FamilyMemberRequest request, FamilyMemberService family, HttpContext http) =>
            (await family.UpdateAsync(memberId, request, http.RequestAborted).ConfigureAwait(false)).ToHttpResult());
        group.MapDelete("/{memberId}", async (string memberId, FamilyMemberService family, HttpContext http) =>
            (await family.DeleteAsync(memberId, http.RequestAborted).ConfigureAwait(false))
                .ToHttpResult(_ => Results.NoContent()));

        group.MapGet("/{memberId}/permissions", async (string memberId, FamilyMemberService family, HttpContext http) =>
            (await family.GetAsync(memberId, http.RequestAborted).ConfigureAwait(false))
                .ToHttpResult(member => Results.Ok(member.CapabilityRules)));
        group.MapPut("/{memberId}/permissions", async (string memberId, [FromBody] List<CapabilityRule> rules, FamilyMemberService family, HttpContext http) =>
            (await family.SetCapabilityRulesAsync(memberId, rules, http.RequestAborted).ConfigureAwait(false))
                .ToHttpResult(member => Results.Ok(member.CapabilityRules)));

        group.MapGet("/{memberId}/llm-controls", async (string memberId, FamilyMemberService family, HttpContext http) =>
            (await family.GetAsync(memberId, http.RequestAborted).ConfigureAwait(false))
                .ToHttpResult(member => Results.Ok(member.LlmControls)));
        group.MapPut("/{memberId}/llm-controls", async (string memberId, [FromBody] MemberLlmControls controls, FamilyMemberService family, HttpContext http) =>
            (await family.SetLlmControlsAsync(memberId, controls, http.RequestAborted).ConfigureAwait(false))
                .ToHttpResult(member => Results.Ok(member.LlmControls)));

        group.MapGet("/{memberId}/identities", async (string memberId, FamilyMemberService family, HttpContext http) =>
            (await family.ListIdentitiesAsync(memberId, http.RequestAborted).ConfigureAwait(false)).ToHttpResult());
        group.MapPost("/{memberId}/identities", async (string memberId, [FromBody] LinkIdentityRequest request, FamilyMemberService family, HttpContext http) =>
            (await family.LinkIdentityAsync(memberId, request, http.RequestAborted).ConfigureAwait(false)).ToHttpResult());
        group.MapDelete("/{memberId}/identities", async (
                string memberId,
                [FromQuery] MemberIdentityKind kind,
                [FromQuery] string subject,
                [FromQuery] string? issuer,
                FamilyMemberService family,
                HttpContext http) =>
            (await family.UnlinkIdentityAsync(memberId, kind, issuer, subject, http.RequestAborted).ConfigureAwait(false))
                .ToHttpResult(_ => Results.NoContent()));

        group.MapGet("/{memberId}/usage", async (string memberId, [FromQuery] int? limit, FamilyMemberService family, HttpContext http) =>
            (await family.GetUsageAsync(memberId, limit ?? 50, http.RequestAborted).ConfigureAwait(false)).ToHttpResult());

        return app;
    }
}
