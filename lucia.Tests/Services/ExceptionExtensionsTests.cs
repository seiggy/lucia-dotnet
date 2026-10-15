using lucia.Agents;

namespace lucia.Tests.Services;

public sealed class ExceptionExtensionsTests
{
    [Fact]
    public void IsCancellation_CallerCancellation_ReturnsTrue()
    {
        Assert.True(new OperationCanceledException().IsCancellation());
        Assert.True(new TaskCanceledException().IsCancellation());
    }

    [Fact]
    public void IsCancellation_HttpClientTimeout_ReturnsFalse()
    {
        var timeout = new TaskCanceledException("timed out", new TimeoutException());

        Assert.False(timeout.IsCancellation());
    }

    [Fact]
    public void IsCancellation_OtherException_ReturnsFalse()
    {
        Assert.False(new InvalidOperationException().IsCancellation());
    }
}