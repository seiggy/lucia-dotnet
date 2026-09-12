using FakeItEasy;
using lucia.Agents.Abstractions;
using lucia.Agents.Models.HomeAssistant;
using Microsoft.Extensions.AI;

namespace lucia.Tests.TestDoubles;

internal static class NullDeviceCache
{
    public static IDeviceCacheService Create()
    {
        // FakeItEasy's default empty collections look like cache hits and hide scenario entities.
        var fake = A.Fake<IDeviceCacheService>();
        A.CallTo(() => fake.GetCachedLightsAsync(A<CancellationToken>._))
            .Returns(Task.FromResult<List<LightEntity>?>(null));
        A.CallTo(() => fake.GetCachedPlayersAsync(A<CancellationToken>._))
            .Returns(Task.FromResult<List<MusicPlayerEntity>?>(null));
        A.CallTo(() => fake.GetCachedClimateDevicesAsync(A<CancellationToken>._))
            .Returns(Task.FromResult<List<ClimateEntity>?>(null));
        A.CallTo(() => fake.GetCachedFansAsync(A<CancellationToken>._))
            .Returns(Task.FromResult<List<FanEntity>?>(null));
        A.CallTo(() => fake.GetCachedSensorsAsync(A<CancellationToken>._))
            .Returns(Task.FromResult<List<SensorEntity>?>(null));
        A.CallTo(() => fake.GetAreaEmbeddingsAsync(A<CancellationToken>._))
            .Returns(Task.FromResult<Dictionary<string, Embedding<float>>?>(null));
        A.CallTo(() => fake.GetEmbeddingAsync(A<string>._, A<CancellationToken>._))
            .Returns(Task.FromResult<Embedding<float>?>(null));
        return fake;
    }
}
