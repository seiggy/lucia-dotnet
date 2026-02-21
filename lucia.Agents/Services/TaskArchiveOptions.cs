namespace lucia.Agents.Services;

/// <summary>
/// Configuration for the task archive MongoDB store.
/// </summary>
public sealed class TaskArchiveOptions
{
    public const string SectionName = "TaskArchive";

    /// <summary>MongoDB database name for task archive.</summary>
    public string DatabaseName { get; set; } = "luciatasks";

    /// <summary>MongoDB collection name for archived tasks.</summary>
    public string CollectionName { get; set; } = "tasks";

    /// <summary>Interval between archival sweep runs.</summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromMinutes(5);
}
