using FakeItEasy;
using lucia.Agents.Models;
using lucia.Tests.TestDoubles;
using StackExchange.Redis;

namespace lucia.Tests.Services.EntityLocation;

public sealed class RedisLocationSnapshotStoreTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task SaveAsync_WaitsForEveryWriteAcknowledgment(int pendingWrite)
    {
        var context = new RedisSnapshotTestContext();
        var save = context.Store.SaveAsync(LocationSnapshot.Empty, DateTimeOffset.UtcNow);
        await context.BatchExecuted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        try
        {
            Assert.Equal(4, context.Writes.Count);
            for (var i = 0; i < context.Writes.Count; i++)
                if (i != pendingWrite)
                    context.Writes[i].SetResult(true);

            Assert.False(save.IsCompleted, "Saving must wait for the last Redis acknowledgment.");
            Assert.All(context.WriteFlags, flags => Assert.Equal(CommandFlags.None, flags));
            A.CallTo(() => context.Batch.Execute()).MustHaveHappenedOnceExactly();
        }
        finally
        {
            foreach (var write in context.Writes)
                write.TrySetResult(true);
            await save.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Theory]
    [InlineData("enqueue")]
    [InlineData("execute")]
    [InlineData("acknowledge")]
    public async Task SaveAsync_RedisFailure_PropagatesToCaller(string stage)
    {
        var context = new RedisSnapshotTestContext { AutoAcknowledge = true };
        var failure = new RedisConnectionException(ConnectionFailureType.UnableToConnect, "test write failure");
        switch (stage)
        {
            case "enqueue":
                A.CallTo(() => context.Database.CreateBatch(A<object>._)).Throws(failure);
                break;
            case "execute":
                A.CallTo(() => context.Batch.Execute()).Throws(failure);
                break;
            default:
                context.WriteFailure = failure;
                break;
        }

        var actual = await Assert.ThrowsAsync<RedisConnectionException>(
            () => context.Store.SaveAsync(LocationSnapshot.Empty, null));

        Assert.Same(failure, actual);
    }
}
