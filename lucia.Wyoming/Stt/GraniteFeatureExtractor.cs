using System.Numerics;
using System.Text.Json;

namespace lucia.Wyoming.Stt;

/// <summary>
/// Extracts Whisper-compatible log-mel spectrogram features from audio.
/// Default parameters: 80 mel bins, 400 n_fft, 160 hop_length, 16kHz sample rate.
/// </summary>
public sealed class GraniteFeatureExtractor
{
    private readonly int _nFft;
    private readonly int _winLength;
    private readonly int _hopLength;
    private readonly int _sampleRate;
    private readonly int _fftSize;
    private readonly float[] _window;
    private readonly float[,] _melFilters;

    public int NMels { get; }

    public GraniteFeatureExtractor(
        int nMels = 80,
        int nFft = 512,
        int winLength = 400,
        int hopLength = 160,
        int sampleRate = 16000)
    {
        NMels = nMels;
        _nFft = nFft;
        _winLength = winLength > 0 ? winLength : nFft;
        _hopLength = hopLength;
        _sampleRate = sampleRate;
        _fftSize = NextPowerOfTwo(nFft);
        _window = CreateHannWindow(_winLength);
        _melFilters = CreateMelFilterbank(nMels, nFft, sampleRate);
    }

    /// <summary>
    /// Creates a feature extractor from a HuggingFace preprocessor_config.json file.
    /// Supports both flat layout and nested melspec_kwargs format.
    /// </summary>
    public static GraniteFeatureExtractor FromPreprocessorConfig(string configPath)
    {
        if (!File.Exists(configPath))
            return new GraniteFeatureExtractor();

        var json = File.ReadAllText(configPath);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Granite Speech uses nested melspec_kwargs; Whisper uses flat properties
        var melKwargs = root.TryGetProperty("melspec_kwargs", out var mk) ? mk
            : root.TryGetProperty("audio_processor", out var ap)
                && ap.TryGetProperty("melspec_kwargs", out var mk2) ? mk2
            : root;

        var nMels = TryGetInt(melKwargs, "n_mels") ?? TryGetInt(root, "feature_size") ?? 80;
        var nFft = TryGetInt(melKwargs, "n_fft") ?? 512;
        var winLength = TryGetInt(melKwargs, "win_length") ?? nFft;
        var hopLength = TryGetInt(melKwargs, "hop_length") ?? 160;
        var sampleRate = TryGetInt(melKwargs, "sample_rate")
            ?? TryGetInt(root, "sampling_rate") ?? 16000;

        return new GraniteFeatureExtractor(nMels, nFft, winLength, hopLength, sampleRate);
    }

