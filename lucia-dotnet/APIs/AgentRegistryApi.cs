using A2A;
using lucia.Agents.Orchestration;
using lucia.Agents.Registry;
using lucia.Agents.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using A2A.AspNetCore;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting.A2A;

namespace lucia_dotnet.APIs;

public static class AgentRegistryApi
{
    public static IEndpointRouteBuilder MapAgentRegistryApiV1(this WebApplication app)
    {
        var vApi = app.NewVersionedApi("Agents");

        var api = vApi.MapGroup("api").HasApiVersion(1, 0);
        
        api.MapGet("agents", GetAgents);
        api.MapPost("agents/register", RegisterAgent);
        api.MapPut("agents/{agentId}", UpdateAgent);
        api.MapDelete("agents/{agentId}", UnregisterAgent);
        // A2A well-known agent card endpoint (serves this host's primary orchestrator card)
        api.MapGet("agents/{agentId}/.well-known/agent-card.json", GetLocalAgentCard)
            .WithName("A2A_WellKnownAgentCard")
            .WithSummary("Returns the local orchestrator's AgentCard per A2A spec")
            .WithDescription("Serves the primary agent card used by remote registries and clients.");

        api.MapPost("agents/{agentId}", HandleJsonRpc)
            .WithName("A2A_JsonRpc")
            .WithSummary("JSON-RPC 2.0 endpoint for A2A protocol communication")
            .WithDescription("Handles A2A protocol messages according to v0.3.0 specification");
        return app;
    }
    
    private static async Task<Results<Ok<AgentCard>, ProblemHttpResult>> GetLocalAgentCard(
        [FromServices] AgentRegistry agentRegistry,
        [FromRoute] string agentId,
        CancellationToken cancellationToken = default)
    {
        // For now return the first registered agent that represents the orchestrator/light agent.
        // Later we may mark a specific AgentCard as primary.
        await foreach (var agent in agentRegistry.GetAgentsAsync(cancellationToken))
        {
            var agentPath = agent.Url.TrimStart('/').TrimEnd('/');
            if (agentId == agentPath)
            {
                return TypedResults.Ok(agent);
            }
        }
        return TypedResults.Problem("No agents registered", statusCode: 404);
    }

    private static async Task<Results<Ok, BadRequest<string>, ProblemHttpResult>> UnregisterAgent(
        [FromServices] AgentRegistry agentRegistry,
        [FromRoute] string agentId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(agentId))
        {
            return TypedResults.BadRequest("Agent URI must be provided");
        }

