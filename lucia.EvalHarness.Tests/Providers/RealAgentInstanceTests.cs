using FakeItEasy;
using lucia.Agents.Abstractions;
using lucia.EvalHarness.Configuration;
using lucia.EvalHarness.Providers;
using lucia.EvalHarness.Tests.TestDoubles;
using Microsoft.Extensions.Logging;

namespace lucia.EvalHarness.Tests.Providers;

public class RealAgentInstanceTests
{
    [Fact]
    public async Task DisposeAsync_CalledTwice_DisposesOwnedClientExactlyOnce()
    {
        // Instance-level idempotency: the Interlocked guard must make the second
        // call a no-op even if both a sweep runner and the factory safety-net
        // dispose the same instance.
        var client = new CountingChatClient();
        var instance = new RealAgentInstance
        {
            AgentName = "test",
            Agent = A.Fake<ILuciaAgent>(),
            DatasetFile = "test.yaml",
            OwnedChatClient = client
        };

        await instance.DisposeAsync();
        await instance.DisposeAsync(); // second call must be a no-op

        Assert.Equal(1, client.DisposeCount);
    }

    [Fact]
    public async Task CreateDynamicAgentAsync_AgentCtorFails_DisposesOwnedClientExactlyOnce()
    {
        // Drives the real RealAgentFactory.CreateDynamicAgentAsync production path.
        //
        // The factory injects a CountingChatClient via ChatClientCreator.
        // Agent construction fails before _instances.Add (tracked=false) because the
        // faked IAgentDefinitionRepository returns a fake AgentDefinition with null
        // properties, causing DynamicAgent..ctor to throw.  The catch block must
        // dispose ownedClient directly; subsequent factory.DisposeAsync must not
        // double-dispose it.
        using var loggerFactory = LoggerFactory.Create(_ => { });
        var client = new CountingChatClient();

        var factory = new RealAgentFactory(
            new InferenceBackend { Name = "test", Endpoint = "http://localhost:11434" },
            "nonexistent-snapshot.json",
            loggerFactory)
        {
            ChatClientCreator = (_, _, _) => client
        };

        // Construction fails before _instances.Add; catch block disposes directly.
        await Assert.ThrowsAnyAsync<Exception>(
            () => factory.CreateDynamicAgentAsync("any-model", "nonexistent-id"));

        // ownedClient must be disposed exactly once in the catch block (no leak)
        Assert.Equal(1, client.DisposeCount);

        // factory.DisposeAsync iterates _instances (empty) — must not double-dispose
        await factory.DisposeAsync();
        Assert.Equal(1, client.DisposeCount);
    }
}