namespace lucia.Agents.Configuration;

/// <summary>
/// Configuration for the task archive store.
/// </summary>
public sealed class TaskArchiveOptions
{
    public const string SectionName = "TaskArchive";


    /// <summary>Interval between archival sweep runs.</summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromMinutes(5);
}
