using System.Collections.Concurrent;
using lucia.Agents.Services;
using Microsoft.Extensions.AI;

namespace lucia.AgentHost.Extensions;

/// <summary>
/// In-memory background job manager for skill parameter optimization.
/// Tracks running/completed jobs in a <see cref="ConcurrentDictionary{TKey,TValue}"/>
/// and supports progress polling and cancellation from the API layer.
/// </summary>
public sealed class SkillOptimizerJobManager
{
    private readonly SkillOptimizerService _optimizer;
    private readonly IEmbeddingProviderResolver _embeddingResolver;
    private readonly IEntityLocationService _locationService;
    private readonly IEnumerable<IOptimizableSkill> _skills;
    private readonly ILogger<SkillOptimizerJobManager> _logger;
    private readonly ConcurrentDictionary<string, OptimizerJobState> _jobs = new();

    public SkillOptimizerJobManager(
        SkillOptimizerService optimizer,
        IEmbeddingProviderResolver embeddingResolver,
        IEntityLocationService locationService,
        IEnumerable<IOptimizableSkill> skills,
        ILogger<SkillOptimizerJobManager> logger)
    {
        _optimizer = optimizer;
        _embeddingResolver = embeddingResolver;
        _locationService = locationService;
        _skills = skills;
        _logger = logger;
    }

    /// <summary>Returns all registered optimizable skills.</summary>
    public IEnumerable<IOptimizableSkill> GetSkills() => _skills;

