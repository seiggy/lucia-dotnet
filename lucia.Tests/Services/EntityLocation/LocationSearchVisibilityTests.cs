using FakeItEasy;
using lucia.Agents.Abstractions;
using lucia.Agents.Models;
using lucia.Agents.Models.HomeAssistant;
using lucia.Agents.Services;
using lucia.Tests.TestDoubles;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests.Services.EntityLocation;

public sealed class LocationSearchVisibilityTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task EntityCandidates_AreFilteredBeforeScoringOrSubstringFallback(bool embeddings, bool domainFilter)
    {
        var resolver = A.Fake<IEmbeddingProviderResolver>();
        using var generator = new DeterministicEmbeddingGenerator();
        A.CallTo(() => resolver.ResolveAsync(A<string?>._, A<CancellationToken>._))
            .Returns(embeddings ? generator : null);
        var matcher = A.Fake<IHybridEntityMatcher>();
        IReadOnlyList<HomeAssistantEntity>? scoredCandidates = null;
        A.CallTo(() => matcher.FindMatchesAsync(A<string>._!, A<IReadOnlyList<HomeAssistantEntity>>._,
            A<IEmbeddingGenerator<string, Embedding<float>>>._, A<HybridMatchOptions>._, A<CancellationToken>._))
            .ReturnsLazily((string _, IReadOnlyList<HomeAssistantEntity> candidates,
                IEmbeddingGenerator<string, Embedding<float>> _, HybridMatchOptions _, CancellationToken _) =>
            {
                scoredCandidates = candidates;
                return Task.FromResult<IReadOnlyList<EntityMatchResult<HomeAssistantEntity>>>(candidates
                    .Select(entity => new EntityMatchResult<HomeAssistantEntity> { Entity = entity, HybridScore = 1, EmbeddingSimilarity = 0 }).ToList());
            });
        var snapshot = LocationSnapshot.Create([], [], [
            Entity("light.unrestricted", null), Entity("light.hidden", []),
            Entity("light.allowed", ["TIMER-AGENT"]), Entity("light.other_agent", ["light-agent"]),
            Entity("climate.unrestricted", null)
        ]);
        var search = new LocationSearchService(resolver, matcher, NullLogger<LocationSearchService>.Instance);
        var domains = domainFilter ? new[] { "light" } : null;

        var matches = await search.SearchEntitiesAsync(snapshot, "", domains, null, "timer-agent", CancellationToken.None);
        var expected = domainFilter ? new[] { "light.allowed", "light.unrestricted" }
            : ["climate.unrestricted", "light.allowed", "light.unrestricted"];
        Assert.Equal(expected, matches.Select(match => match.Entity.EntityId).Order());
        if (embeddings)
            Assert.Equal(expected, scoredCandidates!.Select(entity => entity.EntityId).Order());
    }

    private static HomeAssistantEntity Entity(string id, string[]? agents) => new()
    {
        EntityId = id, FriendlyName = id,
        IncludeForAgent = agents is null ? null : new HashSet<string>(agents)
    };
}
