using FakeItEasy;
using lucia.Agents.Abstractions;
using lucia.EvalHarness.Providers;
using lucia.EvalHarness.Tests.TestDoubles;

namespace lucia.EvalHarness.Tests.Providers;

public class RealAgentInstanceTests
{
    [Fact]
    public async Task DisposeAsync_CalledTwice_DisposesOwnedClientExactlyOnce()
    {
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
    public async Task DisposeAsync_WhenTrackedBeforeInitFailure_OwnedClientReleasedOnFactoryDisposal()
    {
        // Simulates the factory pattern:
        //   _instances.Add(instance);   ← tracked BEFORE InitializeAsync
        //   await agent.InitializeAsync();  ← throws
        //   ...exception propagates...
        //   factory.DisposeAsync();     ← cascades to all tracked instances
        var client = new CountingChatClient();
        var trackedInstances = new List<RealAgentInstance>();

        var instance = new RealAgentInstance
        {
            AgentName = "test",
            Agent = A.Fake<ILuciaAgent>(),
            DatasetFile = "test.yaml",
            OwnedChatClient = client
        };

        trackedInstances.Add(instance); // mirrors _instances.Add before InitializeAsync

        // Simulate factory.DisposeAsync — called even when InitializeAsync threw
        foreach (var i in trackedInstances)
            await i.DisposeAsync();

        Assert.Equal(1, client.DisposeCount);
    }
}
