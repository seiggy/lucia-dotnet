using lucia.Agents.Abstractions;
using lucia.Agents.Configuration;
using lucia.Agents.Mcp;
using lucia.Agents.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace lucia.AgentHost.Extensions;

/// <summary>
/// CRUD endpoints for MCP tool server management.
/// </summary>
public static class McpServerApi
{
    public static IEndpointRouteBuilder MapMcpServerApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/mcp-servers")
            .WithTags("MCP Servers")
            .RequireAuthorization();

        group.MapGet("/", ListServersAsync);
        group.MapGet("/status", GetStatusesAsync);
        group.MapGet("/{id}", GetServerAsync);
        group.MapPost("/", CreateServerAsync);
        group.MapPut("/{id}", UpdateServerAsync);
        group.MapDelete("/{id}", DeleteServerAsync);
        group.MapPost("/{id}/tools", DiscoverToolsAsync);
        group.MapPost("/{id}/connect", ConnectServerAsync);
        group.MapPost("/{id}/disconnect", DisconnectServerAsync);

        return endpoints;
    }

    private static async Task<Ok<List<McpToolServerDefinition>>> ListServersAsync(
        [FromServices] IAgentDefinitionRepository repository)
    {
        var servers = await repository.GetAllToolServersAsync().ConfigureAwait(false);
        return TypedResults.Ok(servers);
    }

    private static async Task<Results<Ok<McpToolServerDefinition>, NotFound>> GetServerAsync(
        string id,
        [FromServices] IAgentDefinitionRepository repository)
    {
        var server = await repository.GetToolServerAsync(id).ConfigureAwait(false);
        return server is not null
            ? TypedResults.Ok(server)
            : TypedResults.NotFound();
    }

    private static async Task<Created<McpToolServerDefinition>> CreateServerAsync(
        [FromBody] McpToolServerDefinition server,
        [FromServices] IAgentDefinitionRepository repository)
    {
        server.CreatedAt = DateTime.UtcNow;
        server.UpdatedAt = DateTime.UtcNow;
        await repository.UpsertToolServerAsync(server).ConfigureAwait(false);
        return TypedResults.Created($"/api/mcp-servers/{server.Id}", server);
    }

    private static async Task<Results<Ok<McpToolServerDefinition>, NotFound>> UpdateServerAsync(
        string id,
        [FromBody] McpToolServerDefinition server,
        [FromServices] IAgentDefinitionRepository repository)
    {
        var existing = await repository.GetToolServerAsync(id).ConfigureAwait(false);
        if (existing is null) return TypedResults.NotFound();

        server.Id = id;
        server.CreatedAt = existing.CreatedAt;
        await repository.UpsertToolServerAsync(server).ConfigureAwait(false);
        return TypedResults.Ok(server);
    }

    private static async Task<Results<NoContent, NotFound>> DeleteServerAsync(
        string id,
        [FromServices] IAgentDefinitionRepository repository)
    {
        var existing = await repository.GetToolServerAsync(id).ConfigureAwait(false);
        if (existing is null) return TypedResults.NotFound();

        await repository.DeleteToolServerAsync(id).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<IReadOnlyList<McpToolInfo>>, NotFound>> DiscoverToolsAsync(
        string id,
        [FromServices] IMcpToolRegistry toolRegistry)
    {
        var tools = await toolRegistry.GetAvailableToolsAsync(id).ConfigureAwait(false);
        return TypedResults.Ok(tools);
    }

    private static async Task<Results<Ok<string>, ProblemHttpResult>> ConnectServerAsync(
        string id,
        [FromServices] IMcpToolRegistry toolRegistry,
        CancellationToken cancellationToken)
    {
        // MCP connect can hang if server is unreachable; enforce 30s timeout
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        try
        {
            await toolRegistry.ConnectServerAsync(id, cts.Token).ConfigureAwait(false);
            return TypedResults.Ok("Connected");
        }
        catch (OperationCanceledException) when (cts.Token.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return TypedResults.Problem(
                detail: "Connection timed out after 30 seconds. The MCP server may be unreachable (check URL, network, and that MetaMCP is running).",
                statusCode: 504,
                title: "MCP connection timeout");
        }
        catch (Exception ex)
        {
            return TypedResults.Problem(
                detail: ex.Message,
                statusCode: 500,
                title: "Failed to connect MCP server");
        }
    }

    private static async Task<Ok<string>> DisconnectServerAsync(
        string id,
        [FromServices] IMcpToolRegistry toolRegistry)
    {
        await toolRegistry.DisconnectServerAsync(id).ConfigureAwait(false);
        return TypedResults.Ok("Disconnected");
    }

    private static Task<Ok<IReadOnlyDictionary<string, McpServerStatus>>> GetStatusesAsync(
        [FromServices] IMcpToolRegistry toolRegistry)
    {
        var statuses = toolRegistry.GetServerStatuses();
        return Task.FromResult(TypedResults.Ok(statuses));
    }
}
