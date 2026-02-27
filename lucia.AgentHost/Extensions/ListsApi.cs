using System.Text.Json.Serialization;
using lucia.HomeAssistant.Models;
using lucia.HomeAssistant.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace lucia.AgentHost.Extensions;

/// <summary>
/// API for Home Assistant shopping list and todo lists.
/// </summary>
public static class ListsApi
{
    public static IEndpointRouteBuilder MapListsApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api")
            .WithTags("Lists")
            .RequireAuthorization();

        // Shopping list
        group.MapGet("/shopping-list", GetShoppingListAsync);
        group.MapPost("/shopping-list", AddShoppingItemAsync);
        group.MapPost("/shopping-list/complete", CompleteShoppingItemAsync);
        group.MapPost("/shopping-list/remove", RemoveShoppingItemAsync);

        // Todo lists
        group.MapGet("/todo-lists", GetTodoEntitiesAsync);
        group.MapGet("/todo-lists/{entityId}", GetTodoItemsAsync);
        group.MapPost("/todo-lists/{entityId}", AddTodoItemAsync);
        group.MapPost("/todo-lists/{entityId}/complete", CompleteTodoItemAsync);
        group.MapPost("/todo-lists/{entityId}/remove", RemoveTodoItemAsync);

        return endpoints;
    }

    private static async Task<Ok<ShoppingListItem[]>> GetShoppingListAsync(
        [FromServices] IHomeAssistantClient ha,
        CancellationToken ct)
    {
        var items = await ha.GetShoppingListItemsAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(items);
    }

    private static async Task<Results<Ok, BadRequest<string>>> AddShoppingItemAsync(
        [FromBody] AddItemRequest req,
        [FromServices] IHomeAssistantClient ha,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return TypedResults.BadRequest("Name is required");
        await ha.CallServiceAsync("shopping_list", "add_item", request: new ServiceCallRequest { { "name", req.Name.Trim() } }, cancellationToken: ct).ConfigureAwait(false);
        return TypedResults.Ok();
    }

    private static async Task<Results<Ok, BadRequest<string>>> CompleteShoppingItemAsync(
        [FromBody] ItemRequest req,
        [FromServices] IHomeAssistantClient ha,
        CancellationToken ct)
    {
        var key = req.Name ?? req.Id;
        if (string.IsNullOrWhiteSpace(key))
            return TypedResults.BadRequest("Name or Id is required");
        await ha.CallServiceAsync("shopping_list", "complete_item", request: new ServiceCallRequest { { "name", key.Trim() } }, cancellationToken: ct).ConfigureAwait(false);
        return TypedResults.Ok();
    }

    private static async Task<Results<Ok, BadRequest<string>>> RemoveShoppingItemAsync(
        [FromBody] ItemRequest req,
        [FromServices] IHomeAssistantClient ha,
        CancellationToken ct)
    {
        var key = req.Name ?? req.Id;
        if (string.IsNullOrWhiteSpace(key))
            return TypedResults.BadRequest("Name or Id is required");
        await ha.CallServiceAsync("shopping_list", "remove_item", request: new ServiceCallRequest { { "name", key.Trim() } }, cancellationToken: ct).ConfigureAwait(false);
        return TypedResults.Ok();
    }

    private static async Task<Ok<TodoEntitySummary[]>> GetTodoEntitiesAsync(
        [FromServices] IHomeAssistantClient ha,
        CancellationToken ct)
    {
        var ids = await ha.GetTodoListEntityIdsAsync(ct).ConfigureAwait(false);
        var summary = ids.Select(id => new TodoEntitySummary(id, id.Replace("todo.", "", StringComparison.OrdinalIgnoreCase))).ToArray();
        return TypedResults.Ok(summary);
    }

    private static async Task<Results<Ok<TodoItem[]>, NotFound>> GetTodoItemsAsync(
        [FromRoute] string entityId,
        [FromServices] IHomeAssistantClient ha,
        CancellationToken ct)
    {
        var items = await ha.GetTodoItemsAsync(entityId, ct).ConfigureAwait(false);
        return TypedResults.Ok(items);
    }

    private static async Task<Results<Ok, BadRequest<string>>> AddTodoItemAsync(
        [FromRoute] string entityId,
        [FromBody] AddItemRequest req,
        [FromServices] IHomeAssistantClient ha,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return TypedResults.BadRequest("Name is required");
        await ha.CallServiceAsync("todo", "add_item", request: new ServiceCallRequest
        {
            { "entity_id", entityId },
            { "item", req.Name.Trim() }
        }, cancellationToken: ct).ConfigureAwait(false);
        return TypedResults.Ok();
    }

    private static async Task<Results<Ok, BadRequest<string>>> CompleteTodoItemAsync(
        [FromRoute] string entityId,
        [FromBody] TodoItemRequest req,
        [FromServices] IHomeAssistantClient ha,
        CancellationToken ct)
    {
        var item = req.Item ?? req.Uid;
        if (string.IsNullOrWhiteSpace(item))
            return TypedResults.BadRequest("Item or Uid is required");
        await ha.CallServiceAsync("todo", "update_item", request: new ServiceCallRequest
        {
            { "entity_id", entityId },
            { "item", item.Trim() },
            { "status", "completed" }
        }, cancellationToken: ct).ConfigureAwait(false);
        return TypedResults.Ok();
    }

    private static async Task<Results<Ok, BadRequest<string>>> RemoveTodoItemAsync(
        [FromRoute] string entityId,
        [FromBody] TodoItemRequest req,
        [FromServices] IHomeAssistantClient ha,
        CancellationToken ct)
    {
        var item = req.Item ?? req.Uid;
        if (string.IsNullOrWhiteSpace(item))
            return TypedResults.BadRequest("Item or Uid is required");
        await ha.CallServiceAsync("todo", "remove_item", request: new ServiceCallRequest
        {
            { "entity_id", entityId },
            { "item", item.Trim() }
        }, cancellationToken: ct).ConfigureAwait(false);
        return TypedResults.Ok();
    }

    private sealed record AddItemRequest([property: JsonPropertyName("name")] string Name);
    private sealed record ItemRequest([property: JsonPropertyName("name")] string? Name, [property: JsonPropertyName("id")] string? Id);
    private sealed record TodoItemRequest([property: JsonPropertyName("item")] string? Item, [property: JsonPropertyName("uid")] string? Uid);
    private sealed record TodoEntitySummary([property: JsonPropertyName("entityId")] string EntityId, [property: JsonPropertyName("name")] string Name);
}
