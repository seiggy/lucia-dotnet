namespace lucia.Tests.Orchestration;

/// <summary>
/// Classification of why a scenario failed for a given model.
/// Used by <see cref="ModelComparisonReporter"/> to categorize failures
/// in the comparison matrix.
/// </summary>
public enum FailureType
{
    /// <summary>No failure — scenario passed.</summary>
    None,

    /// <summary>The model called the wrong tool entirely.</summary>
    WrongTool,

    /// <summary>The correct tool was called but with incorrect parameters.</summary>
    WrongParams,

    /// <summary>The entity/searchTerms argument did not resolve to the expected entity.</summary>
    WrongEntity,

    /// <summary>The model produced no tool calls when one was expected.</summary>
    NoToolCall,

    /// <summary>The model produced a tool call when none was expected (out-of-domain leak).</summary>
    Hallucination,
}
