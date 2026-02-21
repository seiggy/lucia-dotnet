using lucia.Agents.Auth;
using Microsoft.AspNetCore.Mvc;

namespace lucia.AgentHost.Extensions;

/// <summary>
/// API key management endpoints: list, create, revoke, regenerate.
/// </summary>
public static class ApiKeyManagementApi
{
    public static WebApplication MapApiKeyManagementApi(this WebApplication app)
    {
        var group = app.MapGroup("/api/keys")
            .WithTags("API Key Management")
            .RequireAuthorization();

        group.MapGet("/", ListKeysAsync);
        group.MapPost("/", CreateKeyAsync);
        group.MapDelete("/{id}", RevokeKeyAsync);
        group.MapPost("/{id}/regenerate", RegenerateKeyAsync);

        return app;
    }

    private static async Task<IResult> ListKeysAsync(
        IApiKeyService apiKeyService,
        HttpContext httpContext)
    {
        var keys = await apiKeyService.ListKeysAsync(httpContext.RequestAborted).ConfigureAwait(false);
        return Results.Ok(keys);
    }

    private static async Task<IResult> CreateKeyAsync(
        [FromBody] CreateKeyRequest request,
        IApiKeyService apiKeyService,
        HttpContext httpContext)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Results.BadRequest(new { error = "Key name is required." });
        }

        var result = await apiKeyService.CreateKeyAsync(request.Name, httpContext.RequestAborted).ConfigureAwait(false);
        return Results.Created($"/api/keys/{result.Id}", result);
    }

    private static async Task<IResult> RevokeKeyAsync(
        string id,
        IApiKeyService apiKeyService,
        HttpContext httpContext)
    {
        try
        {
            var revoked = await apiKeyService.RevokeKeyAsync(id, httpContext.RequestAborted).ConfigureAwait(false);
            if (!revoked)
            {
                return Results.NotFound(new { error = "Key not found or already revoked." });
            }

            return Results.Ok(new { revoked = true });
        }
        catch (InvalidOperationException ex)
        {
            return Results.Conflict(new { error = ex.Message });
        }
    }

    private static async Task<IResult> RegenerateKeyAsync(
        string id,
        IApiKeyService apiKeyService,
        HttpContext httpContext)
    {
        try
        {
            var result = await apiKeyService.RegenerateKeyAsync(id, httpContext.RequestAborted).ConfigureAwait(false);
            return Results.Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Request body for creating a new API key.
    /// </summary>
    public sealed record CreateKeyRequest(string Name);
}
