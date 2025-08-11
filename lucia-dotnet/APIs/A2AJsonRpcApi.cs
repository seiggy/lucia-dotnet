using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using lucia.Agents.Registry;
using lucia.Agents.Orchestration;
using A2A;

namespace lucia_dotnet.APIs;

/// <summary>
/// A2A JSON-RPC 2.0 API endpoints implementing the A2A v0.3.0 specification
/// </summary>
public static class A2AJsonRpcApi
{
    public static IEndpointRouteBuilder MapA2AJsonRpcApiV1(this IEndpointRouteBuilder app)
    {
        var vApi = app.NewVersionedApi("A2A");
        var api = vApi.MapGroup("api/agents/{agentId}").HasApiVersion(1, 0);

        // Main JSON-RPC 2.0 endpoint for A2A protocol
        api.MapPost("/v1", HandleJsonRpc)
            .WithName("A2A_JsonRpc")
            .WithSummary("JSON-RPC 2.0 endpoint for A2A protocol communication")
            .WithDescription("Handles A2A protocol messages according to v0.3.0 specification");

        return app;
    }

    private static async Task<IResult> HandleJsonRpc(
        [FromRoute] string agentId,
        [FromBody] JsonDocument request,
        [FromServices] IAgentRegistry agentRegistry,
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
        IAgentRegistry agentRegistry,
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