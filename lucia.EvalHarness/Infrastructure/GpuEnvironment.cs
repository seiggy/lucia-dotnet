using System.Diagnostics;
using lucia.EvalHarness.Configuration;
using lucia.EvalHarness.Providers;

namespace lucia.EvalHarness.Infrastructure;

/// <summary>
/// Detects GPU hardware for evaluation report context.
/// Attempts nvidia-smi first, then Ollama <c>/api/ps</c> for VRAM usage,
/// and falls back to the manual <c>--gpu-label</c> configuration.
/// </summary>
public sealed class GpuEnvironment
{
    private readonly HarnessConfiguration _config;
    private readonly OllamaModelDiscovery _discovery;

    public GpuEnvironment(HarnessConfiguration config, OllamaModelDiscovery discovery)
    {
        _config = config;
        _discovery = discovery;
    }

    /// <summary>
    /// Collects GPU environment information for embedding in reports.
    /// </summary>
    public async Task<GpuInfo> DetectAsync(CancellationToken ct = default)
    {
        // Manual override takes priority
        if (!string.IsNullOrWhiteSpace(_config.GpuLabel))
        {
            return new GpuInfo(GpuLabel: _config.GpuLabel, Source: "manual");
        }

        // Try nvidia-smi
        var nvidiaSmi = await TryNvidiaSmiAsync(ct);
        if (nvidiaSmi is not null)
        {
            return nvidiaSmi;
        }

        // Try Ollama /api/ps for VRAM info
        var ollamaPs = await TryOllamaPsAsync(ct);
        if (ollamaPs is not null)
        {
            return ollamaPs;
        }

        return new GpuInfo(GpuLabel: "Unknown (set --gpu-label or install nvidia-smi)", Source: "none");
    }

    private static async Task<GpuInfo?> TryNvidiaSmiAsync(CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "nvidia-smi",
                Arguments = "--query-gpu=name,memory.total,driver_version --format=csv,noheader,nounits",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null) return null;

            var output = await process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
                return null;

            // Parse "NVIDIA GeForce RTX 4090, 24564, 565.77"
            var parts = output.Trim().Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length < 3) return null;

            var gpuName = parts[0];
            var vramMb = int.TryParse(parts[1], out var vram) ? vram : 0;
            var driverVersion = parts[2];

            var label = $"{gpuName} ({vramMb / 1024.0:F0} GB VRAM, driver {driverVersion})";
            return new GpuInfo(
                GpuLabel: label,
                GpuName: gpuName,
                VramMb: vramMb,
                DriverVersion: driverVersion,
                Source: "nvidia-smi"
            );
        }
        catch
        {
            return null;
        }
    }

    private async Task<GpuInfo?> TryOllamaPsAsync(CancellationToken ct)
    {
        try
        {
            var running = await _discovery.ListRunningModelsAsync(ct);
            if (running.Count == 0) return null;

            var totalVram = running.Sum(m => m.SizeVram);
            if (totalVram <= 0) return null;

            var label = $"GPU detected via Ollama ({totalVram / (1024.0 * 1024 * 1024):F1} GB VRAM in use)";
            return new GpuInfo(GpuLabel: label, VramMb: (int)(totalVram / (1024 * 1024)), Source: "ollama-ps");
        }
        catch
        {
            return null;
        }
    }
}

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
