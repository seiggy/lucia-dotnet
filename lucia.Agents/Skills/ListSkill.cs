using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using lucia.HomeAssistant.Models;
using lucia.HomeAssistant.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Skills;

/// <summary>
/// Skill for adding items to Home Assistant shopping list and todo lists.
/// </summary>
public sealed class ListSkill : IAgentSkill
{
    private readonly IHomeAssistantClient _ha;
    private readonly ILogger<ListSkill> _logger;

    private static readonly ActivitySource ActivitySource = new("Lucia.Skills.List", "1.0.0");

    public ListSkill(IHomeAssistantClient ha, ILogger<ListSkill> logger)
    {
        _ha = ha;
        _logger = logger;
    }

    public IList<AITool> GetTools()
    {
        return
        [
            AIFunctionFactory.Create(AddToShoppingListAsync),
            AIFunctionFactory.Create(AddToTodoListAsync),
            AIFunctionFactory.Create(ListShoppingItemsAsync),
            AIFunctionFactory.Create(ListTodoItemsAsync),
            AIFunctionFactory.Create(ListTodoEntitiesAsync)
        ];
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("ListSkill initialized.");
        return Task.CompletedTask;
    }

    [Description("Add an item to the Home Assistant shopping list. Use for groceries and items to buy.")]
    public async Task<string> AddToShoppingListAsync(
        [Description("The item name to add (e.g. 'milk', 'eggs')")] string itemName,
        CancellationToken cancellationToken = default)
    {
        using var _ = ActivitySource.StartActivity();
        try
        {
            if (string.IsNullOrWhiteSpace(itemName))
                return "Item name cannot be empty.";
            await _ha.CallServiceAsync("shopping_list", "add_item", request: new ServiceCallRequest { { "name", itemName.Trim() } }, cancellationToken: cancellationToken).ConfigureAwait(false);
            return $"Added '{itemName.Trim()}' to the shopping list.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add item to shopping list");
            return $"Failed to add to shopping list: {ex.Message}";
        }
    }

    [Description("Add an item to a Home Assistant todo list. entityId is the todo entity (e.g. todo.grocery, todo.personal_tasks).")]
    public async Task<string> AddToTodoListAsync(
        [Description("The todo list entity ID (e.g. todo.grocery, todo.personal_tasks)")] string entityId,
        [Description("The task or item to add")] string itemName,
        CancellationToken cancellationToken = default)
    {
        using var _ = ActivitySource.StartActivity();
        try
        {
            if (string.IsNullOrWhiteSpace(entityId) || string.IsNullOrWhiteSpace(itemName))
                return "Entity ID and item name are required.";
            var eid = entityId.Trim();
            if (!eid.StartsWith("todo.", StringComparison.OrdinalIgnoreCase))
                eid = "todo." + eid;
            await _ha.CallServiceAsync("todo", "add_item", request: new ServiceCallRequest { { "entity_id", eid }, { "item", itemName.Trim() } }, cancellationToken: cancellationToken).ConfigureAwait(false);
            return $"Added '{itemName.Trim()}' to todo list {eid}.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add item to todo list");
            return $"Failed to add to todo list: {ex.Message}";
        }
    }

    [Description("List all items on the Home Assistant shopping list.")]
    public async Task<string> ListShoppingItemsAsync(CancellationToken cancellationToken = default)
    {
        using var _ = ActivitySource.StartActivity();
        try
        {
            var items = await _ha.GetShoppingListItemsAsync(cancellationToken).ConfigureAwait(false);
            if (items.Length == 0)
                return "Shopping list is empty.";
            var sb = new StringBuilder();
            foreach (var i in items)
                sb.AppendLine($"- {(i.Complete ? "[done] " : "")}{i.Name}");
            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list shopping items");
            return $"Failed to list shopping items: {ex.Message}";
        }
    }

    [Description("List todo list entities available in Home Assistant (e.g. todo.grocery).")]
    public async Task<string> ListTodoEntitiesAsync(CancellationToken cancellationToken = default)
    {
        using var _ = ActivitySource.StartActivity();
        try
        {
            var ids = await _ha.GetTodoListEntityIdsAsync(cancellationToken).ConfigureAwait(false);
            if (ids.Length == 0)
                return "No todo lists found in Home Assistant. Add the Local todo integration to create lists.";
            return "Available todo lists: " + string.Join(", ", ids);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list todo entities");
            return $"Failed to list todo lists: {ex.Message}";
        }
    }

    [Description("List items on a specific Home Assistant todo list. Use entityId from ListTodoEntitiesAsync.")]
    public async Task<string> ListTodoItemsAsync(
        [Description("The todo list entity ID (e.g. todo.grocery)")] string entityId,
        CancellationToken cancellationToken = default)
    {
        using var _ = ActivitySource.StartActivity();
        try
        {
            var eid = entityId.Trim();
            if (!eid.StartsWith("todo.", StringComparison.OrdinalIgnoreCase))
                eid = "todo." + eid;
            var items = await _ha.GetTodoItemsAsync(eid, cancellationToken).ConfigureAwait(false);
            if (items.Length == 0)
                return $"Todo list {eid} is empty.";
            var sb = new StringBuilder();
            foreach (var i in items)
                sb.AppendLine($"- {(i.Status == "completed" ? "[done] " : "")}{i.Summary}");
            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list todo items");
            return $"Failed to list todo items: {ex.Message}";
        }
    }
}
