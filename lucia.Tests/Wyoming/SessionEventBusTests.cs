using lucia.Wyoming.Wyoming;

namespace lucia.Tests.Wyoming;

public sealed class SessionEventBusTests
{
    [Fact]
    public async Task Publish_SingleSubscriber_ReceivesEvent()
    {
        var bus = new SessionEventBus();
        using var cts = new CancellationTokenSource();
        var received = new List<SessionEvent>();

        var readTask = Task.Run(async () =>
        {
            await foreach (var evt in bus.SubscribeAsync(cts.Token))
            {
                received.Add(evt);
                if (received.Count >= 1) cts.Cancel();
            }
        });

        // Allow subscription to register
        await Task.Delay(50);

        bus.Publish(new SessionConnectedEvent { SessionId = "s1", RemoteEndPoint = "127.0.0.1" });

        try { await readTask; }
        catch (OperationCanceledException) { }

        Assert.Single(received);
        var connected = Assert.IsType<SessionConnectedEvent>(received[0]);
        Assert.Equal("s1", connected.SessionId);
        Assert.Equal("127.0.0.1", connected.RemoteEndPoint);
    }

    [Fact]
    public async Task Publish_MultipleSubscribers_AllReceive()
    {
        var bus = new SessionEventBus();
        using var cts1 = new CancellationTokenSource();
        using var cts2 = new CancellationTokenSource();
        var received1 = new List<SessionEvent>();
        var received2 = new List<SessionEvent>();

        var readTask1 = Task.Run(async () =>
        {
            await foreach (var evt in bus.SubscribeAsync(cts1.Token))
            {
                received1.Add(evt);
                if (received1.Count >= 1) cts1.Cancel();
            }
        });

        var readTask2 = Task.Run(async () =>
        {
            await foreach (var evt in bus.SubscribeAsync(cts2.Token))
            {
                received2.Add(evt);
                if (received2.Count >= 1) cts2.Cancel();
            }
        });

        await Task.Delay(50);

        bus.Publish(new SessionDisconnectedEvent { SessionId = "s2" });

        try { await readTask1; } catch (OperationCanceledException) { }
        try { await readTask2; } catch (OperationCanceledException) { }

        Assert.Single(received1);
        Assert.Single(received2);
        Assert.Equal("s2", received1[0].SessionId);
        Assert.Equal("s2", received2[0].SessionId);
    }

    [Fact]
    public async Task Subscribe_AfterPublish_DoesNotReceivePastEvents()
    {
        var bus = new SessionEventBus();

        // Publish before anyone subscribes
        bus.Publish(new SessionConnectedEvent { SessionId = "past", RemoteEndPoint = "10.0.0.1" });

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var received = new List<SessionEvent>();

        try
        {
            await foreach (var evt in bus.SubscribeAsync(cts.Token))
            {
                received.Add(evt);
            }
        }
        catch (OperationCanceledException) { }

        Assert.Empty(received);
    }

    [Fact]
    public async Task Subscribe_Cancellation_StopsEnumeration()
    {
        var bus = new SessionEventBus();
        using var cts = new CancellationTokenSource();
        var received = new List<SessionEvent>();

        var readTask = Task.Run(async () =>
        {
            await foreach (var evt in bus.SubscribeAsync(cts.Token))
            {
                received.Add(evt);
            }
        });

        await Task.Delay(50);

        // Publish one event, then cancel
        bus.Publish(new SessionConnectedEvent { SessionId = "x", RemoteEndPoint = "0.0.0.0" });
        await Task.Delay(50);
        cts.Cancel();

        try { await readTask; }
        catch (OperationCanceledException) { }

        // Enumeration should have completed; we received exactly the one event before cancellation
        Assert.Single(received);
    }

    [Fact]
    public void Publish_NoSubscribers_DoesNotThrow()
    {
        var bus = new SessionEventBus();

        var exception = Record.Exception(() =>
            bus.Publish(new SessionConnectedEvent { SessionId = "lonely", RemoteEndPoint = "::1" }));

        Assert.Null(exception);
    }
}
