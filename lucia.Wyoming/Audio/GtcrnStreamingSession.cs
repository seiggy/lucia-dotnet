using System.Numerics;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace lucia.Wyoming.Audio;

/// <summary>
/// Per-stream GTCRN speech enhancement session.
/// Processes audio through STFT → GTCRN ONNX → iSTFT with overlap-add.
/// Maintains internal frame buffers and GTCRN cache state across calls.
/// </summary>
public sealed class GtcrnStreamingSession : ISpeechEnhancerSession
{
    private const int Nfft = 512;
    private const int WindowSize = 512;
    private const int HopSize = 256;
    private const int FreqBins = Nfft / 2 + 1; // 257

    private readonly InferenceSession _session;
    private readonly float[] _window;

    // STFT overlap buffers
    private readonly float[] _inputBuffer;
    private int _inputBufferPos;
    private readonly float[] _outputOverlapBuffer;

    // GTCRN cache state — persisted across frames
    private float[] _convCache;
    private float[] _traCache;
    private float[] _interCache;

    private bool _disposed;

    public GtcrnStreamingSession(InferenceSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;

        // sqrt(hanning) window
        _window = new float[WindowSize];
        for (var i = 0; i < WindowSize; i++)
        {
            _window[i] = MathF.Sqrt(0.5f * (1.0f - MathF.Cos(2.0f * MathF.PI * i / WindowSize)));
        }

        _inputBuffer = new float[WindowSize];
        _inputBufferPos = 0;
        _outputOverlapBuffer = new float[WindowSize];

        // Initialize caches to zeros
        _convCache = new float[2 * 1 * 16 * 16 * 33];
        _traCache = new float[2 * 3 * 1 * 1 * 16];
        _interCache = new float[2 * 1 * 33 * 16];
    }

    public float[] Process(float[] samples)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (samples.Length == 0) return [];

        var outputSamples = new List<float>();

        for (var i = 0; i < samples.Length; i++)
        {
            _inputBuffer[_inputBufferPos++] = samples[i];

            if (_inputBufferPos >= WindowSize)
            {
                // Process one full frame
                var enhanced = ProcessFrame(_inputBuffer);

                // Overlap-add: accumulate into output buffer
                for (var j = 0; j < WindowSize; j++)
                {
                    _outputOverlapBuffer[j] += enhanced[j];
                }

                // Emit the first HopSize samples
                for (var j = 0; j < HopSize; j++)
                {
                    outputSamples.Add(_outputOverlapBuffer[j]);
                }

                // Shift overlap buffer
                Array.Copy(_outputOverlapBuffer, HopSize, _outputOverlapBuffer, 0, WindowSize - HopSize);
                Array.Clear(_outputOverlapBuffer, WindowSize - HopSize, HopSize);

                // Shift input buffer (keep last WindowSize-HopSize samples for overlap)
                Array.Copy(_inputBuffer, HopSize, _inputBuffer, 0, WindowSize - HopSize);
                _inputBufferPos = WindowSize - HopSize;
            }
        }

        return outputSamples.ToArray();
    }

    private float[] ProcessFrame(float[] frame)
    {
        // Apply window
        var windowed = new float[WindowSize];
        for (var i = 0; i < WindowSize; i++)
        {
            windowed[i] = frame[i] * _window[i];
        }

        // FFT (real-valued input → complex output with FreqBins)
        var fftResult = new Complex[Nfft];
        for (var i = 0; i < WindowSize; i++)
        {
            fftResult[i] = new Complex(windowed[i], 0);
        }
        Fft(fftResult, false);

        // Build model input: [1, 257, 1, 2] (real, imag)
        var inputTensor = new DenseTensor<float>(new[] { 1, FreqBins, 1, 2 });
        for (var i = 0; i < FreqBins; i++)
        {
            inputTensor[0, i, 0, 0] = (float)fftResult[i].Real;
            inputTensor[0, i, 0, 1] = (float)fftResult[i].Imaginary;
        }

        // Create cache tensors
        var convCacheTensor = new DenseTensor<float>(_convCache, new[] { 2, 1, 16, 16, 33 });
        var traCacheTensor = new DenseTensor<float>(_traCache, new[] { 2, 3, 1, 1, 16 });
        var interCacheTensor = new DenseTensor<float>(_interCache, new[] { 2, 1, 33, 16 });

        // Run inference
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("mix", inputTensor),
            NamedOnnxValue.CreateFromTensor("conv_cache", convCacheTensor),
            NamedOnnxValue.CreateFromTensor("tra_cache", traCacheTensor),
            NamedOnnxValue.CreateFromTensor("inter_cache", interCacheTensor),
        };

        using var results = _session.Run(inputs);

        // Extract enhanced output
        var enhOutput = results.First(r => r.Name == "enh").AsTensor<float>();
        var convCacheOut = results.First(r => r.Name == "conv_cache_out").AsTensor<float>();
        var traCacheOut = results.First(r => r.Name == "tra_cache_out").AsTensor<float>();
        var interCacheOut = results.First(r => r.Name == "inter_cache_out").AsTensor<float>();

        // Update caches for next frame
        _convCache = convCacheOut.ToArray();
        _traCache = traCacheOut.ToArray();
        _interCache = interCacheOut.ToArray();

        // Inverse FFT
        var enhComplex = new Complex[Nfft];
        for (var i = 0; i < FreqBins; i++)
        {
            enhComplex[i] = new Complex(enhOutput[0, i, 0, 0], enhOutput[0, i, 0, 1]);
        }
        // Mirror for negative frequencies
        for (var i = 1; i < FreqBins - 1; i++)
        {
            enhComplex[Nfft - i] = Complex.Conjugate(enhComplex[i]);
        }
        Fft(enhComplex, true);

        // Apply window to output and take first WindowSize samples
        var output = new float[WindowSize];
        for (var i = 0; i < WindowSize; i++)
        {
            output[i] = (float)enhComplex[i].Real * _window[i];
        }

        return output;
    }

    /// <summary>
    /// In-place radix-2 Cooley-Tukey FFT (or inverse FFT when inverse=true).
    /// </summary>
    private static void Fft(Complex[] data, bool inverse)
    {
        var n = data.Length;
        if (n <= 1) return;

        // Bit-reversal permutation
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
            {
                j ^= bit;
            }
            j ^= bit;

            if (i < j)
            {
                (data[i], data[j]) = (data[j], data[i]);
            }
        }

        // Cooley-Tukey butterfly
        for (var len = 2; len <= n; len <<= 1)
        {
            var angle = 2.0 * Math.PI / len * (inverse ? -1 : 1);
            var wLen = new Complex(Math.Cos(angle), Math.Sin(angle));

            for (var i = 0; i < n; i += len)
            {
                var w = Complex.One;
                for (var j = 0; j < len / 2; j++)
                {
                    var u = data[i + j];
                    var v = data[i + j + len / 2] * w;
                    data[i + j] = u + v;
                    data[i + j + len / 2] = u - v;
                    w *= wLen;
                }
            }
        }

        if (inverse)
        {
            for (var i = 0; i < n; i++)
            {
                data[i] /= n;
            }
        }
    }

    public void Dispose()
    {
        _disposed = true;
        // InferenceSession is shared — don't dispose it here
    }
}
