using Microsoft.Extensions.Logging;

namespace lucia.Tests.TestDoubles;

/// <summary>Keeps every log call so a test can assert on level, exception and rendered message.</summary>
public sealed class RecordingLogger : ILogger
{
    public List<(LogLevel Level, Exception? Exception, string Message)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
        => Entries.Add((logLevel, exception, formatter(state, exception)));
}
