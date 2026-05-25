using System.Reflection;
using FakeItEasy;
using lucia.AgentHost.Apis;
using lucia.Agents.Abstractions;
using lucia.Agents.Models;
using lucia.Agents.Models.HomeAssistant;
using Microsoft.AspNetCore.Http.HttpResults;

namespace lucia.Tests;

public sealed class EntitiesApiTests
{
    [Fact]
    public async Task GetEntitiesAsync_FiltersByLocationAndReturnsRequestedPage()
    {
        var locationService = A.Fake<IEntityLocationService>();
        var kitchenArea = new AreaInfo
        {
            AreaId = "kitchen",
            Name = "Kitchen",
            FloorId = "main-floor",
            Aliases = ["Cookspace"],
        };
        var officeArea = new AreaInfo
        {
            AreaId = "office",
            Name = "Office",
            FloorId = "main-floor",
        };
        var mainFloor = new FloorInfo
        {
            FloorId = "main-floor",
            Name = "Main Floor",
            Aliases = ["Downstairs"],
        };
        var entities = new[]
        {
            CreateEntity("light.kitchen_ceiling", "Kitchen Ceiling", "kitchen"),
            CreateEntity("sensor.kitchen_temperature", "Kitchen Temperature", "kitchen"),
            CreateEntity("light.office_lamp", "Office Lamp", "office"),
        };

        A.CallTo(() => locationService.GetEntitiesAsync(A<CancellationToken>._))
            .Returns(entities);
        A.CallTo(() => locationService.GetAreaForEntity("light.kitchen_ceiling"))
            .Returns(kitchenArea);
        A.CallTo(() => locationService.GetAreaForEntity("sensor.kitchen_temperature"))
            .Returns(kitchenArea);
        A.CallTo(() => locationService.GetAreaForEntity("light.office_lamp"))
            .Returns(officeArea);
        A.CallTo(() => locationService.GetFloorForArea("kitchen"))
            .Returns(mainFloor);

        var response = await InvokeGetEntitiesAsync(
            locationService,
            locationFilter: "kitchen",
            page: 2,
            pageSize: 1);

        Assert.Equal(2, response.TotalCount);
        Assert.Equal(2, response.Page);
        Assert.Equal(1, response.PageSize);
        Assert.Single(response.Items);
        Assert.Equal("sensor.kitchen_temperature", response.Items[0].EntityId);
        Assert.Equal("Kitchen", response.Items[0].AreaName);
        Assert.Equal("Main Floor", response.Items[0].FloorName);
    }

    [Fact]
    public async Task GetEntitiesAsync_UsesSearchServiceForNameFilters()
    {
        var locationService = A.Fake<IEntityLocationService>();
        var entity = CreateEntity("light.office_lamp", "Office Lamp", "office");

        A.CallTo(() => locationService.SearchEntitiesAsync(
                "lamp",
                A<IReadOnlyList<string>?>.That.Matches(filter => HasSingleDomainFilter(filter, "light")),
                null,
                A<CancellationToken>._))
            .Returns(
            [
                new EntityMatchResult<HomeAssistantEntity>
                {
                    Entity = entity,
                    HybridScore = 0.95,
                    EmbeddingSimilarity = 0.91,
                },
            ]);

        var response = await InvokeGetEntitiesAsync(
            locationService,
            nameFilter: "lamp",
            domain: "light");

        Assert.Single(response.Items);
        Assert.Equal("light.office_lamp", response.Items[0].EntityId);
        A.CallTo(() => locationService.GetEntitiesAsync(A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task GetEntitiesAsync_FallsBackToSubstringMatchingWhenSearchReturnsNoResults()
    {
        var locationService = A.Fake<IEntityLocationService>();
        var matchingEntity = CreateEntity("light.office_lamp", "Desk Lamp", "office", ["Reading Light"]);
        var otherEntity = CreateEntity("switch.office_fan", "Office Fan", "office");

        A.CallTo(() => locationService.SearchEntitiesAsync(
                "reading",
                A<IReadOnlyList<string>?>.That.Matches(filter => HasSingleDomainFilter(filter, "light")),
                null,
                A<CancellationToken>._))
            .Returns([]);
        A.CallTo(() => locationService.GetEntitiesAsync(A<CancellationToken>._))
            .Returns([matchingEntity, otherEntity]);

        var response = await InvokeGetEntitiesAsync(
            locationService,
            nameFilter: "reading",
            domain: "light");

        Assert.Single(response.Items);
        Assert.Equal("light.office_lamp", response.Items[0].EntityId);
        A.CallTo(() => locationService.GetEntitiesAsync(A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    private static HomeAssistantEntity CreateEntity(
        string entityId,
        string friendlyName,
        string? areaId,
        IReadOnlyList<string>? aliases = null)
    {
        return new HomeAssistantEntity
        {
            EntityId = entityId,
            FriendlyName = friendlyName,
            AreaId = areaId,
            Aliases = aliases ?? [],
            Platform = "test",
        };
    }

    private static bool HasSingleDomainFilter(IReadOnlyList<string>? filter, string expectedDomain)
    {
        return filter is not null
            && filter.Count == 1
            && string.Equals(filter[0], expectedDomain, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<EntityQueryResponse> InvokeGetEntitiesAsync(
        IEntityLocationService locationService,
        string? nameFilter = null,
        string? locationFilter = null,
        string? domain = null,
        string? agent = null,
        int page = 1,
        int pageSize = 100)
    {
        var method = typeof(EntitiesApi).GetMethod("GetEntitiesAsync", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var invocationResult = method.Invoke(null, [locationService, nameFilter, locationFilter, domain, agent, page, pageSize, CancellationToken.None]);

        Assert.NotNull(invocationResult);

        var task = (Task)invocationResult;
        await task.ConfigureAwait(false);

        var resultProperty = invocationResult.GetType().GetProperty("Result");
        var okResult = resultProperty?.GetValue(invocationResult) as Ok<EntityQueryResponse>;

        Assert.NotNull(okResult);
        Assert.NotNull(okResult.Value);

        return okResult.Value;
    }
}
