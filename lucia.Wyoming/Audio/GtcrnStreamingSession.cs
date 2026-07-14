using System.Numerics;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace lucia.Wyoming.Audio;

/// <summary>
/// Per-stream GTCRN speech enhancement session.
/// Processes audio through STFT → GTCRN ONNX → iSTFT with overlap-add.
/// Maintains internal frame buffers and GTCRN cache state across calls.
/// A session belongs to one audio stream and must be processed sequentially.
/// </summary>
public sealed class GtcrnStreamingSession : ISpeechEnhancerSession
{
    private const int Nfft = 512;
    private const int WindowSize = 512;
    private const int HopSize = 256;
    private const int FreqBins = Nfft / 2 + 1; // 257
    private static readonly string[] s_inputNames = ["mix", "conv_cache", "tra_cache", "inter_cache"];
    private static readonly string[] s_outputNames = ["enh", "conv_cache_out", "tra_cache_out", "inter_cache_out"];

    private readonly InferenceSession _session;
    private readonly float[] _window;
    private readonly Complex[] _fftBuffer;
    private readonly float[] _modelInput;
    private readonly float[] _enhancedOutput;
    private readonly FixedBufferOnnxValue[] _inputValues;
    private readonly FixedBufferOnnxValue[] _outputValues;

    // STFT overlap buffers
    private readonly float[] _inputBuffer;
    private int _inputBufferPos;
    private readonly float[] _outputOverlapBuffer;

    // GTCRN cache state — persisted across frames
    private readonly float[] _convCache;
    private readonly float[] _traCache;
    private readonly float[] _interCache;
    private readonly float[] _convCacheOutput;
    private readonly float[] _traCacheOutput;
    private readonly float[] _interCacheOutput;

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
        _fftBuffer = new Complex[Nfft];
        _modelInput = new float[FreqBins * 2];
        _enhancedOutput = new float[FreqBins * 2];

        // Initialize caches to zeros
        _convCache = new float[2 * 1 * 16 * 16 * 33];
        _traCache = new float[2 * 3 * 1 * 1 * 16];
        _interCache = new float[2 * 1 * 33 * 16];
        _convCacheOutput = new float[_convCache.Length];
        _traCacheOutput = new float[_traCache.Length];
        _interCacheOutput = new float[_interCache.Length];

        _inputValues =
        [
            FixedBufferOnnxValue.CreateFromTensor(new DenseTensor<float>(_modelInput, [1, FreqBins, 1, 2])),
            FixedBufferOnnxValue.CreateFromTensor(new DenseTensor<float>(_convCache, [2, 1, 16, 16, 33])),
            FixedBufferOnnxValue.CreateFromTensor(new DenseTensor<float>(_traCache, [2, 3, 1, 1, 16])),
            FixedBufferOnnxValue.CreateFromTensor(new DenseTensor<float>(_interCache, [2, 1, 33, 16])),
        ];
        _outputValues =
        [
            FixedBufferOnnxValue.CreateFromTensor(new DenseTensor<float>(_enhancedOutput, [1, FreqBins, 1, 2])),
            FixedBufferOnnxValue.CreateFromTensor(new DenseTensor<float>(_convCacheOutput, [2, 1, 16, 16, 33])),
            FixedBufferOnnxValue.CreateFromTensor(new DenseTensor<float>(_traCacheOutput, [2, 3, 1, 1, 16])),
            FixedBufferOnnxValue.CreateFromTensor(new DenseTensor<float>(_interCacheOutput, [2, 1, 33, 16])),
        ];
    }

    public float[] Process(float[] samples)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (samples.Length == 0) return [];

        var availableSamples = (long)_inputBufferPos + samples.Length;
        var frameCount = availableSamples < WindowSize
            ? 0
            : 1 + ((availableSamples - WindowSize) / HopSize);
        var outputSamples = frameCount == 0
            ? []
            : new float[checked((int)frameCount * HopSize)];
        var outputPosition = 0;

        for (var i = 0; i < samples.Length; i++)
        {
            _inputBuffer[_inputBufferPos++] = samples[i];

            if (_inputBufferPos >= WindowSize)
            {
                // Process one full frame
                ProcessFrame(_inputBuffer);

                // Emit the first HopSize samples
                _outputOverlapBuffer.AsSpan(0, HopSize)
                    .CopyTo(outputSamples.AsSpan(outputPosition, HopSize));
                outputPosition += HopSize;

                // Shift overlap buffer
                Array.Copy(_outputOverlapBuffer, HopSize, _outputOverlapBuffer, 0, WindowSize - HopSize);
                Array.Clear(_outputOverlapBuffer, WindowSize - HopSize, HopSize);

                // Shift input buffer (keep last WindowSize-HopSize samples for overlap)
                Array.Copy(_inputBuffer, HopSize, _inputBuffer, 0, WindowSize - HopSize);
                _inputBufferPos = WindowSize - HopSize;
            }
        }

        return outputSamples;
    }

    private void ProcessFrame(float[] frame)
    {
        // Apply window and prepare the real-valued FFT input
        for (var i = 0; i < WindowSize; i++)
        {
            _fftBuffer[i] = new Complex(frame[i] * _window[i], 0);
        }

        Fft(_fftBuffer, false);

        // Build model input: [1, 257, 1, 2] (real, imag)
        for (var i = 0; i < FreqBins; i++)
        {
            _modelInput[i * 2] = (float)_fftBuffer[i].Real;
            _modelInput[(i * 2) + 1] = (float)_fftBuffer[i].Imaginary;
        }

        // Run inference
        _session.Run(s_inputNames, _inputValues, s_outputNames, _outputValues);

        // Update caches for next frame
        _convCacheOutput.AsSpan().CopyTo(_convCache);
        _traCacheOutput.AsSpan().CopyTo(_traCache);
        _interCacheOutput.AsSpan().CopyTo(_interCache);

        // Inverse FFT
        for (var i = 0; i < FreqBins; i++)
        {
            _fftBuffer[i] = new Complex(_enhancedOutput[i * 2], _enhancedOutput[(i * 2) + 1]);
        }
        // Mirror for negative frequencies
        for (var i = 1; i < FreqBins - 1; i++)
        {
            _fftBuffer[Nfft - i] = Complex.Conjugate(_fftBuffer[i]);
        }
        Fft(_fftBuffer, true);

        // Apply window and overlap-add directly into the persistent output buffer
        for (var i = 0; i < WindowSize; i++)
        {
            _outputOverlapBuffer[i] += (float)_fftBuffer[i].Real * _window[i];
        }
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
        if (_disposed) return;
        _disposed = true;
        foreach (var value in _outputValues)
        {
            value.Dispose();
        }
        foreach (var value in _inputValues)
        {
            value.Dispose();
        }
        // InferenceSession is shared — don't dispose it here
    }
}
