using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace lucia.TimerAgent.ScheduledTasks;

/// <summary>
/// Startup service that loads recoverable tasks from MongoDB into the
/// in-memory <see cref="ScheduledTaskStore"/>. Tasks that are too old
/// to be meaningful are marked as failed and skipped.
/// </summary>
public sealed class ScheduledTaskRecoveryService : BackgroundService
{
    /// <summary>
    /// Tasks older than this threshold are marked failed instead of recovered.
    /// </summary>
    private static readonly TimeSpan MaxRecoveryAge = TimeSpan.FromMinutes(30);

    private readonly ScheduledTaskStore _store;
    private readonly IScheduledTaskRepository _repository;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ScheduledTaskRecoveryService> _logger;

    public ScheduledTaskRecoveryService(
        ScheduledTaskStore store,
        IScheduledTaskRepository repository,
        TimeProvider timeProvider,
        ILogger<ScheduledTaskRecoveryService> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation("ScheduledTaskRecoveryService starting — loading persisted tasks");

            var documents = await _repository.GetRecoverableTasksAsync(stoppingToken).ConfigureAwait(false);

            if (documents.Count == 0)
            {
                _logger.LogInformation("No recoverable scheduled tasks found");
                return;
            }

            var now = _timeProvider.GetUtcNow();
            var recovered = 0;
            var expired = 0;

            foreach (var doc in documents)
            {
                if (stoppingToken.IsCancellationRequested)
                    break;

                var age = now - doc.FireAt;

                if (age > MaxRecoveryAge)
                {
                    _logger.LogWarning(
                        "Scheduled task {TaskId} ({TaskType}) expired during downtime — marking failed (age: {Age})",
                        doc.Id, doc.TaskType, age);

                    await _repository.UpdateStatusAsync(doc.Id, ScheduledTaskStatus.Failed, stoppingToken)
                        .ConfigureAwait(false);
                    expired++;
                    continue;
                }

                var task = ScheduledTaskFactory.FromDocument(doc);
                if (task is null)
                {
                    _logger.LogWarning(
                        "Unknown task type {TaskType} for document {TaskId} — skipping",
                        doc.TaskType, doc.Id);
                    continue;
                }

                _store.Add(task);
                recovered++;
            }

            _logger.LogInformation(
                "ScheduledTaskRecoveryService finished — recovered: {Recovered}, expired: {Expired}, total: {Total}",
                recovered, expired, documents.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to recover scheduled tasks from MongoDB");
        }
    }
}
