using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using A2A;
using StackExchange.Redis;

namespace lucia.Agents.Services;

/// <summary>
/// Redis-based implementation of ITaskStore for durable task persistence.
/// </summary>
public sealed class RedisTaskStore : ITaskStore
{
    private const string TaskIdSetKey = "lucia:task-ids";
    
    private readonly IConnectionMultiplexer _redis;
    private readonly JsonSerializerOptions _jsonOptions;
    private static readonly ActivitySource ActivitySource = new("lucia.Agents.RedisTaskStore");
    private static readonly Meter Meter = new("lucia.Agents.RedisTaskStore", "1.0.0");
    
    private readonly Histogram<double> _taskSaveDurationMs;
    private readonly Histogram<double> _taskLoadDurationMs;
    private readonly Counter<long> _taskCacheHits;
    private readonly Counter<long> _taskCacheMisses;

    public RedisTaskStore(IConnectionMultiplexer redis)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        // Initialize metrics
        _taskSaveDurationMs = Meter.CreateHistogram<double>(
            "task_save_duration_ms",
            unit: "ms",
            description: "Duration in milliseconds to save a task to Redis");

        _taskLoadDurationMs = Meter.CreateHistogram<double>(
            "task_load_duration_ms",
            unit: "ms",
            description: "Duration in milliseconds to load a task from Redis");

        _taskCacheHits = Meter.CreateCounter<long>(
            "task_cache_hits",
            description: "Number of successful task loads from cache");

        _taskCacheMisses = Meter.CreateCounter<long>(
            "task_cache_misses",
            description: "Number of task cache misses (not found in Redis)");
    }

    public async Task<AgentTask?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("GetTask");
        activity?.SetTag("taskId", taskId);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        var db = _redis.GetDatabase();
        var key = GetTaskKey(taskId);
        var json = await db.StringGetAsync(key).WaitAsync(cancellationToken).ConfigureAwait(false);

        stopwatch.Stop();
        var durationMs = stopwatch.Elapsed.TotalMilliseconds;
        _taskLoadDurationMs.Record(durationMs, new KeyValuePair<string, object?>("operation", "GetTask"));

        if (json.IsNullOrEmpty)
        {
            activity?.SetTag("found", false);
            _taskCacheMisses.Add(1);
            return null;
        }

        activity?.SetTag("found", true);
        _taskCacheHits.Add(1);
        return JsonSerializer.Deserialize<AgentTask>(json!.ToString(), _jsonOptions);
    }

    public async Task<TaskPushNotificationConfig?> GetPushNotificationAsync(
        string taskId, 
        string notificationConfigId, 
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("GetPushNotification");
        activity?.SetTag("taskId", taskId);
        activity?.SetTag("notificationConfigId", notificationConfigId);

        var db = _redis.GetDatabase();
        var key = GetNotificationKey(taskId, notificationConfigId);
        var json = await db.StringGetAsync(key).WaitAsync(cancellationToken).ConfigureAwait(false);

        if (json.IsNullOrEmpty)
        {
            activity?.SetTag("found", false);
            return null;
        }

        activity?.SetTag("found", true);
        return JsonSerializer.Deserialize<TaskPushNotificationConfig>(json!.ToString(), _jsonOptions);
    }

    public async Task<AgentTaskStatus> UpdateStatusAsync(
        string taskId, 
        TaskState status, 
        AgentMessage? message = null, 
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("UpdateStatus");
        activity?.SetTag("taskId", taskId);
        activity?.SetTag("status", status.ToString());

        var task = await GetTaskAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (task == null)
        {
            throw new A2AException(
                $"Task with ID '{taskId}' not found",
                A2AErrorCode.TaskNotFound);
        }

        // Append the message to History so the full conversation is persisted
        if (message is not null)
        {
            task.History ??= new List<AgentMessage>();
            task.History.Add(message);
        }

        var newStatus = new AgentTaskStatus
        {
            State = status,
            Message = message,
            Timestamp = DateTimeOffset.UtcNow
        };

        task.Status = newStatus;
        await SetTaskAsync(task, cancellationToken).ConfigureAwait(false);

        return newStatus;
    }

    public async Task SetTaskAsync(AgentTask task, CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("SetTask");
        activity?.SetTag("taskId", task.Id);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var db = _redis.GetDatabase();
        var key = GetTaskKey(task.Id);
        var json = JsonSerializer.Serialize(task, _jsonOptions);

        // Set with 24-hour TTL per spec requirements
        await db.StringSetAsync(key, json, TimeSpan.FromHours(24)).WaitAsync(cancellationToken).ConfigureAwait(false);
        
        // Track task ID in the index set (auxiliary bookkeeping)
        _ = db.SetAddAsync(TaskIdSetKey, task.Id, CommandFlags.FireAndForget);

        stopwatch.Stop();
        var durationMs = stopwatch.Elapsed.TotalMilliseconds;
        _taskSaveDurationMs.Record(durationMs, new KeyValuePair<string, object?>("operation", "SetTask"));
        
        activity?.SetTag("bytesSerialized", json.Length);
    }

    public async Task SetPushNotificationConfigAsync(
        TaskPushNotificationConfig pushNotificationConfig, 
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("SetPushNotificationConfig");
        activity?.SetTag("taskId", pushNotificationConfig.TaskId);

        var db = _redis.GetDatabase();
        // Use task ID as the notification ID since TaskPushNotificationConfig doesn't have separate Id
        var key = $"lucia:task:{pushNotificationConfig.TaskId}:notification:default";
        var json = JsonSerializer.Serialize(pushNotificationConfig, _jsonOptions);

        // Set with 24-hour TTL matching task TTL
        await db.StringSetAsync(key, json, TimeSpan.FromHours(24)).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IEnumerable<TaskPushNotificationConfig>> GetPushNotificationsAsync(
        string taskId, 
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("GetPushNotifications");
        activity?.SetTag("taskId", taskId);

        var db = _redis.GetDatabase();
        var server = _redis.GetServer(_redis.GetEndPoints().First());
        var pattern = $"lucia:task:{taskId}:notification:*";
        
        var keys = server.Keys(pattern: pattern).ToList();
        
        if (!keys.Any())
        {
            activity?.SetTag("count", 0);
            return Enumerable.Empty<TaskPushNotificationConfig>();
        }

        var configs = new List<TaskPushNotificationConfig>();
        foreach (var key in keys)
        {
            var json = await db.StringGetAsync(key).WaitAsync(cancellationToken).ConfigureAwait(false);
            if (!json.IsNullOrEmpty)
            {
                var config = JsonSerializer.Deserialize<TaskPushNotificationConfig>(json!.ToString(), _jsonOptions);
                if (config != null)
                {
                    configs.Add(config);
                }
            }
        }

        activity?.SetTag("count", configs.Count);
        return configs;
    }

    private static string GetTaskKey(string taskId) => $"lucia:task:{taskId}";
    
    private static string GetNotificationKey(string taskId, string notificationConfigId) => 
        $"lucia:task:{taskId}:notification:{notificationConfigId}";
}
