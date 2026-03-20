using System.Diagnostics;
using System.Text.Json;

using lucia.Agents.CommandTracing;

namespace lucia.AgentHost.Conversation.Tracing;

/// <summary>
/// Collects individual tool call records during skill execution.
/// Create one per request and pass it through the dispatch methods.
/// </summary>
public sealed class ToolCallCollector
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly List<CommandTraceToolCall> _calls = [];

    public IReadOnlyList<CommandTraceToolCall> ToolCalls => _calls;

    /// <summary>
    /// Records a tool call with the given method name, arguments, and result.
    /// </summary>
    public async Task<string> RecordAsync(
        string methodName,
        object? arguments,
        Func<Task<string>> invoke)
    {
        var sw = Stopwatch.StartNew();
        string? response = null;
        string? error = null;
        var success = true;

        try
        {
            response = await invoke().ConfigureAwait(false);
            return response;
        }
        catch (Exception ex)
        {
            success = false;
            error = ex.Message;
            throw;
        }
        finally
        {
            sw.Stop();
            _calls.Add(new CommandTraceToolCall
            {
                MethodName = methodName,
                Arguments = arguments is not null ? JsonSerializer.Serialize(arguments, JsonOptions) : null,
                Response = response,
                DurationMs = sw.Elapsed.TotalMilliseconds,
                Success = success,
                Error = error,
            });
        }
    }
}
