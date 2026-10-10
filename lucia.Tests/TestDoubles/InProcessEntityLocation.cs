using FakeItEasy;
using lucia.Agents.Abstractions;
using lucia.Agents.Services;
using lucia.HomeAssistant.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests.TestDoubles;

/// <summary>Builds the production <see cref="EntityLocationService"/> in its in-process (no Redis) configuration.</summary>
public static class InProcessEntityLocation
{
    public static EntityLocationService Create(
        IHomeAssistantClient haClient,
        IEmbeddingProviderResolver embeddingResolver,
        IHybridEntityMatcher entityMatcher,
        ILoggerFactory? loggerFactory = null)
    {
        loggerFactory ??= NullLoggerFactory.Instance;
        var visibilityStore = A.Fake<IVisibilityConfigStore>();

        return new EntityLocationService(
            new InProcessLocationSnapshotStore(),
            visibilityStore,
            embeddingResolver,
            new LocationSearchService(embeddingResolver, entityMatcher, loggerFactory.CreateLogger<LocationSearchService>()),
            new LocationSnapshotLoader(haClient, loggerFactory.CreateLogger<LocationSnapshotLoader>()),
            new EntityEmbeddingJob(embeddingResolver, loggerFactory.CreateLogger<EntityEmbeddingJob>()),
            loggerFactory.CreateLogger<EntityLocationService>());
    }
}