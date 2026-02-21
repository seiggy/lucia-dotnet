using Microsoft.Extensions.Logging;

namespace lucia.HomeAssistant.Services;

/// <summary>
/// Compile-time structured logging for HomeAssistantClient.
/// </summary>
public static partial class HomeAssistantClientLogMessages
{
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Debug,
        Message = "Sending GET {Endpoint}")]
    public static partial void GetRequest(this ILogger logger, string endpoint);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Debug,
        Message = "Sending POST {Endpoint}")]
    public static partial void PostRequest(this ILogger logger, string endpoint);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Warning,
        Message = "Entity not found: {EntityId}")]
    public static partial void EntityNotFound(this ILogger logger, string entityId);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Error,
        Message = "HTTP request failed for {Method} {Endpoint}")]
    public static partial void HttpRequestFailed(this ILogger logger, Exception ex, string method, string endpoint);

    [LoggerMessage(
        EventId = 1004,
        Level = LogLevel.Error,
        Message = "Deserialization failed for {Method} {Endpoint}")]
    public static partial void DeserializationFailed(this ILogger logger, Exception ex, string method, string endpoint);
}
