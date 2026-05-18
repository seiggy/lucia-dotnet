using System.Linq;
using System.Text.Json;

using lucia.Agents.Abstractions;
using lucia.Agents.Models;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace lucia.AgentHost.Apis;

/// <summary>
/// Minimal API endpoints for managing per-user memories.
/// </summary>
public static class MemoryApi
{
    /// <summary>
    /// Maps the memory management API endpoints.
    /// </summary>
    public static IEndpointRouteBuilder MapMemoryApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/memory")
            .WithTags("Memory")
            .RequireAuthorization();

        group.MapGet("/{userId}", GetAllAsync);
        group.MapGet("/{userId}/{key}", GetAsync);
        group.MapPut("/{userId}/{key}", PutAsync);
        group.MapDelete("/{userId}/{key}", DeleteAsync);

        return endpoints;
    }

    private static async Task<Ok<IReadOnlyList<MemoryEntry>>> GetAllAsync(
        [FromRoute] string userId,
        [FromServices] IMemoryStore memoryStore,
        CancellationToken ct)
    {
        var memories = await memoryStore.GetAllAsync(userId, ct).ConfigureAwait(false);
        return TypedResults.Ok(memories);
    }

    private static async Task<Results<Ok<MemoryEntry>, NotFound>> GetAsync(
        [FromRoute] string userId,
        [FromRoute] string key,
        [FromServices] IMemoryStore memoryStore,
        CancellationToken ct)
    {
        var memory = (await memoryStore.GetAllAsync(userId, ct).ConfigureAwait(false))
            .FirstOrDefault(entry => string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase));

        return memory is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(memory);
    }

    private static async Task<Results<Ok<MemoryEntry>, BadRequest<string>>> PutAsync(
        [FromRoute] string userId,
        [FromRoute] string key,
        [FromBody] JsonElement body,
        [FromServices] IMemoryStore memoryStore,
        CancellationToken ct)
    {
        if (!TryReadValue(body, out var value))
        {
            return TypedResults.BadRequest("A non-empty 'value' field is required.");
        }

        if (!TryReadTtl(body, out var ttl, out var ttlError))
        {
            return TypedResults.BadRequest(ttlError);
        }

        await memoryStore.StoreAsync(userId, key, value!, ttl, ct).ConfigureAwait(false);
        var storedMemory = (await memoryStore.GetAllAsync(userId, ct).ConfigureAwait(false))
            .First(entry => string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase));

        return TypedResults.Ok(storedMemory);
    }

    private static async Task<Ok<object>> DeleteAsync(
        [FromRoute] string userId,
        [FromRoute] string key,
        [FromServices] IMemoryStore memoryStore,
        CancellationToken ct)
    {
        await memoryStore.DeleteAsync(userId, key, ct).ConfigureAwait(false);
        return TypedResults.Ok<object>(new { deleted = true });
    }

    private static bool TryReadValue(JsonElement body, out string? value)
    {
        value = null;
        if (!body.TryGetProperty("value", out var valueElement) || valueElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = valueElement.GetString();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryReadTtl(JsonElement body, out TimeSpan? ttl, out string error)
    {
        ttl = null;
        error = string.Empty;

        if (body.TryGetProperty("ttl", out var ttlElement))
        {
            if (ttlElement.ValueKind != JsonValueKind.String || !TimeSpan.TryParse(ttlElement.GetString(), out var parsedTtl))
            {
                error = "The optional 'ttl' field must be a valid TimeSpan string.";
                return false;
            }

            ttl = parsedTtl;
            return true;
        }

        if (body.TryGetProperty("ttlSeconds", out var ttlSecondsElement))
        {
            if (ttlSecondsElement.ValueKind != JsonValueKind.Number || !ttlSecondsElement.TryGetDouble(out var ttlSeconds))
            {
                error = "The optional 'ttlSeconds' field must be numeric.";
                return false;
            }

            ttl = TimeSpan.FromSeconds(ttlSeconds);
        }

        return true;
    }
}
