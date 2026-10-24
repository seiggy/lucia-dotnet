using FakeItEasy;
using lucia.Agents.Services;
using lucia.Tests.TestDoubles;
using StackExchange.Redis;

namespace lucia.Tests.Services.EntityLocation;

public sealed class RedisSnapshotPublicationTests
{
    [Theory]
    [InlineData("reload")]
    [InlineData("remove")]
    [InlineData("evict")]
    public async Task Mutation_PublishesVersionOnlyAfterSavingIsAcknowledged(string mutation)
    {
        var context = new RedisSnapshotTestContext { AutoAcknowledge = true };
        var service = context.CreateService(new SampleHomeAssistant());
        await service.InitializeAsync();
        context.ResetWrites();
        context.AutoAcknowledge = false;

        var operation = MutateAsync(service, mutation);
        await context.BatchExecuted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            Assert.False(operation.IsCompleted, "The mutation must wait for the snapshot save.");
            A.CallTo(() => context.Database.StringIncrementAsync(
                "lucia:location:version", 1L, CommandFlags.None)).MustNotHaveHappened();
        }
        finally
        {
            foreach (var write in context.Writes)
                write.TrySetResult(true);
            await operation.WaitAsync(TimeSpan.FromSeconds(5));
        }

        A.CallTo(() => context.Database.StringIncrementAsync(
            "lucia:location:version", 1L, CommandFlags.None)).MustHaveHappenedOnceExactly();
    }

    [Theory]
    [InlineData("reload")]
    [InlineData("remove")]
    [InlineData("evict")]
    public async Task Mutation_FailedSaveDoesNotPublishVersion(string mutation)
    {
        var context = new RedisSnapshotTestContext { AutoAcknowledge = true };
        var service = context.CreateService(new SampleHomeAssistant());
        await service.InitializeAsync();
        context.ResetWrites();
        var failure = new RedisConnectionException(ConnectionFailureType.UnableToConnect, "test write failure");
        context.WriteFailure = failure;

        var actual = await Assert.ThrowsAsync<RedisConnectionException>(() => MutateAsync(service, mutation));

        Assert.Same(failure, actual);
        A.CallTo(() => context.Database.StringIncrementAsync(
            "lucia:location:version", 1L, CommandFlags.None)).MustNotHaveHappened();
    }

    private static Task MutateAsync(EntityLocationService service, string mutation) => mutation switch
    {
        "reload" => service.InvalidateAndReloadAsync(),
        "remove" => service.RemoveEntityAsync("light.porch"),
        "evict" => service.EvictEmbeddingAsync("entity", "light.porch"),
        _ => throw new ArgumentOutOfRangeException(nameof(mutation))
    };
}
