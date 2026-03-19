using lucia.AgentHost.Conversation.Templates;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace lucia.AgentHost.Apis;

/// <summary>
/// Minimal API endpoints for response template CRUD and reset-to-defaults.
/// </summary>
public static class ResponseTemplateApi
{
    /// <summary>
    /// Maps the <c>/api/response-templates</c> endpoint group.
    /// </summary>
    public static IEndpointRouteBuilder MapResponseTemplateApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/response-templates")
            .WithTags("Response Templates")
            .RequireAuthorization();

        group.MapGet("/", ListAllAsync);
        group.MapGet("/{id}", GetByIdAsync);
        group.MapPost("/", CreateAsync);
        group.MapPut("/{id}", UpdateAsync);
        group.MapDelete("/{id}", DeleteAsync);
        group.MapPost("/reset", ResetToDefaultsAsync);

        return endpoints;
    }

    private static async Task<Ok<IReadOnlyList<ResponseTemplate>>> ListAllAsync(
        [FromServices] IResponseTemplateRepository repo,
        CancellationToken ct)
    {
        var templates = await repo.GetAllAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(templates);
    }

    private static async Task<Results<Ok<ResponseTemplate>, NotFound>> GetByIdAsync(
        [FromRoute] string id,
        [FromServices] IResponseTemplateRepository repo,
        CancellationToken ct)
    {
        var template = await repo.GetByIdAsync(id, ct).ConfigureAwait(false);
        return template is not null
            ? TypedResults.Ok(template)
            : TypedResults.NotFound();
    }

    private static async Task<Results<Created<ResponseTemplate>, BadRequest<string>, Conflict<string>>> CreateAsync(
        [FromBody] CreateResponseTemplateRequest request,
        [FromServices] IResponseTemplateRepository repo,
        [FromServices] ResponseTemplateRenderer renderer,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.SkillId))
            return TypedResults.BadRequest("SkillId is required");

        if (string.IsNullOrWhiteSpace(request.Action))
            return TypedResults.BadRequest("Action is required");

        if (request.Templates is null || request.Templates.Length == 0)
            return TypedResults.BadRequest("At least one template string is required");

        var template = new ResponseTemplate
        {
            SkillId = request.SkillId,
            Action = request.Action,
            Templates = request.Templates,
            IsDefault = false
        };

        try
        {
            var created = await repo.CreateAsync(template, ct).ConfigureAwait(false);
            renderer.InvalidateCache();
            return TypedResults.Created($"/api/response-templates/{created.Id}", created);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return TypedResults.Conflict($"A template for {request.SkillId}/{request.Action} already exists");
        }
    }

    private static async Task<Results<Ok<ResponseTemplate>, NotFound, BadRequest<string>>> UpdateAsync(
        [FromRoute] string id,
        [FromBody] UpdateResponseTemplateRequest request,
        [FromServices] IResponseTemplateRepository repo,
        [FromServices] ResponseTemplateRenderer renderer,
        CancellationToken ct)
    {
        var existing = await repo.GetByIdAsync(id, ct).ConfigureAwait(false);
        if (existing is null)
            return TypedResults.NotFound();

        if (request.Templates is not null && request.Templates.Length == 0)
            return TypedResults.BadRequest("Templates array must not be empty");

        // Build the updated document, preserving immutable fields
        var updated = new ResponseTemplate
        {
            Id = existing.Id,
            SkillId = request.SkillId ?? existing.SkillId,
            Action = request.Action ?? existing.Action,
            Templates = request.Templates ?? existing.Templates,
            IsDefault = existing.IsDefault,
            CreatedAt = existing.CreatedAt,
            UpdatedAt = DateTime.UtcNow
        };

        var result = await repo.UpdateAsync(id, updated, ct).ConfigureAwait(false);
        renderer.InvalidateCache();
        return TypedResults.Ok(result);
    }

    private static async Task<Results<NoContent, NotFound>> DeleteAsync(
        [FromRoute] string id,
        [FromServices] IResponseTemplateRepository repo,
        [FromServices] ResponseTemplateRenderer renderer,
        CancellationToken ct)
    {
        var deleted = await repo.DeleteAsync(id, ct).ConfigureAwait(false);
        if (deleted) renderer.InvalidateCache();
        return deleted
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }

    private static async Task<Ok<string>> ResetToDefaultsAsync(
        [FromServices] IResponseTemplateRepository repo,
        [FromServices] ResponseTemplateRenderer renderer,
        CancellationToken ct)
    {
        await repo.ResetToDefaultsAsync(ct).ConfigureAwait(false);
        renderer.InvalidateCache();
        return TypedResults.Ok("Templates reset to defaults");
    }
}