    /// <summary>Finds a skill by its ID.</summary>
    public IOptimizableSkill? GetSkill(string skillId)
        => _skills.FirstOrDefault(s => string.Equals(s.SkillId, skillId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Gets the current state of a job, or null if not found.</summary>
    public OptimizerJobState? GetJob(string jobId)
        => _jobs.GetValueOrDefault(jobId);

    /// <summary>
    /// Starts a background optimization job for the given skill.
    /// Returns immediately with a job ID for polling.
    /// </summary>
    public string StartJob(StartOptimizerJobRequest request)
    {
        var jobId = Guid.NewGuid().ToString("N")[..12];
        var cts = new CancellationTokenSource();

        var state = new OptimizerJobState
        {
            JobId = jobId,
            SkillId = request.SkillId,
            EmbeddingModel = request.EmbeddingModel,
            TestCaseCount = request.TestCases.Count,
            Status = OptimizerJobStatus.Running,
            StartedAt = DateTime.UtcNow,
            CancellationSource = cts
        };

        _jobs[jobId] = state;

        // Fire and forget — progress is tracked via state object
        _ = RunJobAsync(state, request, cts.Token);

        return jobId;
    }

    /// <summary>Cancels a running job.</summary>
    public bool CancelJob(string jobId)
    {
        if (!_jobs.TryGetValue(jobId, out var state))
            return false;

        if (state.Status != OptimizerJobStatus.Running)
            return false;

        state.CancellationSource.Cancel();
        state.Status = OptimizerJobStatus.Cancelled;
        return true;
    }

    private async Task RunJobAsync(OptimizerJobState state, StartOptimizerJobRequest request, CancellationToken ct)
    {
        try
        {
            var skill = GetSkill(request.SkillId);
            if (skill is null)
            {
                state.Status = OptimizerJobStatus.Failed;
                state.Error = $"Skill '{request.SkillId}' not found";
                return;
            }

            // Resolve embedding generator for the requested model
            var generator = await _embeddingResolver.ResolveAsync(request.EmbeddingModel, ct).ConfigureAwait(false);
            if (generator is null)
            {
                state.Status = OptimizerJobStatus.Failed;
                state.Error = $"Embedding model '{request.EmbeddingModel}' not available";
                return;
            }

            // Get cached entities from the skill, fall back to location service
            var entities = await skill.GetCachedEntitiesAsync(ct).ConfigureAwait(false);
            if (entities.Count == 0)
            {
                _logger.LogInformation(
                    "Skill cache empty for {SkillId}, building matchable entities from location service",
                    request.SkillId);

                entities = await BuildEntitiesFromLocationServiceAsync(skill, generator, ct).ConfigureAwait(false);
            }

            if (entities.Count == 0)
            {
                state.Status = OptimizerJobStatus.Failed;
                state.Error = "No entities available. Ensure the entity location cache has been loaded.";
                return;
            }

            var initialParams = skill.GetCurrentMatchOptions();

            var result = await _optimizer.OptimizeAsync(
                request.TestCases,
                entities,
                generator,
                initialParams,
                onProgress: progress =>
                {
                    state.LatestProgress = progress;
                    return Task.CompletedTask;
                },
                cancellationToken: ct).ConfigureAwait(false);

            state.Result = result;
            state.Status = OptimizerJobStatus.Completed;
            state.CompletedAt = DateTime.UtcNow;

            _logger.LogInformation(
                "Optimizer job {JobId} completed: Score={Score:F1}/{Max:F0} T={T:F4} W={W:F4} D={D:F4}",
                state.JobId, result.Score, result.MaxScore,
                result.BestParams.Threshold, result.BestParams.EmbeddingWeight, result.BestParams.ScoreDropoffRatio);
        }
        catch (OperationCanceledException)
        {
            state.Status = OptimizerJobStatus.Cancelled;
            _logger.LogInformation("Optimizer job {JobId} cancelled", state.JobId);
        }
        catch (Exception ex)
        {
            state.Status = OptimizerJobStatus.Failed;
            state.Error = ex.Message;
            _logger.LogError(ex, "Optimizer job {JobId} failed", state.JobId);
        }
    }

    /// <summary>
    /// Builds <see cref="IMatchableEntity"/> instances from the shared
    /// <see cref="IEntityLocationService"/> when the skill's internal cache
    /// has not been populated (e.g. HA not connected).
    /// Generates embeddings and phonetic keys on the fly.
    /// </summary>
    private async Task<IReadOnlyList<IMatchableEntity>> BuildEntitiesFromLocationServiceAsync(
        IOptimizableSkill skill,
        IEmbeddingGenerator<string, Embedding<float>> generator,
        CancellationToken ct)
    {
        var allEntities = await _locationService.GetEntitiesAsync(ct).ConfigureAwait(false);
        var domains = skill.EntityDomains;

        var filtered = allEntities
            .Where(e => domains.Contains(e.Domain, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (filtered.Count == 0)
            return [];

        // Generate embeddings in batch
        var names = filtered.Select(e => e.FriendlyName).ToList();
        var embeddings = await generator.GenerateAsync(names, cancellationToken: ct).ConfigureAwait(false);

        var result = new List<IMatchableEntity>(filtered.Count);
        for (var i = 0; i < filtered.Count; i++)
        {
            result.Add(new MatchableEntityInfo
            {
                EntityId = filtered[i].EntityId,
                MatchableName = filtered[i].FriendlyName,
                NameEmbedding = embeddings[i],
                PhoneticKeys = StringSimilarity.BuildPhoneticKeys(filtered[i].FriendlyName)
            });
        }

        _logger.LogInformation(
            "Built {Count} matchable entities from location service for skill {SkillId}",
            result.Count, skill.SkillId);

        return result;
    }
}

/// <summary>Mutable state for a running/completed optimizer job.</summary>
public sealed class OptimizerJobState
{
    public required string JobId { get; init; }
    public required string SkillId { get; init; }
    public required string EmbeddingModel { get; init; }
    public int TestCaseCount { get; init; }
    public OptimizerJobStatus Status { get; set; }
    public DateTime StartedAt { get; init; }
    public DateTime? CompletedAt { get; set; }
    public OptimizationProgress? LatestProgress { get; set; }
    public OptimizationResult? Result { get; set; }
    public string? Error { get; set; }
    internal CancellationTokenSource CancellationSource { get; init; } = new();
}

public enum OptimizerJobStatus
{
    Running,
    Completed,
    Failed,
    Cancelled
}

/// <summary>Request to start an optimizer job.</summary>
public sealed record StartOptimizerJobRequest
{
    public required string SkillId { get; init; }
    public required string EmbeddingModel { get; init; }
    public required IReadOnlyList<OptimizationTestCase> TestCases { get; init; }
}
