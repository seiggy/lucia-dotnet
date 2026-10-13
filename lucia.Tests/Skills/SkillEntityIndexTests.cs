using FakeItEasy;
using lucia.Agents.Abstractions;
using lucia.Agents.Skills;
using lucia.Tests.TestDoubles;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace lucia.Tests.Skills;

public sealed class SkillEntityIndexTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

    private static IEmbeddingProviderResolver Resolver(IEmbeddingGenerator<string, Embedding<float>>? generator)
    {
        var resolver = A.Fake<IEmbeddingProviderResolver>();
        A.CallTo(() => resolver.ResolveAsync(A<string?>._, A<CancellationToken>._))
            .Returns(Task.FromResult(generator));
        return resolver;
    }

    private static SkillEntityIndex<string> CreateIndex(
        IEmbeddingProviderResolver resolver,
        Func<IEmbeddingGenerator<string, Embedding<float>>, CancellationToken, Task<IReadOnlyList<string>>> load,
        TimeProvider? time = null)
        => new(resolver, NullLogger.Instance, "test", load, () => Interval, timeProvider: time);

    private static Task<IReadOnlyList<string>> Items(params string[] items) => Task.FromResult<IReadOnlyList<string>>(items);

    [Fact]
    public async Task InitializeAsync_LoadsTheItemsWithTheResolvedGenerator()
    {
        var generator = new DeterministicEmbeddingGenerator();
        IEmbeddingGenerator<string, Embedding<float>>? seen = null;
        var index = CreateIndex(Resolver(generator), (g, _) => { seen = g; return Items("a", "b"); });

        await index.InitializeAsync();

        Assert.Equal(["a", "b"], index.Items.ToArray());
        Assert.Same(generator, seen);
        Assert.Same(generator, index.Embeddings);
    }

    [Fact]
    public async Task InitializeAsync_NoProvider_LoadsNothing()
    {
        var loads = 0;
        var index = CreateIndex(Resolver(null), (_, _) => { loads++; return Items("a"); });

        await index.InitializeAsync();

        Assert.Empty(index.Items);
        Assert.Null(index.Embeddings);
        Assert.Equal(0, loads);
    }

    [Fact]
    public async Task EnsureCurrentAsync_BeforeTheFirstLoad_Loads()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var loads = 0;
        var index = CreateIndex(Resolver(new DeterministicEmbeddingGenerator()), (_, _) => { loads++; return Items("a"); }, time);
        await index.UpdateEmbeddingProviderAsync(null);

        await index.EnsureCurrentAsync();

        Assert.Equal(1, loads);
        Assert.Equal(["a"], index.Items.ToArray());
    }

    [Fact]
    public async Task EnsureCurrentAsync_WithinTheInterval_DoesNotReload()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var loads = 0;
        var index = CreateIndex(Resolver(new DeterministicEmbeddingGenerator()), (_, _) => { loads++; return Items("a"); }, time);
        await index.InitializeAsync();

        time.Advance(Interval - TimeSpan.FromSeconds(1));
        await index.EnsureCurrentAsync();

        Assert.Equal(1, loads);
    }

    [Fact]
    public async Task EnsureCurrentAsync_AfterTheInterval_Reloads()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var generation = 0;
        var index = CreateIndex(
            Resolver(new DeterministicEmbeddingGenerator()), (_, _) => Items($"gen{++generation}"), time);
        await index.InitializeAsync();

        time.Advance(Interval + TimeSpan.FromSeconds(1));
        await index.EnsureCurrentAsync();

        Assert.Equal(["gen2"], index.Items.ToArray());
    }

    [Fact]
    public async Task EnsureCurrentAsync_WhileAnotherReloadRuns_UsesTheCurrentItemsWithoutWaiting()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loads = 0;
        var index = CreateIndex(
            Resolver(new DeterministicEmbeddingGenerator()),
            async (_, _) =>
            {
                if (Interlocked.Increment(ref loads) == 2)
                {
                    started.SetResult();
                    await gate.Task;
                }

                return ["a"];
            },
            time);
        await index.InitializeAsync();
        time.Advance(Interval + TimeSpan.FromSeconds(1));

        var first = index.EnsureCurrentAsync();
        await started.Task;
        await index.EnsureCurrentAsync();
        gate.SetResult();
        await first;

        Assert.Equal(2, loads);
    }

    [Fact]
    public async Task EnsureCurrentAsync_WithNoItemsWhileAnotherReloadRuns_WaitsForIt()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loads = 0;
        var index = CreateIndex(
            Resolver(new DeterministicEmbeddingGenerator()),
            async (_, _) =>
            {
                Interlocked.Increment(ref loads);
                started.TrySetResult();
                await gate.Task;
                return ["a"];
            },
            time);
        await index.UpdateEmbeddingProviderAsync(null);

        var first = index.EnsureCurrentAsync();
        await started.Task;
        var second = index.EnsureCurrentAsync();
        Assert.False(second.IsCompleted);

        gate.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(1, loads);
        Assert.Equal(["a"], index.Items.ToArray());
    }

    [Fact]
    public async Task EnsureCurrentAsync_AfterAFailedLoad_KeepsTheOldItemsAndRetries()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var calls = 0;
        var index = CreateIndex(
            Resolver(new DeterministicEmbeddingGenerator()),
            (_, _) => ++calls switch
            {
                1 => Items("old"),
                2 => throw new InvalidOperationException("boom"),
                _ => Items("new"),
            },
            time);
        await index.InitializeAsync();

        time.Advance(Interval + TimeSpan.FromSeconds(1));
        await index.EnsureCurrentAsync();
        Assert.Equal(["old"], index.Items.ToArray());

        await index.EnsureCurrentAsync();
        Assert.Equal(["new"], index.Items.ToArray());
    }

    [Fact]
    public async Task EnsureCurrentAsync_CallerCancellation_Propagates()
    {
        var index = CreateIndex(
            Resolver(new DeterministicEmbeddingGenerator()), (_, _) => throw new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() => index.InitializeAsync());
    }

    [Fact]
    public async Task ReloadAsync_DropsTheItemsAndLoadsAgainBeforeTheIntervalEnds()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var generation = 0;
        var index = CreateIndex(
            Resolver(new DeterministicEmbeddingGenerator()), (_, _) => Items($"gen{++generation}"), time);
        await index.InitializeAsync();

        await index.ReloadAsync();

        Assert.Equal(["gen2"], index.Items.ToArray());
    }

    [Fact]
    public async Task ReloadAsync_WhenTheLoadFails_LeavesNoItemsAndRetriesOnTheNextCall()
    {
        var calls = 0;
        var index = CreateIndex(
            Resolver(new DeterministicEmbeddingGenerator()),
            (_, _) => ++calls switch
            {
                1 => Items("old"),
                2 => throw new InvalidOperationException("boom"),
                _ => Items("new"),
            });
        await index.InitializeAsync();

        await index.ReloadAsync();
        Assert.Empty(index.Items);

        await index.EnsureCurrentAsync();
        Assert.Equal(["new"], index.Items.ToArray());
    }

    [Fact]
    public async Task UpdateEmbeddingProviderAsync_SwitchesTheGeneratorUsedByTheNextLoad()
    {
        var first = new DeterministicEmbeddingGenerator();
        var second = new DeterministicEmbeddingGenerator();
        var resolver = A.Fake<IEmbeddingProviderResolver>();
        A.CallTo(() => resolver.ResolveAsync(A<string?>._, A<CancellationToken>._)).ReturnsLazily(call =>
            Task.FromResult<IEmbeddingGenerator<string, Embedding<float>>?>(call.GetArgument<string?>(0) == "other" ? second : first));
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        IEmbeddingGenerator<string, Embedding<float>>? used = null;
        var index = CreateIndex(resolver, (g, _) => { used = g; return Items("a"); }, time);
        await index.InitializeAsync();

        await index.UpdateEmbeddingProviderAsync("other");
        time.Advance(Interval + TimeSpan.FromSeconds(1));
        await index.EnsureCurrentAsync();

        Assert.Same(second, used);
    }
}
