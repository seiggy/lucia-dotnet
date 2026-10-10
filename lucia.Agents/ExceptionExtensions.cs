namespace lucia.Agents;

public static class ExceptionExtensions
{
    /// <summary>
    /// True when the exception means the caller cancelled the operation. <c>HttpClient.Timeout</c> also
    /// throws <see cref="OperationCanceledException"/>, but with a <see cref="TimeoutException"/> inside.
    /// That is a failure to handle, not a cancellation to propagate.
    /// </summary>
    public static bool IsCancellation(this Exception exception)
        => exception is OperationCanceledException && exception.InnerException is not TimeoutException;
}