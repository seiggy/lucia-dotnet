using lucia.HomeAssistant.Services;
using Microsoft.Extensions.Time.Testing;

namespace lucia.Tests;

/// <summary>
/// Deterministic tests for <see cref="AsyncDeadline"/>, which backs the internal
/// timeout applied to Home Assistant WebSocket commands. Uses <see cref="FakeTimeProvider"/>
/// to fire the deadline while an operation is genuinely in-flight (not a pre-cancel).
/// </summary>
public sealed class AsyncDeadlineTests
{
    [Fact]
    public async Task RunAsync_InternalTimeoutFiresWhileInFlight_ThrowsTimeoutException()
    {
        var timeProvider = new FakeTimeProvider();

        var task = AsyncDeadline.RunAsync<int>(
            BlockUntilCanceledAsync,
            TimeSpan.FromSeconds(30),
            timeProvider,
            CancellationToken.None,
            "op timed out");

        // Advance past the deadline while the operation is still awaiting.
        timeProvider.Advance(TimeSpan.FromSeconds(31));

        await Assert.ThrowsAsync<TimeoutException>(() => task);
    }

    [Fact]
    public async Task RunAsync_CallerCancels_PropagatesOperationCanceled_NotTimeout()
    {
        var timeProvider = new FakeTimeProvider();
        using var caller = new CancellationTokenSource();

        var task = AsyncDeadline.RunAsync<int>(
            BlockUntilCanceledAsync,
            TimeSpan.FromSeconds(30),
            timeProvider,
            caller.Token,
            "op timed out");

        caller.Cancel();

        // Caller cancellation must surface as OperationCanceledException, never TimeoutException.
        await Assert.ThrowsAsync<TaskCanceledException>(() => task);
    }

    [Fact]
    public async Task RunAsync_OperationCompletesBeforeDeadline_ReturnsResult()
    {
        var timeProvider = new FakeTimeProvider();

        var result = await AsyncDeadline.RunAsync<int>(
            _ => Task.FromResult(7),
            TimeSpan.FromSeconds(30),
            timeProvider,
            CancellationToken.None,
            "op timed out");

        Assert.Equal(7, result);
    }

    [Fact]
    public async Task RunAsync_NonPositiveTimeout_DisablesDeadline_HonorsCallerToken()
    {
        var timeProvider = new FakeTimeProvider();
        using var caller = new CancellationTokenSource();
        caller.Cancel();

        // No internal deadline; the pre-cancelled caller token must still cancel the op.
        await Assert.ThrowsAsync<TaskCanceledException>(() => AsyncDeadline.RunAsync<int>(
            BlockUntilCanceledAsync,
            TimeSpan.Zero,
            timeProvider,
            caller.Token,
            "op timed out"));
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
