using System.Diagnostics;
using System.Text.Json;
using lucia.Wyoming.Audio;
using lucia.Wyoming.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace lucia.Wyoming.Stt;

/// <summary>
/// Offline speech-to-text engine using IBM Granite 4.0 Speech ONNX models.
/// Implements the 3-model architecture: audio_encoder → embed_tokens → decoder_model_merged.
/// </summary>
public sealed class GraniteOnnxEngine : IGraniteEngine, IDisposable
{
    private readonly ILogger<GraniteOnnxEngine> _logger;
    private readonly IModelChangeNotifier _modelChangeNotifier;
    private readonly OnnxProviderDetector _providerDetector;
    private readonly GraniteOptions _options;
    private readonly object _lock = new();

    private InferenceSession? _encoderSession;
    private InferenceSession? _embedSession;
    private InferenceSession? _decoderSession;
    private GraniteFeatureExtractor? _featureExtractor;
    private GraniteTokenizer? _tokenizer;

    // Model config read from config.json
    private int _numLayers;
    private int _numKvHeads;
    private int _headDim;
    private int _hiddenSize;
    private int _eosTokenId;
    private int _padTokenId;

    public bool IsReady { get; private set; }

    public GraniteOnnxEngine(
        IOptions<GraniteOptions> options,
        IModelChangeNotifier modelChangeNotifier,
        OnnxProviderDetector providerDetector,
        ILogger<GraniteOnnxEngine> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(modelChangeNotifier);
        ArgumentNullException.ThrowIfNull(providerDetector);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value;
        _modelChangeNotifier = modelChangeNotifier;
        _providerDetector = providerDetector;
        _logger = logger;

        if (_options.Enabled)
            TryLoadModel(_options.ModelPath);

        _modelChangeNotifier.ActiveModelChanged += OnActiveModelChanged;
    }

    public Task<GraniteTranscript> TranscribeAsync(
        float[] audio,
        int sampleRate,
        IReadOnlyList<KeywordBias>? keywordBias = null,
        CancellationToken ct = default)
    {
        if (!IsReady)
            throw new InvalidOperationException("Granite engine is not ready.");

        return Task.Run(() => TranscribeCore(audio, sampleRate, keywordBias, ct), ct);
    }

    public void Dispose()
    {
        _modelChangeNotifier.ActiveModelChanged -= OnActiveModelChanged;
        lock (_lock)
        {
            IsReady = false;
            _encoderSession?.Dispose();
            _embedSession?.Dispose();
            _decoderSession?.Dispose();
            _encoderSession = null;
            _embedSession = null;
            _decoderSession = null;
        }
    }

    private GraniteTranscript TranscribeCore(
        float[] audio, int sampleRate,
        IReadOnlyList<KeywordBias>? keywordBias, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        lock (_lock)
        {
            if (!IsReady || _encoderSession is null || _featureExtractor is null || _tokenizer is null)
                return new GraniteTranscript();

            // Resample if needed
            if (sampleRate != _options.SampleRate)
                audio = AudioResampler.Resample(audio.AsSpan(), sampleRate, _options.SampleRate);

            // 1. Extract mel features
            var features = _featureExtractor.ExtractFeatures(audio, out var nFrames);

            ct.ThrowIfCancellationRequested();

            // 2. Run audio encoder → audio embeddings
            var audioEmbeds = RunAudioEncoder(features, nFrames);

            ct.ThrowIfCancellationRequested();

            // 3. Build keyword bias token weights
            var biasWeights = BuildKeywordBiasWeights(keywordBias);

            // 4. Run decoder (auto-regressive or CTC depending on model structure)
            int[] tokenIds;
            if (_decoderSession is not null && _embedSession is not null)
            {
                tokenIds = RunAutoRegressiveDecoder(audioEmbeds, biasWeights, ct);
            }
            else if (_decoderSession is not null)
            {
                tokenIds = RunSimpleDecoder(audioEmbeds, ct);
            }
            else
            {
                tokenIds = CtcGreedyDecode(audioEmbeds.data, audioEmbeds.shape);
            }

            // 4. Decode tokens → text
            var text = _tokenizer.Decode(tokenIds);
            sw.Stop();

            _logger.LogDebug(
                "Granite transcribed {AudioMs}ms audio \u2192 \"{Text}\" in {InferenceMs}ms ({TokenCount} tokens)",
                audio.Length * 1000 / _options.SampleRate,
                text, sw.ElapsedMilliseconds, tokenIds.Length);

            return new GraniteTranscript
            {
                Text = text,
                Confidence = 1.0f,
                InferenceDuration = sw.Elapsed,
            };
        }
    }

