using FakeItEasy;
using lucia.Agents.Skills;
using lucia.HomeAssistant.Models;
using lucia.HomeAssistant.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests;

public sealed class ListSkillTests
{
    private static readonly string NewLine = Environment.NewLine;

    private static ListSkill CreateSkill(IHomeAssistantClient ha) => new(ha, NullLogger<ListSkill>.Instance);

    private static IHomeAssistantClient FailingClient(Exception exception)
    {
        var ha = A.Fake<IHomeAssistantClient>();
        A.CallTo(() => ha.CallServiceAsync(
                A<string>._, A<string>._, A<string?>._, A<ServiceCallRequest?>._, A<CancellationToken>._))
            .Throws(exception);
        A.CallTo(() => ha.GetShoppingListItemsAsync(A<CancellationToken>._)).Throws(exception);
        A.CallTo(() => ha.GetTodoListEntityIdsAsync(A<CancellationToken>._)).Throws(exception);
        A.CallTo(() => ha.GetTodoItemsAsync(A<string>._, A<CancellationToken>._)).Throws(exception);
        return ha;
    }

    [Fact]
    public void GetTools_ExposesTheFiveListTools_WithoutAnAsyncSuffix()
    {
        var skill = CreateSkill(A.Fake<IHomeAssistantClient>());

        Assert.Equal(
            ["AddToShoppingList", "AddToTodoList", "ListShoppingItems", "ListTodoEntities", "ListTodoItems"],
            skill.GetTools().Select(tool => tool.Name).Order().ToArray());
    }