        // unregister the agent from the registry
        await agentRegistry.UnregisterAgentAsync(agentId, cancellationToken);
        return TypedResults.Ok();
    }

    private static async Task<Results<Ok, BadRequest<string>, ProblemHttpResult>> UpdateAgent(
        [FromServices] AgentRegistry agentRegistry,
        [FromServices] IA2AClientService a2aService,
        [FromRoute] string agentId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(agentId))
        {
            return TypedResults.BadRequest("Agent URI must be provided");
        }
        // get the agent from the registry
        var agent = await agentRegistry.GetAgentAsync(agentId, cancellationToken);
        if (agent == null)
        {
            return TypedResults.Problem(detail: $"Agent not found: {agentId}", statusCode: 404);
        }
        // downlaod the latest agent from the provided URI
        var updatedAgent = await a2aService.DownloadAgentCardAsync(agentId, cancellationToken);
        if (updatedAgent == null)
        {
            return TypedResults.Problem(detail: $"Agent not found at URI: {agentId}", statusCode: 404);
        }
        // update the agent in the registry
        await agentRegistry.RegisterAgentAsync(updatedAgent, cancellationToken);
        return TypedResults.Ok();
    }

    public static Task<IAsyncEnumerable<AgentCard>> GetAgents(
        [FromServices] AgentRegistry agentRegistry,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(agentRegistry.GetAgentsAsync(cancellationToken));
    }

    public static async Task<Results<Created, BadRequest<string>, ProblemHttpResult>> RegisterAgent(
        [FromServices] AgentRegistry agentRegistry,
        [FromServices] IA2AClientService a2aService,
        [FromForm] string agentId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(agentId))
        {
            return TypedResults.BadRequest("Agent URI must be provided");
        }

        // download the agent from the provided URI
        var agent = await a2aService.DownloadAgentCardAsync(agentId, cancellationToken);
        if (agent == null)
        {
            return TypedResults.Problem(detail: $"Agent not found at URI: {agentId}", statusCode: 404);
        }
        // register the agent with the registry
        await agentRegistry.RegisterAgentAsync(agent, cancellationToken);
        return TypedResults.Created(agentId);
    }

    private static async Task<IResult> HandleJsonRpc(
        [FromRoute] string agentId,
        [FromBody] JsonDocument request,
        [FromServices] AgentRegistry agentRegistry,
        [FromServices] LuciaOrchestrator orchestrator,
        [FromServices] ILogger<Program> logger,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Validate JSON-RPC 2.0 structure
            if (!request.RootElement.TryGetProperty("jsonrpc", out var jsonrpcElement) ||
                jsonrpcElement.GetString() != "2.0")
            {
                return Results.BadRequest(CreateErrorResponse(null, -32600, "Invalid Request", "Missing or invalid 'jsonrpc' field"));
            }

            if (!request.RootElement.TryGetProperty("method", out var methodElement))
            {
                return Results.BadRequest(CreateErrorResponse(GetRequestId(request.RootElement), -32600, "Invalid Request", "Missing 'method' field"));
            }

            var method = methodElement.GetString();
            var requestId = GetRequestId(request.RootElement);

            logger.LogInformation("Received JSON-RPC request for agent {AgentId}, method {Method}", agentId, method);

            // Find the agent
            var agent = await agentRegistry.GetAgentAsync(agentId, cancellationToken);
            if (agent == null)
            {
                return Results.NotFound(CreateErrorResponse(requestId, -32601, "Method not found", $"Agent not found: {agentId}"));
            }

            // Route based on method
            return method switch
            {
                "message/send" => await HandleMessageSend(request.RootElement, requestId, agentId, agentRegistry, orchestrator, logger, cancellationToken),
                "message/stream" => await HandleMessageStream(request.RootElement, requestId, logger),
                "tasks/get" => await HandleTaskGet(request.RootElement, requestId),
                "tasks/cancel" => await HandleTaskCancel(request.RootElement, requestId),
                _ => Results.BadRequest(CreateErrorResponse(requestId, -32601, "Method not found", $"Unsupported method: {method}"))
            };
        }
        catch (JsonException)
        {
            return Results.BadRequest(CreateErrorResponse(null, -32700, "Parse error", "Invalid JSON payload"));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing JSON-RPC request for agent {AgentId}", agentId);
            return Results.StatusCode(500);
        }
    }

    private static async Task<IResult> HandleMessageSend(
        JsonElement request,
        object? requestId,
        string agentId,
        AgentRegistry agentRegistry,
        LuciaOrchestrator orchestrator,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        if (!request.TryGetProperty("params", out var paramsElement))
        {
            return Results.BadRequest(CreateErrorResponse(requestId, -32602, "Invalid params", "Missing 'params' field"));
        }

        if (!paramsElement.TryGetProperty("message", out var messageElement))
        {
            return Results.BadRequest(CreateErrorResponse(requestId, -32602, "Invalid params", "Missing 'message' field in params"));
        }

        if (!messageElement.TryGetProperty("parts", out var partsElement) ||
            partsElement.ValueKind != JsonValueKind.Array ||
            partsElement.GetArrayLength() == 0)
        {
            return Results.BadRequest(CreateErrorResponse(requestId, -32602, "Invalid params", "Missing or empty 'parts' array in message"));
        }

        // Extract the text content from the first text part
        var textContent = "";
        foreach (var part in partsElement.EnumerateArray())
        {
            if (part.TryGetProperty("kind", out var kindElement) &&
                kindElement.GetString() == "text" &&
                part.TryGetProperty("text", out var textElement))
            {
                textContent = textElement.GetString() ?? "";
                break;
            }
        }

        if (string.IsNullOrEmpty(textContent))
        {
            return Results.BadRequest(CreateErrorResponse(requestId, -32602, "Invalid params", "No text content found in message parts"));
        }

        try
        {
            // Process the message through the orchestrator
            var response = await orchestrator.ProcessRequestAsync(textContent, cancellationToken);

            // Generate response IDs
            var messageId = Guid.NewGuid().ToString();
            var contextId = request.TryGetProperty("params", out var p) &&
                           p.TryGetProperty("message", out var m) &&
                           m.TryGetProperty("contextId", out var cid)
                           ? cid.GetString()
                           : Guid.NewGuid().ToString();

            // Return a synchronous message response per A2A spec
            var result = new
            {
                messageId,
                contextId,
                parts = new[]
                {
                    new { kind = "text", text = response }
                },
                kind = "message",
                metadata = new { }
            };

            return Results.Ok(CreateSuccessResponse(requestId, result));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing message for agent {AgentId}", agentId);
            return Results.StatusCode(500);
        }
    }

    private static Task<IResult> HandleMessageStream(JsonElement request, object? requestId, ILogger<Program> logger)
    {
        // For now, return error as streaming is not yet implemented
        return Task.FromResult(
            Results.BadRequest(CreateErrorResponse(requestId, -32004, "UnsupportedOperationError", "Streaming is not yet implemented"))
        );
    }

    private static Task<IResult> HandleTaskGet(JsonElement request, object? requestId)
    {
        // For now, return error as task management is not yet implemented
        return Task.FromResult(
            Results.BadRequest(CreateErrorResponse(requestId, -32001, "TaskNotFoundError", "Task management is not yet implemented"))
        );
    }

    private static Task<IResult> HandleTaskCancel(JsonElement request, object? requestId)
    {
        // For now, return error as task management is not yet implemented
        return Task.FromResult(
            Results.BadRequest(CreateErrorResponse(requestId, -32002, "TaskNotCancelableError", "Task management is not yet implemented"))
        );
    }

    private static object? GetRequestId(JsonElement request)
    {
        if (!request.TryGetProperty("id", out var idElement))
            return null;

        return idElement.ValueKind switch
        {
            JsonValueKind.String => idElement.GetString(),
            JsonValueKind.Number => idElement.GetInt64(),
            JsonValueKind.Null => null,
            _ => null
        };
    }

    private static object CreateErrorResponse(object? id, int code, string message, string? detail = null)
    {
        return new
        {
            jsonrpc = "2.0",
            id,
            error = new
            {
                code,
                message,
                data = detail
            }
        };
    }

    private static object CreateSuccessResponse(object? id, object result)
    {
        return new
        {
            jsonrpc = "2.0",
            id,
            result
        };
    }
}
