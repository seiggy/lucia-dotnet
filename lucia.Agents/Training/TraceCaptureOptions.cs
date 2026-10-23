namespace lucia.Agents.Training;

/// <summary>
/// Configuration options for conversation trace capture.
/// Bind to the "TraceCapture" configuration section.
/// </summary>
public sealed class TraceCaptureOptions
{
    public const string SectionName = "TraceCapture";

    /// <summary>Whether trace capture is enabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Explicit opt-in to sanitized tool-result previews. Disabled by default.</summary>
    public bool CaptureToolResults { get; set; }

    /// <summary>Maximum UTF-8 bytes per preview, between 1 and 65536. Invalid values suppress previews.</summary>
    public int MaxToolResultBytes { get; set; } = 4096;

    /// <summary>Separate opt-in to sanitized tool results in newly generated training exports.</summary>
    public bool IncludeToolResultsInTrainingExport { get; set; }

    /// <summary>Number of days to retain unlabeled traces before automatic cleanup.</summary>
    public int RetentionDays { get; set; } = 30;

    /// <summary>Regex patterns for redacting sensitive data from trace content.</summary>
    public string[] RedactionPatterns { get; set; } = [
        @"(?i)(api[_-]?key|token|secret|password|bearer)\s*[:=]\s*\S+",
        @"eyJ[A-Za-z0-9_-]+\.eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+"
    ];

}
