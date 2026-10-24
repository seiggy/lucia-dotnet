using lucia.Data.Redis;
using lucia.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace lucia.Tests.Services.EntityLocation;

[Trait("Category", "Integration")]
public sealed class RedisSnapshotPublicationIntegrationTests : IAsyncLifetime
{
    private readonly RedisContainer _container = new RedisBuilder("redis:7-alpine").Build();
    private IConnectionMultiplexer _writer = null!;
    private IConnectionMultiplexer _reader = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _writer = await ConnectionMultiplexer.ConnectAsync(_container.GetConnectionString() + ",allowAdmin=true");
        _reader = await ConnectionMultiplexer.ConnectAsync(_container.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        _writer?.Dispose();
        _reader?.Dispose();
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task SecondConnection_ObservesSavedSnapshotWhenVersionIsPublished()
    {
        var writerStore = Store(_writer);
        var writer = RedisSnapshotTestContext.CreateService(writerStore, new SampleHomeAssistant());
        await writer.InitializeAsync();
        var initialVersion = await writerStore.BumpVersionAsync();

        Assert.True(await writer.RemoveEntityAsync("light.porch"));

        var readerStore = Store(_reader);
        Assert.Equal(initialVersion + 1, await readerStore.GetVersionAsync());
        var stored = await readerStore.TryLoadAsync();
        Assert.NotNull(stored);
        Assert.Equal(initialVersion + 1, stored.Version);
        Assert.Equal(writer.LastLoadedAt, stored.LoadedAt);
        Assert.Equal(2, stored.Snapshot.Floors.Length);
        Assert.Equal(4, stored.Snapshot.Areas.Length);
        Assert.Equal(6, stored.Snapshot.Entities.Length);
        Assert.DoesNotContain(stored.Snapshot.Entities, entity => entity.EntityId == "light.porch");
        foreach (var key in new[] { "floors", "areas", "entities", "loaded-at" })
        {
            var ttl = await _reader.GetDatabase().KeyTimeToLiveAsync($"lucia:location:{key}");
            Assert.NotNull(ttl);
            Assert.InRange(ttl.Value, TimeSpan.FromHours(23), TimeSpan.FromHours(24));
        }

        var secondHome = new SampleHomeAssistant();
        var reader = RedisSnapshotTestContext.CreateService(readerStore, secondHome);
        await reader.InitializeAsync();
        Assert.Equal(0, secondHome.RegistryLoadCount);
        Assert.Equal(6, reader.GetSnapshot().Entities.Length);
        Assert.DoesNotContain(reader.GetSnapshot().Entities, entity => entity.EntityId == "light.porch");
    }

    [Fact]
    public async Task RejectedSnapshotWrites_DoNotPublishNewVersionOrReplaceSharedSnapshot()
    {
        var writerStore = Store(_writer);
        var seed = RedisSnapshotTestContext.CreateService(writerStore, new SampleHomeAssistant());
        await seed.InitializeAsync();
        var initialVersion = await writerStore.BumpVersionAsync();
        var password = Guid.NewGuid().ToString("N");
        // Reject each SET form used for expiring strings, while keeping INCR allowed.
        await _writer.GetDatabase().ExecuteAsync(
            "ACL", "SETUSER", "snapshot-writer", "reset", "on", $">{password}", "~*", "+@all",
            "-set", "-setex", "-psetex");
        var options = ConfigurationOptions.Parse(_container.GetConnectionString());
        options.User = "snapshot-writer";
        options.Password = password;
        using var restricted = await ConnectionMultiplexer.ConnectAsync(options);
        // Prove the failure injection matches the client's actual command, not an assumed wire form.
        await Assert.ThrowsAsync<RedisServerException>(() => restricted.GetDatabase().StringSetAsync(
            "lucia:location:write-probe", "denied", TimeSpan.FromHours(24)));
        var writer = RedisSnapshotTestContext.CreateService(Store(restricted), new SampleHomeAssistant());
        await writer.InitializeAsync();

        var failure = await Record.ExceptionAsync(() => writer.RemoveEntityAsync("light.porch"));

        var readerStore = Store(_reader);
        Assert.Equal(initialVersion, await readerStore.GetVersionAsync());
        var actual = Assert.IsType<RedisServerException>(failure);
        Assert.Contains("NOPERM", actual.Message);
        var stored = await readerStore.TryLoadAsync();
        Assert.NotNull(stored);
        Assert.Equal(initialVersion, stored.Version);
        Assert.Equal(7, stored.Snapshot.Entities.Length);
        Assert.Contains(stored.Snapshot.Entities, entity => entity.EntityId == "light.porch");
    }

    private static RedisLocationSnapshotStore Store(IConnectionMultiplexer redis)
        => new(redis, NullLogger<RedisLocationSnapshotStore>.Instance);
}
