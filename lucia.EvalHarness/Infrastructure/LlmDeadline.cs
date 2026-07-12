namespace lucia.EvalHarness.Infrastructure;

/// <summary>
/// Runs an async LLM operation under an internal timeout deadline that is kept
/// distinct from caller cancellation. If the deadline fires first, a
/// <see cref="TimeoutException"/> is thrown; if the caller cancels, the original
/// <see cref="OperationCanceledException"/> propagates unchanged. A non-positive
/// timeout disables the internal deadline and simply honors the caller token.
/// </summary>
internal static class LlmDeadline
{
    public static async Task<TResult> RunAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        TimeSpan timeout,
        TimeProvider timeProvider,
        CancellationToken cancellationToken,
        string timeoutMessage)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (timeout <= TimeSpan.Zero)
        {
            return await operation(cancellationToken).ConfigureAwait(false);
        }

        using var timeoutCts = new CancellationTokenSource(timeout, timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            return await operation(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(timeoutMessage);
        }
    }
}
