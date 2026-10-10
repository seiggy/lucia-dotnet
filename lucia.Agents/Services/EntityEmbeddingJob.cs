using System.Collections.Immutable;
using lucia.Agents.Abstractions;
using lucia.Agents.Models.HomeAssistant;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Services;

/// <summary>
/// Generates name embeddings for a snapshot's floors, areas and entities in throttled batches on a
/// background task. Starting a new job cancels the previous one.
/// </summary>
public sealed class EntityEmbeddingJob
{
    private const int BatchSize = 25;
    private const int BatchMaxAttempts = 3;
    private static readonly TimeSpan BatchDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

    private readonly IEmbeddingProviderResolver _embeddingResolver;
    private readonly ILogger<EntityEmbeddingJob> _logger;
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private Task? _task;
    private long _jobCounter;
    private long _activeJobId;
    private int _isRunning;

    public EntityEmbeddingJob(IEmbeddingProviderResolver embeddingResolver, ILogger<EntityEmbeddingJob> logger)
    {
        _embeddingResolver = embeddingResolver;
        _logger = logger;
    }

    public bool IsRunning => Volatile.Read(ref _isRunning) == 1;

    /// <param name="persistProgress">Called after each batch so partial results survive a restart.</param>
    public void Start(
        ImmutableArray<FloorInfo> floors,
        ImmutableArray<AreaInfo> areas,
        ImmutableArray<HomeAssistantEntity> entities,
        Func<CancellationToken, Task> persistProgress)
    {
        lock (_gate)
        {
            _cts?.Cancel();
            _cts?.Dispose();

            var cts = new CancellationTokenSource();
            _cts = cts;
            var jobId = Interlocked.Increment(ref _jobCounter);
            Volatile.Write(ref _activeJobId, jobId);
            Volatile.Write(ref _isRunning, 1);
            _task = Task.Run(
                () => RunAsync(floors, areas, entities, persistProgress, jobId, cts.Token),
                cts.Token);
        }
    }

    public static Embedding<float> Clone(Embedding<float> source)
    {
        var cloned = source.Vector.ToArray();
        return new Embedding<float>(cloned)
        {
            CreatedAt = source.CreatedAt,
            ModelId = source.ModelId,
            AdditionalProperties = source.AdditionalProperties
        };
    }

    private async Task RunAsync(
        ImmutableArray<FloorInfo> floors,
        ImmutableArray<AreaInfo> areas,
        ImmutableArray<HomeAssistantEntity> entities,
        Func<CancellationToken, Task> persistProgress,
        long jobId,
        CancellationToken ct)
    {
        try
        {
            var embeddingService = await _embeddingResolver.ResolveAsync(ct: ct).ConfigureAwait(false);
            if (embeddingService is null)
            {
                _logger.LogWarning("No embedding provider available — embedding-based search will be disabled");
                return;
            }

            var namesToEmbed = new Dictionary<string, List<Action<Embedding<float>>>>(StringComparer.OrdinalIgnoreCase);

            void RegisterForEmbedding(string name, Action<Embedding<float>> setter, bool hasEmbedding)
            {
                if (hasEmbedding || string.IsNullOrWhiteSpace(name))
                {
                    return;
                }

                if (!namesToEmbed.TryGetValue(name, out var setters))
                {
                    setters = [];
                    namesToEmbed[name] = setters;
                }

                setters.Add(setter);
            }

            foreach (var floor in floors)
            {
                RegisterForEmbedding(floor.Name, e => floor.NameEmbedding = e, floor.NameEmbedding is not null);
            }

            foreach (var area in areas)
            {
                RegisterForEmbedding(area.Name, e => area.NameEmbedding = e, area.NameEmbedding is not null);
            }

            foreach (var entity in entities)
            {
                RegisterForEmbedding(entity.FriendlyName, e => entity.NameEmbedding = e, entity.NameEmbedding is not null);
            }

            if (namesToEmbed.Count == 0)
            {
                _logger.LogDebug("Embedding cache already complete for current location snapshot");
                return;
            }

            var names = namesToEmbed.Keys.ToList();
            var generated = 0;

            for (var offset = 0; offset < names.Count; offset += BatchSize)
            {
                ct.ThrowIfCancellationRequested();

                var batchNames = names.Skip(offset).Take(BatchSize).ToArray();
                var batchEmbeddings = await TryGenerateBatchWithRetryAsync(embeddingService, batchNames, ct).ConfigureAwait(false);
                if (batchEmbeddings is null)
                {
                    continue;
                }

                var count = Math.Min(batchNames.Length, batchEmbeddings.Count);
                for (var i = 0; i < count; i++)
                {
                    if (!namesToEmbed.TryGetValue(batchNames[i], out var setters))
                    {
                        continue;
                    }

                    foreach (var setter in setters)
                    {
                        setter(Clone(batchEmbeddings[i]));
                    }

                    generated++;
                }

                await persistProgress(ct).ConfigureAwait(false);

                if (offset + BatchSize < names.Count)
                {
                    await Task.Delay(BatchDelay, ct).ConfigureAwait(false);
                }
            }

            _logger.LogInformation(
                "Generated {GeneratedCount} embeddings for location snapshot (requested {RequestedCount})",
                generated,
                namesToEmbed.Count);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogDebug("Embedding generation job cancelled");
        }
        catch (Exception ex) when (!ex.IsCancellation())
        {
            _logger.LogWarning(ex, "Background embedding generation failed");
        }
        finally
        {
            if (Volatile.Read(ref _activeJobId) == jobId)
            {
                Volatile.Write(ref _isRunning, 0);
            }
        }
    }

    private async Task<GeneratedEmbeddings<Embedding<float>>?> TryGenerateBatchWithRetryAsync(
        IEmbeddingGenerator<string, Embedding<float>> embeddingService,
        string[] batchNames,
        CancellationToken ct)
    {
        for (var attempt = 1; attempt <= BatchMaxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                return await embeddingService.GenerateAsync(batchNames, cancellationToken: ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt < BatchMaxAttempts)
            {
                _logger.LogWarning(
                    ex,
                    "Embedding batch failed on attempt {Attempt}/{MaxAttempts}; retrying in {DelayMs}ms",
                    attempt,
                    BatchMaxAttempts,
                    RetryDelay.TotalMilliseconds);
                await Task.Delay(RetryDelay, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (!ex.IsCancellation())
            {
                _logger.LogWarning(
                    ex,
                    "Embedding batch failed after {MaxAttempts} attempts for {ItemCount} items",
                    BatchMaxAttempts,
                    batchNames.Length);
                return null;
            }
        }

        return null;
    }
}
