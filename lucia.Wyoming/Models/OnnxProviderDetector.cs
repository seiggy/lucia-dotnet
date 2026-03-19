using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;

namespace lucia.Wyoming.Models;

/// <summary>
/// Probes the ONNX Runtime once at startup to determine which execution
/// providers are available (CUDA, OpenVINO, DirectML, CoreML, etc.) and selects
/// the best one. All engines should use <see cref="BestProvider"/> or
/// <see cref="BestSherpaProvider"/> instead of hardcoding "cpu".
/// </summary>
public sealed class OnnxProviderDetector
{
    // Priority order: GPU accelerators first, then specialized hardware, then CPU.
    // ORT name → sherpa-onnx name mapping. sherpa-onnx supports: cuda, directml, coreml, cpu.
    // OpenVINO and ROCm are ORT-level providers — sherpa-onnx falls back to cpu for these.
    private static readonly (string OrtName, string SherpaName, int Priority)[] KnownAccelerators =
    [
        ("CUDAExecutionProvider", "cuda", 100),
        ("MIGraphXExecutionProvider", "migraphx", 95),
        ("ROCMExecutionProvider", "cpu", 90),           // deprecated in ROCm 7.1+; MIGraphX preferred
        ("OpenVINOExecutionProvider", "cpu", 85),       // sherpa-onnx has no OpenVINO support
        ("DmlExecutionProvider", "directml", 80),
        ("CoreMLExecutionProvider", "coreml", 70),
    ];

    /// <summary>
    /// ONNX Runtime execution provider name (e.g. "CUDAExecutionProvider", "OpenVINOExecutionProvider").
    /// Use when configuring <see cref="SessionOptions"/> for Microsoft.ML.OnnxRuntime engines.
    /// </summary>
    public string BestProvider { get; }

    /// <summary>
    /// sherpa-onnx provider name (e.g. "cuda", "cpu").
    /// Use with sherpa-onnx config <c>Provider</c> fields. Note: sherpa-onnx does not
    /// support OpenVINO — when OpenVINO is the best ORT provider, this returns "cpu".
    /// </summary>
    public string BestSherpaProvider { get; }

    /// <summary>All providers reported by the ONNX Runtime.</summary>
    public IReadOnlyList<string> AvailableProviders { get; }

    /// <summary>Whether a GPU/hardware-accelerated provider was detected.</summary>
    public bool IsAccelerated { get; }

    public OnnxProviderDetector(ILogger<OnnxProviderDetector> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        string[] available;
        try
        {
            available = OrtEnv.Instance().GetAvailableProviders();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to query ONNX Runtime providers — falling back to CPU");
            available = ["CPUExecutionProvider"];
        }

        AvailableProviders = available;

        var bestOrt = "CPUExecutionProvider";
        var bestSherpa = "cpu";
        var isAccelerated = false;

        foreach (var (ortName, sherpaName, _) in KnownAccelerators.OrderByDescending(a => a.Priority))
        {
            if (available.Contains(ortName, StringComparer.OrdinalIgnoreCase))
            {
                bestOrt = ortName;
                bestSherpa = sherpaName;
                isAccelerated = true;
                break;
            }
        }

        BestProvider = bestOrt;
        BestSherpaProvider = bestSherpa;
        IsAccelerated = isAccelerated;

        logger.LogInformation(
            "ONNX provider auto-detection: selected {Provider} (sherpa: {SherpaProvider}) from available [{Available}]",
            BestProvider,
            BestSherpaProvider,
            string.Join(", ", available));
    }

    /// <summary>
    /// Configures a <see cref="SessionOptions"/> with the best available accelerator.
    /// Falls through to CPU if the accelerated provider fails to initialize.
    /// </summary>
    public void ConfigureSessionOptions(SessionOptions options, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!IsAccelerated)
        {
            return;
        }

        try
        {
            switch (BestProvider)
            {
                case "CUDAExecutionProvider":
                    options.AppendExecutionProvider_CUDA();
                    break;
                case "MIGraphXExecutionProvider":
                    options.AppendExecutionProvider_MIGraphX();
                    break;
                case "ROCMExecutionProvider":
                    options.AppendExecutionProvider_ROCm();
                    break;
                case "OpenVINOExecutionProvider":
                    options.AppendExecutionProvider_OpenVINO();
                    break;
                case "DmlExecutionProvider":
                    options.AppendExecutionProvider_DML();
                    break;
                case "CoreMLExecutionProvider":
                    options.AppendExecutionProvider_CoreML();
                    break;
            }

            logger.LogDebug("Configured ONNX session with {Provider}", BestProvider);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to configure {Provider} — session will fall back to CPU",
                BestProvider);
        }
    }
}