    private static int? TryGetInt(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var val) && val.ValueKind == JsonValueKind.Number
            ? val.GetInt32()
            : null;
    }

    /// <summary>
    /// Extracts log-mel spectrogram features from audio samples.
    /// </summary>
    /// <param name="audio">Audio samples normalized to [-1, 1] at the configured sample rate.</param>
    /// <param name="nFrames">Number of time frames in the output.</param>
    /// <returns>Feature array in row-major [nMels, nFrames] layout.</returns>
    public float[] ExtractFeatures(float[] audio, out int nFrames)
    {
        // Pad with winLength/2 on each side (center=True in torch.stft)
        var padLength = _winLength / 2;
        var padded = new float[audio.Length + 2 * padLength];

        // Reflect padding on left
        for (var i = 0; i < padLength; i++)
            padded[padLength - 1 - i] = audio[Math.Min(i + 1, audio.Length - 1)];

        // Copy audio
        Array.Copy(audio, 0, padded, padLength, audio.Length);

        // Reflect padding on right
        for (var i = 0; i < padLength; i++)
            padded[padLength + audio.Length + i] = audio[Math.Max(0, audio.Length - 2 - i)];

        nFrames = 1 + (padded.Length - _winLength) / _hopLength;
        var freqBins = _nFft / 2 + 1;

        // Compute STFT power spectrogram
        var fftBuffer = new Complex[_fftSize];
        var magnitudesSq = new float[nFrames * freqBins];

        for (var frame = 0; frame < nFrames; frame++)
        {
            var offset = frame * _hopLength;

            // Zero-pad the FFT buffer, then apply windowed samples
            Array.Clear(fftBuffer, 0, _fftSize);
            for (var i = 0; i < _winLength && (offset + i) < padded.Length; i++)
                fftBuffer[i] = new Complex(padded[offset + i] * _window[i], 0);

            Fft(fftBuffer, inverse: false);

            for (var k = 0; k < freqBins; k++)
            {
                var re = (float)fftBuffer[k].Real;
                var im = (float)fftBuffer[k].Imaginary;
                magnitudesSq[frame * freqBins + k] = re * re + im * im;
            }
        }

        // Apply mel filterbank: features[m, t] = sum_k(melFilters[m, k] * magnitudesSq[t, k])
        var features = new float[NMels * nFrames];
        for (var m = 0; m < NMels; m++)
        {
            for (var t = 0; t < nFrames; t++)
            {
                var sum = 0f;
                var magOffset = t * freqBins;
                for (var k = 0; k < freqBins; k++)
                    sum += _melFilters[m, k] * magnitudesSq[magOffset + k];
                features[m * nFrames + t] = sum;
            }
        }

        // Whisper-style log normalization
        var maxLogSpec = float.NegativeInfinity;
        for (var i = 0; i < features.Length; i++)
        {
            features[i] = MathF.Log10(MathF.Max(features[i], 1e-10f));
            if (features[i] > maxLogSpec) maxLogSpec = features[i];
        }

        for (var i = 0; i < features.Length; i++)
        {
            features[i] = MathF.Max(features[i], maxLogSpec - 8.0f);
            features[i] = (features[i] + 4.0f) / 4.0f;
        }

        return features;
    }

    private static float[] CreateHannWindow(int length)
    {
        var window = new float[length];
        for (var i = 0; i < length; i++)
            window[i] = 0.5f * (1.0f - MathF.Cos(2.0f * MathF.PI * i / length));
        return window;
    }

    private static float HzToMel(float hz) => 2595.0f * MathF.Log10(1.0f + hz / 700.0f);

    private static float MelToHz(float mel) => 700.0f * (MathF.Pow(10.0f, mel / 2595.0f) - 1.0f);

    private static float[,] CreateMelFilterbank(int nMels, int nFft, int sampleRate)
    {
        var freqBins = nFft / 2 + 1;
        var fMin = 0f;
        var fMax = sampleRate / 2f;

        var melMin = HzToMel(fMin);
        var melMax = HzToMel(fMax);

        // nMels + 2 equally spaced mel-frequency points
        var melPoints = new float[nMels + 2];
        for (var i = 0; i < nMels + 2; i++)
            melPoints[i] = melMin + (melMax - melMin) * i / (nMels + 1);

        // Convert back to Hz
        var hzPoints = new float[nMels + 2];
        for (var i = 0; i < nMels + 2; i++)
            hzPoints[i] = MelToHz(melPoints[i]);

        // FFT frequency bin centers
        var fftFreqs = new float[freqBins];
        for (var i = 0; i < freqBins; i++)
            fftFreqs[i] = (float)sampleRate * i / nFft;

        // Build triangular filters
        var filters = new float[nMels, freqBins];
        for (var m = 0; m < nMels; m++)
        {
            var lower = hzPoints[m];
            var center = hzPoints[m + 1];
            var upper = hzPoints[m + 2];

            for (var k = 0; k < freqBins; k++)
            {
                if (fftFreqs[k] >= lower && fftFreqs[k] < center && center > lower)
                    filters[m, k] = (fftFreqs[k] - lower) / (center - lower);
                else if (fftFreqs[k] >= center && fftFreqs[k] <= upper && upper > center)
                    filters[m, k] = (upper - fftFreqs[k]) / (upper - center);
            }

            // Slaney normalization: normalize each filter to have unit area
            var enorm = 2.0f / (upper - lower);
            for (var k = 0; k < freqBins; k++)
                filters[m, k] *= enorm;
        }

        return filters;
    }

    private static int NextPowerOfTwo(int n)
    {
        var p = 1;
        while (p < n) p <<= 1;
        return p;
    }

    /// <summary>
    /// In-place radix-2 Cooley-Tukey FFT.
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
                j ^= bit;
            j ^= bit;

            if (i < j)
                (data[i], data[j]) = (data[j], data[i]);
        }

        // Butterfly stages
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
                data[i] /= n;
        }
    }
}
