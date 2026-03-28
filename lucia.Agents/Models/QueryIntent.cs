namespace lucia.Agents.Models;

internal sealed record QueryIntent
{
    public required string NormalizedQuery { get; init; }
    public string? Action { get; init; }
    public string? ExplicitLocation { get; init; }
    public string? DeviceType { get; init; }
    public bool IsComplex { get; init; }
    public string? ComplexityReason { get; init; }
    public IReadOnlyList<string> CandidateEntityNames { get; init; } = [];
    public IReadOnlyList<string> CandidateAreaNames { get; init; } = [];
}