    private (float[] data, int[] shape) RunAudioEncoder(float[] features, int nFrames)
    {
        var nMels = _featureExtractor!.NMels;

        // The encoder expects [batch, time, 160] — 80 mel bins × 2 stacked consecutive frames.
        // features is in [nMels, nFrames] row-major layout from the feature extractor.
        var stackedFrames = nFrames / 2;
        var stackedDim = nMels * 2; // 80 * 2 = 160

        var stacked = new float[stackedFrames * stackedDim];
        for (var t = 0; t < stackedFrames; t++)
        {
            for (var m = 0; m < nMels; m++)
                stacked[t * stackedDim + m] = features[m * nFrames + (2 * t)];
            for (var m = 0; m < nMels; m++)
                stacked[t * stackedDim + nMels + m] = features[m * nFrames + (2 * t + 1)];
        }

        var inputTensor = new DenseTensor<float>(stacked, [1, stackedFrames, stackedDim]);
        var inputName = _encoderSession!.InputNames[0];

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(inputName, inputTensor),
        };

        using var results = _encoderSession.Run(inputs);
        var output = results.First();
        var tensor = output.AsTensor<float>();
        return (tensor.ToArray(), tensor.Dimensions.ToArray());
    }

    /// <summary>
    /// Full auto-regressive decoding with embed_tokens + decoder_model_merged + KV cache.
    /// Applies keyword logit biasing when biasTokenIds is provided.
    /// </summary>
    private int[] RunAutoRegressiveDecoder(
        (float[] data, int[] shape) audioEmbeds,
        Dictionary<int, float>? biasWeights,
        CancellationToken ct)
    {
        var audioSeqLen = audioEmbeds.shape[1];
        var embedDim = audioEmbeds.shape[^1];
        var maxTokens = _options.MaxTokens;

        // Build initial inputs_embeds: audio_embeds from encoder (already projected)
        var currentEmbeds = audioEmbeds.data;
        var currentSeqLen = audioSeqLen;

        var generatedTokens = new List<int>();

        // Initialize empty KV cache
        var kvCache = InitializeKvCache();
        var useCacheBranch = false;
        var totalSeqLen = currentSeqLen;

        // Hard time limit: 30s max for decoder (prevents runaway generation)
        var decoderDeadline = Stopwatch.StartNew();
        const long maxDecoderMs = 30_000;

        for (var step = 0; step < maxTokens; step++)
        {
            ct.ThrowIfCancellationRequested();
            if (decoderDeadline.ElapsedMilliseconds > maxDecoderMs)
            {
                _logger.LogWarning("Decoder hit {MaxMs}ms time limit after {Steps} tokens", maxDecoderMs, step);
                break;
            }

            var embedsTensor = new DenseTensor<float>(
                currentEmbeds, [1, currentSeqLen, embedDim]);

            var decoderInputs = BuildDecoderInputs(embedsTensor, kvCache, useCacheBranch, totalSeqLen);

            using var results = _decoderSession!.Run(decoderInputs);

            // Extract logits
            var logitsResult = results.FirstOrDefault(r =>
                r.Name.Contains("logits", StringComparison.OrdinalIgnoreCase));
            if (logitsResult is null)
                logitsResult = results.First();

            var logits = logitsResult.AsTensor<float>();
            var vocabSize = logits.Dimensions[^1];
            var lastPos = logits.Dimensions[1] - 1;

            // Apply tiered keyword biasing during greedy decode
            var nextToken = GreedyDecodeWithBias(logits, lastPos, vocabSize, biasWeights);

            if (nextToken == _eosTokenId)
                break;

            generatedTokens.Add(nextToken);

            // Update KV cache from decoder outputs
            UpdateKvCache(kvCache, results);

            // For next step: embed the new token and use cache
            currentEmbeds = EmbedToken(nextToken);
            currentSeqLen = 1;
            useCacheBranch = true;
            totalSeqLen++;
        }

        return generatedTokens.ToArray();
    }

    /// <summary>
    /// Simpler decoder path when no embed_tokens model is present.
    /// Uses encoder output as encoder_hidden_states for cross-attention.
    /// </summary>
    private int[] RunSimpleDecoder(
        (float[] data, int[] shape) encoderOutput,
        CancellationToken ct)
    {
        var maxTokens = _options.MaxTokens;
        var generatedTokens = new List<int>();

        // Start with BOS token
        var allTokens = new List<long>();
        if (_tokenizer!.BosTokenId >= 0)
            allTokens.Add(_tokenizer.BosTokenId);

        var encoderTensor = new DenseTensor<float>(encoderOutput.data, encoderOutput.shape);

        for (var step = 0; step < maxTokens; step++)
        {
            ct.ThrowIfCancellationRequested();

            var inputIdsTensor = new DenseTensor<long>(
                allTokens.ToArray(), [1, allTokens.Count]);

            var inputs = new List<NamedOnnxValue>();

            // Add input_ids and encoder_hidden_states using actual model input names
            foreach (var inputName in _decoderSession!.InputNames)
            {
                if (inputName.Contains("input_ids", StringComparison.OrdinalIgnoreCase))
                    inputs.Add(NamedOnnxValue.CreateFromTensor(inputName, inputIdsTensor));
                else if (inputName.Contains("encoder", StringComparison.OrdinalIgnoreCase)
                    || inputName.Contains("hidden", StringComparison.OrdinalIgnoreCase))
                    inputs.Add(NamedOnnxValue.CreateFromTensor(inputName, encoderTensor));
            }

            if (inputs.Count == 0)
            {
                inputs.Add(NamedOnnxValue.CreateFromTensor(
                    _decoderSession.InputNames[0], inputIdsTensor));
                if (_decoderSession.InputNames.Count > 1)
                    inputs.Add(NamedOnnxValue.CreateFromTensor(
                        _decoderSession.InputNames[1], encoderTensor));
            }

            using var results = _decoderSession.Run(inputs);
            var logits = results.First().AsTensor<float>();
            var vocabSize = logits.Dimensions[^1];
            var lastPos = logits.Dimensions[1] - 1;

            var nextToken = ArgMaxAtPosition(logits, lastPos, vocabSize);

            if (nextToken == _eosTokenId)
                break;

            generatedTokens.Add(nextToken);
            allTokens.Add(nextToken);
        }

        return generatedTokens.ToArray();
    }

    private Dictionary<string, (float[] data, int[] shape)> InitializeKvCache()
    {
        var cache = new Dictionary<string, (float[] data, int[] shape)>();

        if (_decoderSession is null) return cache;

        // Create empty KV cache tensors with batch=1, heads=4, seq=0, dim=128
        foreach (var inputName in _decoderSession.InputNames)
        {
            if (!inputName.StartsWith("past_key_values", StringComparison.Ordinal))
                continue;

            // Shape: [batch_size=1, num_kv_heads, past_sequence_length=0, head_dim]
            var shape = new[] { 1, _numKvHeads, 0, _headDim };
            cache[inputName] = ([], shape);
        }

        return cache;
    }

    private List<NamedOnnxValue> BuildDecoderInputs(
        DenseTensor<float> embedsTensor,
        Dictionary<string, (float[] data, int[] shape)> kvCache,
        bool useCacheBranch,
        int totalSeqLen)
    {
        var inputs = new List<NamedOnnxValue>();

        foreach (var inputName in _decoderSession!.InputNames)
        {
            if (inputName.Contains("inputs_embeds", StringComparison.OrdinalIgnoreCase)
                || inputName.Contains("input_embeds", StringComparison.OrdinalIgnoreCase))
            {
                inputs.Add(NamedOnnxValue.CreateFromTensor(inputName, embedsTensor));
            }
            else if (inputName.Contains("input_ids", StringComparison.OrdinalIgnoreCase))
            {
                // Some models take input_ids instead of inputs_embeds
                // Skip if we're providing embeds, or provide dummy
                inputs.Add(NamedOnnxValue.CreateFromTensor(inputName, embedsTensor));
            }
            else if (inputName.Contains("attention_mask", StringComparison.OrdinalIgnoreCase))
            {
                var mask = new long[totalSeqLen];
                Array.Fill(mask, 1L);
                inputs.Add(NamedOnnxValue.CreateFromTensor(inputName,
                    new DenseTensor<long>(mask, [1, totalSeqLen])));
            }
            else if (inputName == "use_cache_branch")
            {
                var boolData = new[] { useCacheBranch };
                inputs.Add(NamedOnnxValue.CreateFromTensor(inputName,
                    new DenseTensor<bool>(boolData, new[] { 1 })));
            }
            else if (kvCache.TryGetValue(inputName, out var kv))
            {
                inputs.Add(NamedOnnxValue.CreateFromTensor(inputName,
                    new DenseTensor<float>(kv.data, kv.shape)));
            }
        }

        return inputs;
    }

    private void UpdateKvCache(
        Dictionary<string, (float[] data, int[] shape)> kvCache,
        IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results)
    {
        foreach (var result in results)
        {
            if (!result.Name.StartsWith("present", StringComparison.Ordinal))
                continue;

            // Map present.N.key → past_key_values.N.key
            var pastName = result.Name.Replace("present", "past_key_values");
            var tensor = result.AsTensor<float>();
            kvCache[pastName] = (tensor.ToArray(), tensor.Dimensions.ToArray());
        }
    }

    private float[] EmbedToken(int tokenId)
    {
        if (_embedSession is null)
            return [];

        var inputIds = new DenseTensor<long>(new[] { (long)tokenId }, new[] { 1, 1 });
        var inputName = _embedSession.InputNames[0];

        using var results = _embedSession.Run(
        [
            NamedOnnxValue.CreateFromTensor(inputName, inputIds),
        ]);

        var tensor = results.First().AsTensor<float>();
        return tensor.ToArray();
    }

    private static int[] CtcGreedyDecode(float[] data, int[] shape)
    {
        var timeSteps = shape[1];
        var vocabSize = shape[^1];
        var tokens = new List<int>();
        var prevToken = -1;

        for (var t = 0; t < timeSteps; t++)
        {
            var offset = t * vocabSize;
            var maxIdx = 0;
            var maxVal = data[offset];
            for (var v = 1; v < vocabSize; v++)
            {
                if (data[offset + v] > maxVal)
                {
                    maxVal = data[offset + v];
                    maxIdx = v;
                }
            }

            if (maxIdx != 0 && maxIdx != prevToken)
                tokens.Add(maxIdx);
            prevToken = maxIdx;
        }

        return tokens.ToArray();
    }

    /// <summary>
    /// Greedy decode from logits with optional tiered keyword biasing.
    /// Each biased token has its own weight — strong for names, gentle for common words.
    /// Never boosts above EOS to prevent infinite generation.
    /// </summary>
    private int GreedyDecodeWithBias(
        Tensor<float> logits, int position, int vocabSize,
        Dictionary<int, float>? biasWeights)
    {
        if (biasWeights is null or { Count: 0 })
            return ArgMaxAtPosition(logits, position, vocabSize);

        // First pass: find the unbiased top logit to use as plausibility threshold
        var topLogit = logits[0, position, 0];
        for (var v = 1; v < vocabSize; v++)
        {
            var val = logits[0, position, v];
            if (val > topLogit) topLogit = val;
        }

        // Second pass: apply bias and find argmax
        // Only boost tokens within 15 logit units of the top — avoids hallucinating
        // tokens that have near-zero probability
        var threshold = topLogit - 15.0f;
        var maxIdx = 0;
        var maxVal = logits[0, position, 0];

        for (var v = 1; v < vocabSize; v++)
        {
            var val = logits[0, position, v];

            if (v != _eosTokenId && v != _padTokenId
                && val > threshold
                && biasWeights.TryGetValue(v, out var boost))
            {
                val += boost;
            }

            if (val > maxVal)
            {
                maxVal = val;
                maxIdx = v;
            }
        }

        return maxIdx;
    }

    private Dictionary<int, float>? BuildKeywordBiasWeights(IReadOnlyList<KeywordBias>? keywords)
    {
        if (keywords is null or { Count: 0 } || _tokenizer is null)
            return null;

        var weighted = keywords
            .Select(static kb => (kb.Keyword, kb.Weight))
            .ToList();

        var biasWeights = _tokenizer.FindKeywordTokenWeights(weighted);

        _logger.LogDebug(
            "Keyword bias: {KeywordCount} keywords \u2192 {TokenCount} biased token IDs",
            keywords.Count, biasWeights.Count);

        return biasWeights.Count > 0 ? biasWeights : null;
    }

    private void TryLoadModel(string modelPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath) || !Directory.Exists(modelPath))
        {
            _logger.LogInformation("Granite model path not configured or missing: {Path}",
                modelPath ?? "(not configured)");
            return;
        }

        lock (_lock)
        {
            try
            {
                // Find ONNX files (prefer quantized for CPU)
                var onnxDir = Path.Combine(modelPath, "onnx");
                var searchDir = Directory.Exists(onnxDir) ? onnxDir : modelPath;

                var encoderPath = FindModel(searchDir, "audio_encoder_quantized", "audio_encoder");
                var embedPath = FindModel(searchDir, "embed_tokens_quantized", "embed_tokens");
                var decoderPath = FindModel(searchDir,
                    "decoder_model_merged_quantized", "decoder_model_merged",
                    "decoder_model");

                // Also try generic encoder/decoder naming
                encoderPath ??= FindModel(searchDir, "encoder_model");
                decoderPath ??= FindModel(searchDir, "decoder_model");

                // Single model fallback (CTC-only)
                encoderPath ??= FindModel(searchDir, "model");

                var tokenizerPath = FindFile(modelPath, "tokenizer.json");

                if (encoderPath is null)
                {
                    _logger.LogWarning("No ONNX encoder found in {Path}", modelPath);
                    return;
                }

                if (tokenizerPath is null)
                {
                    _logger.LogWarning("No tokenizer.json found in {Path}", modelPath);
                    return;
                }

                ReadModelConfig(Path.Combine(modelPath, "config.json"));

                var sessionOptions = CreateSessionOptions();

                var oldEncoder = _encoderSession;
                var oldEmbed = _embedSession;
                var oldDecoder = _decoderSession;

                _encoderSession = new InferenceSession(encoderPath, sessionOptions);
                _embedSession = embedPath is not null
                    ? new InferenceSession(embedPath, sessionOptions) : null;
                _decoderSession = decoderPath is not null
                    ? new InferenceSession(decoderPath, sessionOptions) : null;
                _tokenizer = new GraniteTokenizer(tokenizerPath);

                // Use EOS from config if tokenizer didn't detect it
                if (_tokenizer.EosTokenId >= 0)
                    _eosTokenId = _tokenizer.EosTokenId;

                var preprocessorPath = Path.Combine(modelPath, "preprocessor_config.json");
                _featureExtractor = GraniteFeatureExtractor.FromPreprocessorConfig(preprocessorPath);

                oldEncoder?.Dispose();
                oldEmbed?.Dispose();
                oldDecoder?.Dispose();

                IsReady = true;
                _logger.LogInformation(
                    "Granite engine loaded from {Path} (encoder: {Encoder}, embed: {HasEmbed}, decoder: {HasDecoder})",
                    modelPath,
                    Path.GetFileName(encoderPath),
                    _embedSession is not null,
                    _decoderSession is not null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load Granite model from {Path}", modelPath);
                IsReady = false;
            }
        }
    }

    private void ReadModelConfig(string configPath)
    {
        if (!File.Exists(configPath)) return;

        try
        {
            var json = File.ReadAllText(configPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("text_config", out var textCfg))
            {
                _numLayers = textCfg.TryGetProperty("num_hidden_layers", out var nl)
                    ? nl.GetInt32() : 40;
                _numKvHeads = textCfg.TryGetProperty("num_key_value_heads", out var nkv)
                    ? nkv.GetInt32() : 4;
                _hiddenSize = textCfg.TryGetProperty("hidden_size", out var hs)
                    ? hs.GetInt32() : 2048;

                var numHeads = textCfg.TryGetProperty("num_attention_heads", out var nh)
                    ? nh.GetInt32() : 16;
                _headDim = _hiddenSize / numHeads;

                _eosTokenId = textCfg.TryGetProperty("eos_token_id", out var eos)
                    ? eos.GetInt32() : 100257;
                _padTokenId = textCfg.TryGetProperty("pad_token_id", out var pad)
                    ? pad.GetInt32() : 100256;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse config.json, using defaults");
            _numLayers = 40;
            _numKvHeads = 4;
            _hiddenSize = 2048;
            _headDim = 128;
            _eosTokenId = 100257;
            _padTokenId = 100256;
        }
    }

    private SessionOptions CreateSessionOptions()
    {
        var options = new SessionOptions
        {
            InterOpNumThreads = 1,
            IntraOpNumThreads = _options.NumThreads,
            LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR,
        };
        _providerDetector.ConfigureSessionOptions(options, _logger);
        return options;
    }

    private void OnActiveModelChanged(ActiveModelChangedEvent evt)
    {
        if (evt.EngineType != EngineType.OfflineStt) return;
        _logger.LogInformation("Reloading Granite engine with model {ModelId}", evt.ModelId);
        TryLoadModel(evt.ModelPath);
    }

    /// <summary>
    /// Finds an ONNX model file, checking for external data files.
    /// Tries each candidate name in order (e.g., quantized first, then full).
    /// </summary>
    private static string? FindModel(string directory, params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            var onnxPath = Path.Combine(directory, candidate + ".onnx");
            if (File.Exists(onnxPath))
                return onnxPath;
        }
        return null;
    }

    private static string? FindFile(string directory, string fileName)
    {
        var path = Path.Combine(directory, fileName);
        return File.Exists(path) ? path : null;
    }

    private static int ArgMaxAtPosition(Tensor<float> logits, int position, int vocabSize)
    {
        var maxIdx = 0;
        var maxVal = logits[0, position, 0];
        for (var v = 1; v < vocabSize; v++)
        {
            var val = logits[0, position, v];
            if (val > maxVal)
            {
                maxVal = val;
                maxIdx = v;
            }
        }
        return maxIdx;
    }
}
