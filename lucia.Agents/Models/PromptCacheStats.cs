namespace lucia.Agents.Models;

/// <summary>
/// Summary statistics for the prompt cache.
/// </summary>
public sealed class PromptCacheStats
{
    public long TotalEntries { get; set; }

    public long TotalHits { get; set; }

    public long TotalMisses { get; set; }

    public double HitRate => TotalHits + TotalMisses > 0 ? (double)TotalHits / (TotalHits + TotalMisses) : 0;
}
