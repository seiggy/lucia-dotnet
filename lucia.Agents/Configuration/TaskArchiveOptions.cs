namespace lucia.Agents.Configuration;

/// <summary>
/// Configuration for the task archive store.
/// </summary>
public sealed class TaskArchiveOptions
{
    public const string SectionName = "TaskArchive";

    /// <summary>Database name for task archive.</summary>
    public string DatabaseName { get; set; } = "luciatasks";

    /// <summary>Collection name for archived tasks.</summary>
    public string CollectionName { get; set; } = "tasks";

    /// <summary>Interval between archival sweep runs.</summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromMinutes(5);
}
