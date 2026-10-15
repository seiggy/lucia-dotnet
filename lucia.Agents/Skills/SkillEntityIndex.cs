using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using lucia.Agents.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Skills;

/// <summary>
/// Holds the entities a skill searches, with the embedding generator they are matched against. It
/// refreshes through a loader the skill supplies, at most once per interval, and never from two
/// callers at once. A failed refresh keeps the previous items and tries again on the next call.
/// </summary>
public sealed partial class SkillEntityIndex<T>
{
    private readonly IEmbeddingProviderResolver _embeddingResolver;
    private readonly ILogger _logger;
    private readonly string _name;
    private readonly Func<IEmbeddingGenerator<string, Embedding<float>>, CancellationToken, Task<IReadOnlyList<T>>> _load;
    private readonly Func<TimeSpan> _refreshInterval;
    private readonly Histogram<double>? _refreshDuration;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private ImmutableArray<T> _items = [];
    private long _lastRefreshTicks;

    /// <param name="name">What the entities are, such as "climate". Appears in log messages.</param>
    /// <param name="load">Builds the full item list. It receives the embedding generator to embed names with.</param>
    /// <param name="refreshInterval">How old the items may get before the next call reloads them.</param>
    public SkillEntityIndex(
        IEmbeddingProviderResolver embeddingResolver,
        ILogger logger,
        string name,
        Func<IEmbeddingGenerator<string, Embedding<float>>, CancellationToken, Task<IReadOnlyList<T>>> load,
        Func<TimeSpan> refreshInterval,
        Histogram<double>? refreshDuration = null,
        TimeProvider? timeProvider = null)
    {
        _embeddingResolver = embeddingResolver;
        _logger = logger;
        _name = name;
        _load = load;
        _refreshInterval = refreshInterval;
        _refreshDuration = refreshDuration;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public ImmutableArray<T> Items => _items;

    /// <summary>Null until a provider resolves.</summary>
    public IEmbeddingGenerator<string, Embedding<float>>? Embeddings { get; private set; }

    /// <summary>Resolves the default embedding provider and loads the items.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Embeddings = await _embeddingResolver.ResolveAsync(ct: cancellationToken).ConfigureAwait(false);
        if (Embeddings is null)
        {
            LogNoProvider(_name);
            return;
        }

        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Switches to another provider. The items keep the embeddings they have until the next reload.</summary>
    public async Task UpdateEmbeddingProviderAsync(string? providerName, CancellationToken cancellationToken = default)
    {
        Embeddings = await _embeddingResolver.ResolveAsync(providerName, cancellationToken).ConfigureAwait(false);
        LogProviderUpdated(_name, providerName ?? "system-default");
    }

    /// <summary>
    /// Reloads when the items are older than the interval. A caller that finds a reload already running
    /// uses the current items and returns. If there are no items yet, it waits for that reload instead.
    /// </summary>
    public async Task EnsureCurrentAsync(CancellationToken cancellationToken = default)
    {
        if (!IsStale())
            return;

        if (_items.IsEmpty)
        {
            await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (!await _refreshLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            if (IsStale())
                await RefreshAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>Drops the items and reloads them now, whatever their age.</summary>
    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _items = [];
            Volatile.Write(ref _lastRefreshTicks, 0);
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private bool IsStale()
    {
        var last = new DateTimeOffset(Volatile.Read(ref _lastRefreshTicks), TimeSpan.Zero);
        return _timeProvider.GetUtcNow() - last > _refreshInterval();
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var embeddings = Embeddings;
        if (embeddings is null)
        {
            LogSkippedRefresh(_name);
            return;
        }

        var start = Stopwatch.GetTimestamp();
        try
        {
            var items = await _load(embeddings, cancellationToken).ConfigureAwait(false);
            _items = [.. items];
            Volatile.Write(ref _lastRefreshTicks, _timeProvider.GetUtcNow().UtcTicks);
        }
        catch (Exception ex) when (!ex.IsCancellation())
        {
            LogRefreshFailed(ex, _name);
        }
        finally
        {
            _refreshDuration?.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No embedding provider configured — {Name} semantic search will not be available.")]
    private partial void LogNoProvider(string name);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Name}: embedding provider updated to '{Provider}'")]
    private partial void LogProviderUpdated(string name, string provider);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipping {Name} cache refresh — no embedding provider available.")]
    private partial void LogSkippedRefresh(string name);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error refreshing {Name} cache")]
    private partial void LogRefreshFailed(Exception exception, string name);
}
