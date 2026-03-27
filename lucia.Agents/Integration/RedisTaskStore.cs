using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using A2A;
using lucia.Agents.Abstractions;
using StackExchange.Redis;

namespace lucia.Agents.Integration;

/// <summary>
/// Redis-based implementation of ITaskStore for durable task persistence.
/// </summary>
public sealed class RedisTaskStore : ITaskStore, ITaskIdIndex
{
    private const string TaskIdSetKey = "lucia:task-ids";
    
    private readonly IConnectionMultiplexer _redis;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly AgentsTelemetrySource _telemetrySource;
    
    private readonly Histogram<double> _taskSaveDurationMs;
    private readonly Histogram<double> _taskLoadDurationMs;
    private readonly Counter<long> _taskCacheHits;
    private readonly Counter<long> _taskCacheMisses;

    public RedisTaskStore(
        IConnectionMultiplexer redis,
        AgentsTelemetrySource telemetrySource)
    {
        _telemetrySource = telemetrySource;
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        // Initialize metrics
        _taskSaveDurationMs = _telemetrySource.Meter.CreateHistogram<double>(
            "task_save_duration_ms",
            unit: "ms",
            description: "Duration in milliseconds to save a task to Redis");

        _taskLoadDurationMs = _telemetrySource.Meter.CreateHistogram<double>(
            "task_load_duration_ms",
            unit: "ms",
            description: "Duration in milliseconds to load a task from Redis");

        _taskCacheHits = _telemetrySource.Meter.CreateCounter<long>(
            "task_cache_hits",
            description: "Number of successful task loads from cache");

        _taskCacheMisses = _telemetrySource.Meter.CreateCounter<long>(
            "task_cache_misses",
            description: "Number of task cache misses (not found in Redis)");
    }

    public async Task<AgentTask?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        using var activity = _telemetrySource.ActivitySource.StartActivity();
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
        using var activity = _telemetrySource.ActivitySource.StartActivity();
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

    public async Task<A2A.TaskStatus> UpdateStatusAsync(
        string taskId, 
        TaskState status, 
        Message? message = null, 
        CancellationToken cancellationToken = default)
    {
        using var activity = _telemetrySource.ActivitySource.StartActivity();
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
            task.History ??= new List<Message>();
            task.History.Add(message);
        }

        var newStatus = new A2A.TaskStatus
        {
            State = status,
            Message = message,
            Timestamp = DateTimeOffset.UtcNow
        };

        task.Status = newStatus;
        await SaveTaskAsync(task.Id, task, cancellationToken).ConfigureAwait(false);

        return newStatus;
    }

    public async Task SaveTaskAsync(string taskId, AgentTask task, CancellationToken cancellationToken = default)
    {
        using var activity = _telemetrySource.ActivitySource.StartActivity();
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
        using var activity = _telemetrySource.ActivitySource.StartActivity();
        activity?.SetTag("taskId", pushNotificationConfig.TaskId);

        var db = _redis.GetDatabase();
        var notificationId = "default";
        var key = GetNotificationKey(pushNotificationConfig.TaskId, notificationId);
        var json = JsonSerializer.Serialize(pushNotificationConfig, _jsonOptions);

        // Set with 24-hour TTL matching task TTL
        await db.StringSetAsync(key, json, TimeSpan.FromHours(24)).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IEnumerable<TaskPushNotificationConfig>> GetPushNotificationsAsync(
        string taskId, 
        CancellationToken cancellationToken = default)
    {
        using var activity = _telemetrySource.ActivitySource.StartActivity();
        activity?.SetTag("taskId", taskId);

        var db = _redis.GetDatabase();
        var server = _redis.GetServer(_redis.GetEndPoints().First());
        var pattern = $"lucia:task:{taskId}:notification:*";
        
        var keys = server.Keys(pattern: pattern).ToArray();
        
        if (keys.Length == 0)
        {
            activity?.SetTag("count", 0);
            return [];
        }

        // Batch fetch all notification keys in a single MGET instead of N individual GETs
        var redisKeys = keys.Select(k => (RedisKey)k).ToArray();
        var values = await db.StringGetAsync(redisKeys).WaitAsync(cancellationToken).ConfigureAwait(false);

        var configs = new List<TaskPushNotificationConfig>(values.Length);
        foreach (var json in values)
        {
            if (!json.IsNullOrEmpty)
            {
                var config = JsonSerializer.Deserialize<TaskPushNotificationConfig>(json!.ToString(), _jsonOptions);
                if (config is not null)
                {
                    configs.Add(config);
                }
            }
        }

        activity?.SetTag("count", configs.Count);
        return configs;
    }

    public async Task DeleteTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        using var activity = _telemetrySource.ActivitySource.StartActivity();
        activity?.SetTag("taskId", taskId);

        var db = _redis.GetDatabase();
        var key = GetTaskKey(taskId);
        await db.KeyDeleteAsync(key).WaitAsync(cancellationToken).ConfigureAwait(false);
        _ = db.SetRemoveAsync(TaskIdSetKey, taskId, CommandFlags.FireAndForget);
    }

    public async Task<ListTasksResponse> ListTasksAsync(ListTasksRequest request, CancellationToken cancellationToken = default)
    {
        using var activity = _telemetrySource.ActivitySource.StartActivity();

        var db = _redis.GetDatabase();
        var taskIdValues = await db.SetMembersAsync(TaskIdSetKey).WaitAsync(cancellationToken).ConfigureAwait(false);

        var tasks = new List<AgentTask>();
        foreach (var taskIdValue in taskIdValues.Where(v => v.HasValue))
        {
            var task = await GetTaskAsync(taskIdValue.ToString(), cancellationToken).ConfigureAwait(false);
            if (task is not null)
            {
                tasks.Add(task);
            }
        }

        activity?.SetTag("count", tasks.Count);
        return new ListTasksResponse { Tasks = tasks };
    }

    public async Task<IReadOnlyList<string>> GetAllTrackedTaskIdsAsync(CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var taskIdValues = await db.SetMembersAsync(TaskIdSetKey).WaitAsync(cancellationToken).ConfigureAwait(false);

        return taskIdValues
            .Where(v => v.HasValue)
            .Select(v => v.ToString())
            .ToList();
    }

    public async Task RemoveTaskIdAsync(string taskId, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        await db.SetRemoveAsync(TaskIdSetKey, taskId).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string GetTaskKey(string taskId) => $"lucia:task:{taskId}";
    
    private static string GetNotificationKey(string taskId, string notificationConfigId) => 
        $"lucia:task:{taskId}:notification:{notificationConfigId}";
}
