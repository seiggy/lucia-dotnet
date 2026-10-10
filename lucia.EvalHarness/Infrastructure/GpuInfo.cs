namespace lucia.EvalHarness.Infrastructure;

/// <summary>
/// GPU hardware information for evaluation report context.
/// </summary>
public sealed record GpuInfo(
    string GpuLabel,
    string? GpuName = null,
    int? VramMb = null,
    string? DriverVersion = null,
    string Source = "unknown"
);
