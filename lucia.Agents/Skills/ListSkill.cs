using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using lucia.HomeAssistant.Models;
using lucia.HomeAssistant.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Skills;

/// <summary>
/// Skill for adding items to Home Assistant shopping list and todo lists.
/// </summary>
public sealed class ListSkill : ILuciaSkill
{
    public IReadOnlyList<string> EntityDomains { get; } = ["todo"];

    private readonly IHomeAssistantClient _ha;
    private readonly ILogger<ListSkill> _logger;

    private static readonly ActivitySource ActivitySource = new("Lucia.Skills.List", "1.0.0");
    private static readonly Meter Meter = new("Lucia.Skills.List", "1.0.0");
    private static readonly ToolMetrics Metrics = ToolMetrics.Create(Meter, "list", "list tool");

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
        using var activity = ActivitySource.StartActivity();
        return await ToolRunner.RunAsync(_logger, activity, Metrics.ForTool(nameof(AddToShoppingListAsync)), "Unable to add to the shopping list. Please try again.", async () =>
        {
            if (string.IsNullOrWhiteSpace(itemName))
                return "Item name cannot be empty.";
            await _ha.CallServiceAsync("shopping_list", "add_item", request: new ServiceCallRequest { { "name", itemName.Trim() } }, cancellationToken: cancellationToken).ConfigureAwait(false);
            return $"Added '{itemName.Trim()}' to the shopping list.";
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    [Description("Add an item to a Home Assistant todo list. entityId is the todo entity (e.g. todo.grocery, todo.personal_tasks).")]
    public async Task<string> AddToTodoListAsync(
        [Description("The todo list entity ID (e.g. todo.grocery, todo.personal_tasks)")] string entityId,
        [Description("The task or item to add")] string itemName,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity();
        return await ToolRunner.RunAsync(_logger, activity, Metrics.ForTool(nameof(AddToTodoListAsync)), "Unable to add to the todo list. Please try again.", async () =>
        {
            if (string.IsNullOrWhiteSpace(entityId) || string.IsNullOrWhiteSpace(itemName))
                return "Entity ID and item name are required.";
            var eid = entityId.Trim();
            if (!eid.StartsWith("todo.", StringComparison.OrdinalIgnoreCase))
                eid = "todo." + eid;
            await _ha.CallServiceAsync("todo", "add_item", request: new ServiceCallRequest { { "entity_id", eid }, { "item", itemName.Trim() } }, cancellationToken: cancellationToken).ConfigureAwait(false);
            return $"Added '{itemName.Trim()}' to todo list {eid}.";
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    [Description("List all items on the Home Assistant shopping list.")]
    public async Task<string> ListShoppingItemsAsync(CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity();
        return await ToolRunner.RunAsync(_logger, activity, Metrics.ForTool(nameof(ListShoppingItemsAsync)), "Unable to list the shopping items. Please try again.", async () =>
        {
            var items = await _ha.GetShoppingListItemsAsync(cancellationToken).ConfigureAwait(false);
            if (items.Length == 0)
                return "Shopping list is empty.";
            var sb = new StringBuilder();
            foreach (var i in items)
                sb.AppendLine($"- {(i.Complete ? "[done] " : "")}{i.Name}");
            return sb.ToString().TrimEnd();
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    [Description("List todo list entities available in Home Assistant (e.g. todo.grocery).")]
    public async Task<string> ListTodoEntitiesAsync(CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity();
        return await ToolRunner.RunAsync(_logger, activity, Metrics.ForTool(nameof(ListTodoEntitiesAsync)), "Unable to list the todo lists. Please try again.", async () =>
        {
            var ids = await _ha.GetTodoListEntityIdsAsync(cancellationToken).ConfigureAwait(false);
            if (ids.Length == 0)
                return "No todo lists found in Home Assistant. Add the Local todo integration to create lists.";
            return "Available todo lists: " + string.Join(", ", ids);
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    [Description("List items on a specific Home Assistant todo list. Use entityId from ListTodoEntitiesAsync.")]
    public async Task<string> ListTodoItemsAsync(
        [Description("The todo list entity ID (e.g. todo.grocery)")] string entityId,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity();
        return await ToolRunner.RunAsync(_logger, activity, Metrics.ForTool(nameof(ListTodoItemsAsync)), "Unable to list the todo items. Please try again.", async () =>
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
        }, detail: entityId, cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
