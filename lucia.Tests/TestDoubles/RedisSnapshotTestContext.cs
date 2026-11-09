using FakeItEasy;
using lucia.Agents.Abstractions;
using lucia.Agents.Models;
using lucia.Agents.Services;
using lucia.Data.Redis;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;

namespace lucia.Tests.TestDoubles;

/// <summary>Models queued writes separately from Redis acknowledgments, without timing sleeps.</summary>
public sealed class RedisSnapshotTestContext
{
    public IConnectionMultiplexer Redis { get; } = A.Fake<IConnectionMultiplexer>();
    public IDatabase Database { get; } = A.Fake<IDatabase>();
    public IBatch Batch { get; } = A.Fake<IBatch>();
    public List<TaskCompletionSource<bool>> Writes { get; } = [];
    public List<CommandFlags> WriteFlags { get; } = [];
    public TaskCompletionSource BatchExecuted { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool AutoAcknowledge { get; set; }
    public RedisException? WriteFailure { get; set; }

    public RedisLocationSnapshotStore Store { get; }

    public RedisSnapshotTestContext()
    {
        A.CallTo(() => Redis.GetDatabase(A<int>._, A<object>._)).Returns(Database);
        A.CallTo(() => Database.CreateBatch(A<object>._)).Returns(Batch);
        A.CallTo(() => Batch.Execute()).Invokes(() => BatchExecuted.TrySetResult());
        A.CallTo(() => Batch.StringSetAsync(
                A<RedisKey>._, A<RedisValue>._, A<Expiration>._, A<ValueCondition>._, A<CommandFlags>._))
            .ReturnsLazily((RedisKey key, RedisValue value, Expiration expiry, ValueCondition condition, CommandFlags flags) =>
            {
                var acknowledgment = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Writes.Add(acknowledgment);
                WriteFlags.Add(flags);
                if (WriteFailure is not null)
                    return Task.FromException<bool>(WriteFailure);
                if (AutoAcknowledge)
                    acknowledgment.SetResult(true);

                // FireAndForget completes at queueing time, not when the server replies.
                return flags.HasFlag(CommandFlags.FireAndForget) ? Task.FromResult(false) : acknowledgment.Task;
            });
        Store = new RedisLocationSnapshotStore(Redis, NullLogger<RedisLocationSnapshotStore>.Instance);
    }

    public void ResetWrites()
    {
        Writes.Clear();
        WriteFlags.Clear();
        BatchExecuted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Fake.ClearRecordedCalls(Database);
    }

    public EntityLocationService CreateService(SampleHomeAssistant home)
        => CreateService(Store, home);

    public static EntityLocationService CreateService(ILocationSnapshotStore store, SampleHomeAssistant home)
    {
        var resolver = A.Fake<IEmbeddingProviderResolver>();
        A.CallTo(() => resolver.ResolveAsync(A<string?>._, A<CancellationToken>._))
            .Returns(Task.FromResult<IEmbeddingGenerator<string, Embedding<float>>?>(null));
        var matcher = new HybridEntityMatcher(new EmbeddingSimilarityService(), NullLogger<HybridEntityMatcher>.Instance);
        return new EntityLocationService(
            store, A.Fake<IVisibilityConfigStore>(), resolver,
            new LocationSearchService(resolver, matcher, NullLogger<LocationSearchService>.Instance),
            new LocationSnapshotLoader(home.Client, NullLogger<LocationSnapshotLoader>.Instance),
            new EntityEmbeddingJob(resolver, NullLogger<EntityEmbeddingJob>.Instance),
            NullLogger<EntityLocationService>.Instance);
    }
}
