namespace lucia.Agents.Services;

/// <summary>
/// A single test case for the skill parameter optimizer.
/// Defines a search term, the expected entity to find, and the maximum
/// acceptable number of results.
/// </summary>
public sealed record OptimizationTestCase
{
    /// <summary>
    /// The search term to pass to the matcher (e.g. "Kitchen Lights", "Sack's Light").
    /// </summary>
    public required string SearchTerm { get; init; }

    /// <summary>
    /// The entity ID that must appear in the results for the test to pass.
    /// </summary>
    public required string ExpectedEntityId { get; init; }

    /// <summary>
    /// Maximum number of matches allowed. Exceeding this is a soft failure
    /// (lower score) but the entity still counts as "found".
    /// </summary>
    public int MaxResults { get; init; } = 3;

    /// <summary>
    /// Human-readable label for this test case (e.g. "stt-phonetic", "exact").
    /// </summary>
    public string? Variant { get; init; }
}
