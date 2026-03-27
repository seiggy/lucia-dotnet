namespace lucia.Agents.Models;

public enum BailReason
{
    NoMatch,
    Ambiguous,
    ComplexCommand,
    CacheNotReady,
    UnsupportedIntent
}