    // ── Adding ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AddToShoppingListAsync_TrimsTheNameAndCallsShoppingListAddItem()
    {
        var ha = A.Fake<IHomeAssistantClient>();

        var result = await CreateSkill(ha).AddToShoppingListAsync("  milk ");

        Assert.Equal("Added 'milk' to the shopping list.", result);
        A.CallTo(() => ha.CallServiceAsync(
                "shopping_list",
                "add_item",
                A<string?>._,
                A<ServiceCallRequest?>.That.Matches(request => request!.Count == 1 && Equals(request["name"], "milk")),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AddToShoppingListAsync_BlankName_IsRejectedWithoutCallingHomeAssistant(string name)
    {
        var ha = A.Fake<IHomeAssistantClient>();

        Assert.Equal("Item name cannot be empty.", await CreateSkill(ha).AddToShoppingListAsync(name));
        A.CallTo(ha).MustNotHaveHappened();
    }

    [Theory]
    [InlineData("todo.grocery", "todo.grocery")]
    [InlineData("grocery", "todo.grocery")]
    [InlineData(" Todo.Tasks ", "Todo.Tasks")]
    public async Task AddToTodoListAsync_NormalizesTheEntityIdAndCallsTodoAddItem(string input, string expectedEntityId)
    {
        var ha = A.Fake<IHomeAssistantClient>();

        var result = await CreateSkill(ha).AddToTodoListAsync(input, " eggs ");

        Assert.Equal($"Added 'eggs' to todo list {expectedEntityId}.", result);
        A.CallTo(() => ha.CallServiceAsync(
                "todo",
                "add_item",
                A<string?>._,
                A<ServiceCallRequest?>.That.Matches(request =>
                    request!.Count == 2 && Equals(request["entity_id"], expectedEntityId) && Equals(request["item"], "eggs")),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Theory]
    [InlineData("", "eggs")]
    [InlineData("todo.grocery", " ")]
    public async Task AddToTodoListAsync_MissingValues_AreRejected(string entityId, string itemName)
    {
        var ha = A.Fake<IHomeAssistantClient>();

        Assert.Equal("Entity ID and item name are required.", await CreateSkill(ha).AddToTodoListAsync(entityId, itemName));
        A.CallTo(ha).MustNotHaveHappened();
    }

    // ── Listing ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ListShoppingItemsAsync_MarksCompletedItems()
    {
        var ha = A.Fake<IHomeAssistantClient>();
        A.CallTo(() => ha.GetShoppingListItemsAsync(A<CancellationToken>._)).Returns(
        [
            new ShoppingListItem { Name = "milk" },
            new ShoppingListItem { Name = "eggs", Complete = true },
        ]);

        Assert.Equal($"- milk{NewLine}- [done] eggs", await CreateSkill(ha).ListShoppingItemsAsync());
    }

    [Fact]
    public async Task ListShoppingItemsAsync_Empty_SaysSo()
    {
        var ha = A.Fake<IHomeAssistantClient>();
        A.CallTo(() => ha.GetShoppingListItemsAsync(A<CancellationToken>._)).Returns(Array.Empty<ShoppingListItem>());

        Assert.Equal("Shopping list is empty.", await CreateSkill(ha).ListShoppingItemsAsync());
    }

    [Fact]
    public async Task ListTodoEntitiesAsync_ListsTheIds()
    {
        var ha = A.Fake<IHomeAssistantClient>();
        A.CallTo(() => ha.GetTodoListEntityIdsAsync(A<CancellationToken>._)).Returns(["todo.grocery", "todo.tasks"]);

        Assert.Equal("Available todo lists: todo.grocery, todo.tasks", await CreateSkill(ha).ListTodoEntitiesAsync());
    }

    [Fact]
    public async Task ListTodoEntitiesAsync_None_ExplainsHowToAddOne()
    {
        var ha = A.Fake<IHomeAssistantClient>();
        A.CallTo(() => ha.GetTodoListEntityIdsAsync(A<CancellationToken>._)).Returns(Array.Empty<string>());

        Assert.Equal(
            "No todo lists found in Home Assistant. Add the Local todo integration to create lists.",
            await CreateSkill(ha).ListTodoEntitiesAsync());
    }

    [Fact]
    public async Task ListTodoItemsAsync_NormalizesTheIdAndMarksCompletedItems()
    {
        var ha = A.Fake<IHomeAssistantClient>();
        A.CallTo(() => ha.GetTodoItemsAsync("todo.grocery", A<CancellationToken>._)).Returns(
        [
            new TodoItem { Summary = "bread" },
            new TodoItem { Summary = "butter", Status = "completed" },
        ]);

        Assert.Equal($"- bread{NewLine}- [done] butter", await CreateSkill(ha).ListTodoItemsAsync(" grocery "));
    }

    [Fact]
    public async Task ListTodoItemsAsync_Empty_NamesTheList()
    {
        var ha = A.Fake<IHomeAssistantClient>();
        A.CallTo(() => ha.GetTodoItemsAsync("todo.grocery", A<CancellationToken>._)).Returns(Array.Empty<TodoItem>());

        Assert.Equal("Todo list todo.grocery is empty.", await CreateSkill(ha).ListTodoItemsAsync("grocery"));
    }

    // ── Failures ────────────────────────────────────────────────────────

    [Fact]
    public async Task EveryTool_WhenHomeAssistantFails_ReturnsAGenericMessage()
    {
        var skill = CreateSkill(FailingClient(new InvalidOperationException("secret connection string")));

        var results = new[]
        {
            await skill.AddToShoppingListAsync("milk"),
            await skill.AddToTodoListAsync("grocery", "eggs"),
            await skill.ListShoppingItemsAsync(),
            await skill.ListTodoEntitiesAsync(),
            await skill.ListTodoItemsAsync("grocery"),
        };

        Assert.Equal(
            [
                "Unable to add to the shopping list. Please try again.",
                "Unable to add to the todo list. Please try again.",
                "Unable to list the shopping items. Please try again.",
                "Unable to list the todo lists. Please try again.",
                "Unable to list the todo items. Please try again.",
            ],
            results);
    }

    [Fact]
    public async Task EveryTool_WhenCancelled_Propagates()
    {
        var skill = CreateSkill(FailingClient(new OperationCanceledException()));

        await Assert.ThrowsAsync<OperationCanceledException>(() => skill.AddToShoppingListAsync("milk"));
        await Assert.ThrowsAsync<OperationCanceledException>(() => skill.AddToTodoListAsync("grocery", "eggs"));
        await Assert.ThrowsAsync<OperationCanceledException>(() => skill.ListShoppingItemsAsync());
        await Assert.ThrowsAsync<OperationCanceledException>(() => skill.ListTodoEntitiesAsync());
        await Assert.ThrowsAsync<OperationCanceledException>(() => skill.ListTodoItemsAsync("grocery"));
    }
}
