using FakeItEasy;
using lucia.Agents.Services;
using lucia.HomeAssistant.Services;
using lucia.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests.Services.EntityLocation;

public sealed class LocationSnapshotLoaderTests
{
    private static LocationSnapshotLoader CreateLoader(SampleHomeAssistant home)
        => new(home.Client, NullLogger<LocationSnapshotLoader>.Instance);

    private static void RunTemplateThrows(SampleHomeAssistant home, Exception exception)
        => A.CallTo(home.Client)
            .Where(call => call.Method.Name == nameof(IHomeAssistantClient.RunTemplateAsync))
            .WithNonVoidReturnType()
            .Throws(exception);

    [Fact]
    public async Task LoadAsync_AreaMapTimesOut_FallsBackToRegistryAreaIds()
    {
        var home = new SampleHomeAssistant();
        home.Entities[0] = SampleHomeAssistant.Entity("light.kitchen_ceiling", "Kitchen Ceiling", areaId: "kitchen");
        RunTemplateThrows(home, new TaskCanceledException("timed out", new TimeoutException()));

        var snapshot = await CreateLoader(home).LoadAsync(exposedEntitiesOnly: false);

        Assert.Equal(4, snapshot.Areas.Length);
        Assert.Equal("kitchen", snapshot.EntityById["light.kitchen_ceiling"].AreaId);
        Assert.Null(snapshot.EntityById["light.kitchen_island"].AreaId);
    }

    [Fact]
    public async Task LoadAsync_ExposedListFails_PropagatesInsteadOfLoadingEverything()
    {
        var home = new SampleHomeAssistant();
        A.CallTo(() => home.Client.GetExposedEntitiesAsync(A<CancellationToken>._))
            .Throws(new HttpRequestException("Home Assistant is unreachable"));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => CreateLoader(home).LoadAsync(exposedEntitiesOnly: true));
    }

    [Fact]
    public async Task LoadAsync_CallerCancellation_Propagates()
    {
        var home = new SampleHomeAssistant();
        RunTemplateThrows(home, new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => CreateLoader(home).LoadAsync(exposedEntitiesOnly: false));
    }
}