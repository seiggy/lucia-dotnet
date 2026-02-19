namespace lucia.Agents.Training.Models;

/// <summary>
/// A paginated result set.
/// </summary>
public sealed class PagedResult<T>
{
    public required List<T> Items { get; set; }

    public int TotalCount { get; set; }

    public int Page { get; set; }

    public int PageSize { get; set; }

    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;
}
