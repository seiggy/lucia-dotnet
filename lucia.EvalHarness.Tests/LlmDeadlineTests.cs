using lucia.EvalHarness.Infrastructure;
using Microsoft.Extensions.Time.Testing;

namespace lucia.EvalHarness.Tests;

/// <summary>
/// Deterministic tests for <see cref="LlmDeadline"/>, which backs the per-call
/// timeout applied to every eval-harness LLM site. Uses <see cref="FakeTimeProvider"/>
/// to fire the deadline while an operation is genuinely in-flight (not a pre-cancel).
/// </summary>
public sealed class LlmDeadlineTests
{
    [Fact]
    public async Task RunAsync_InternalTimeoutFiresWhileInFlight_ThrowsTimeoutException()
    {
        var timeProvider = new FakeTimeProvider();

        var task = LlmDeadline.RunAsync<int>(
            BlockUntilCanceledAsync,
            TimeSpan.FromSeconds(120),
            timeProvider,
            CancellationToken.None,
            "op timed out");

        timeProvider.Advance(TimeSpan.FromSeconds(121));

        await Assert.ThrowsAsync<TimeoutException>(() => task);
    }

    [Fact]
    public async Task RunAsync_CallerCancels_PropagatesOperationCanceled_NotTimeout()
    {
        var timeProvider = new FakeTimeProvider();
        using var caller = new CancellationTokenSource();

        var task = LlmDeadline.RunAsync<int>(
            BlockUntilCanceledAsync,
            TimeSpan.FromSeconds(120),
            timeProvider,
            caller.Token,
            "op timed out");

        caller.Cancel();

        await Assert.ThrowsAsync<TaskCanceledException>(() => task);
    }

    [Fact]
    public async Task RunAsync_OperationCompletesBeforeDeadline_ReturnsResult()
    {
        var timeProvider = new FakeTimeProvider();

        var result = await LlmDeadline.RunAsync<int>(
            _ => Task.FromResult(11),
            TimeSpan.FromSeconds(120),
            timeProvider,
            CancellationToken.None,
            "op timed out");

        Assert.Equal(11, result);
    }

    /// <summary>
    /// Deterministic stand-in for an in-flight operation: completes only when the
    /// supplied token is canceled (by the deadline or the caller), throwing
    /// <see cref="OperationCanceledException"/>. Uses a <see cref="TaskCompletionSource{TResult}"/>
    /// rather than <c>Task.Delay</c> so no wall-clock time is involved.
    /// </summary>
    private static async Task<int> BlockUntilCanceledAsync(CancellationToken token)
    {
        var tcs = new TaskCompletionSource<int>();
        await using var registration = token.Register(() => tcs.TrySetCanceled(token));
        return await tcs.Task;
    }
}
