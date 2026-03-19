namespace lucia.Agents.CommandTracing;

/// <summary>
/// Trace data captured during response template rendering.
/// Shows which template was selected, how many variants were available,
/// and which placeholders were replaced.
/// </summary>
public sealed record CommandTraceTemplateRender
{
    /// <summary>Skill/action key (e.g., "LightControlSkill::toggle").</summary>
    public required string TemplateKey { get; init; }

    /// <summary>The raw template string before placeholder substitution.</summary>
    public required string RawTemplate { get; init; }

    /// <summary>The rendered output after placeholder replacement.</summary>
    public required string RenderedText { get; init; }

    /// <summary>Total number of template variants available for random selection.</summary>
    public required int VariantCount { get; init; }

    /// <summary>The zero-based index of the variant that was selected.</summary>
    public required int SelectedIndex { get; init; }

    /// <summary>Placeholder tokens that were replaced, with their substituted values.</summary>
    public IReadOnlyDictionary<string, string> ReplacedTokens { get; init; } = new Dictionary<string, string>();

    /// <summary>True when no template was found and the fallback response was used.</summary>
    public bool IsFallback { get; init; }
}
