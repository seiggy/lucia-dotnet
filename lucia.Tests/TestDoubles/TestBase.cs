using FakeItEasy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Tests.TestDoubles;

/// <summary>
/// Base class for unit tests with common test utilities.
/// </summary>
public abstract class TestBase
{
    /// <summary>
    /// Creates a fake ILogger instance for testing.
    /// </summary>
    protected ILogger<T> CreateLogger<T>() => A.Fake<ILogger<T>>();
    
    /// <summary>
    /// Creates an IOptions wrapper for configuration testing.
    /// </summary>
    protected IOptions<T> CreateOptions<T>(T value) where T : class => Options.Create(value);
    
    /// <summary>
    /// Creates a fake ILogger instance (non-generic).
    /// </summary>
    protected ILogger CreateLogger() => A.Fake<ILogger>();
    
    /// <summary>
    /// Creates a fake ILoggerFactory for testing.
    /// </summary>
    protected ILoggerFactory CreateLoggerFactory() => A.Fake<ILoggerFactory>();
}
