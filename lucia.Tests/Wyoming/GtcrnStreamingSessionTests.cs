using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using lucia.Wyoming.Audio;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Xunit.Abstractions;

namespace lucia.Tests.Wyoming;

public sealed class GtcrnStreamingSessionTests(ITestOutputHelper output)
{
    private const int HopSize = 256;
    private const string TestModelPath = "lucia.Tests/TestData/gtcrn-streaming-test.onnx";

    [Fact]
    public void Process_MatchesIndependentReferenceAcrossChunkBoundaries()
    {
        var input = CreateSamples(1536);
        var chunkSizes = new[] { 17, 239, 1, 512, 300, 467 };

        using var onnxSession = CreateOnnxSession();
        using var optimizedSession = new GtcrnStreamingSession(onnxSession);

        var (expected, expectedConv, expectedTra, expectedInter) =
            ProcessWithReferenceImplementation(onnxSession, input, chunkSizes);
        var actual = ProcessInChunks(optimizedSession, input, chunkSizes);

        Assert.Equal(1280, expected.Length);
        Assert.Equal(expected.Length, actual.Length);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], actual[i], 6);
        }

        Assert.Equal(expectedConv, GetPrivateArray(optimizedSession, "_convCache"));
        Assert.Equal(expectedTra, GetPrivateArray(optimizedSession, "_traCache"));
        Assert.Equal(expectedInter, GetPrivateArray(optimizedSession, "_interCache"));
    }

    [Fact]
    public void Process_ReturnedArrayRemainsOwnedByCaller()
    {
        using var onnxSession = CreateOnnxSession();
        using var session = new GtcrnStreamingSession(onnxSession);
        var first = session.Process(CreateSamples(HopSize * 2));
        var snapshot = first.ToArray();

        for (var i = 0; i < 8; i++)
        {
            var later = session.Process(CreateSamples(HopSize));
            Assert.NotSame(first, later);
        }

        Assert.Equal(snapshot, first);
    }

    [Fact]
    public async Task Dispose_WaitsForInFlightRunAndIsIdempotent()
    {
        using var onnxSession = CreateOnnxSession();
        using var runEntered = new ManualResetEventSlim();
        using var releaseRun = new ManualResetEventSlim();
        using var disposeEntered = new ManualResetEventSlim();
        using var session = new GtcrnStreamingSession(
            onnxSession,
            () =>
            {
                runEntered.Set();
                releaseRun.Wait();
            },
            disposeEntered.Set);

        var processTask = Task.Run(() => session.Process(CreateSamples(HopSize * 2)));
        Assert.True(runEntered.Wait(TimeSpan.FromSeconds(10)), "Process did not reach OrtRun.");

        var disposeTask = Task.Run(session.Dispose);
        Assert.True(disposeEntered.Wait(TimeSpan.FromSeconds(10)), "Dispose did not start.");
        Assert.False(disposeTask.IsCompleted, "Dispose completed while OrtRun buffers were in use.");

        releaseRun.Set();
        var output = await processTask;
        await disposeTask;

        Assert.Equal(HopSize, output.Length);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(session.Dispose)));
        Assert.Throws<ObjectDisposedException>(() => session.Process([0.1f]));
    }

    [Fact]
    public void Process_WarmHopAllocationsRemainMeaningfullyBelowReference()
    {
        var hop = CreateSamples(HopSize);

        using var onnxSession = CreateOnnxSession();
        const int Iterations = 64;
        var referenceInput = CreateSamples(HopSize * (Iterations + 1));
        var referenceBefore = GC.GetAllocatedBytesForCurrentThread();
        _ = ProcessWithReferenceImplementation(onnxSession, referenceInput, [referenceInput.Length]);
        var referenceBytesPerHop =
            (GC.GetAllocatedBytesForCurrentThread() - referenceBefore) / Iterations;

        using var session = new GtcrnStreamingSession(onnxSession);
        _ = session.Process(CreateSamples(HopSize * 2));
        for (var i = 0; i < 16; i++)
        {
            _ = session.Process(hop);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Iterations; i++)
        {
            _ = session.Process(hop);
        }
        var bytesPerHop = (GC.GetAllocatedBytesForCurrentThread() - before) / Iterations;

        output.WriteLine(
            $"Warm allocations: reference {referenceBytesPerHop:N0}, optimized {bytesPerHop:N0} " +
            $"bytes per {HopSize}-sample hop");
        Assert.True(bytesPerHop < 10_000, $"Allocated {bytesPerHop:N0} bytes per hop.");
        Assert.True(
            bytesPerHop * 20 < referenceBytesPerHop,
            $"Optimized {bytesPerHop:N0} vs reference {referenceBytesPerHop:N0} bytes per hop.");
    }

    [Fact]
    public void Process_UncontendedLockOverheadIsNegligible()
    {
        using var onnxSession = CreateOnnxSession();
        using var session = new GtcrnStreamingSession(onnxSession);
        var hop = CreateSamples(HopSize);
        _ = session.Process(CreateSamples(HopSize * 2));

        const int ProcessIterations = 128;
        var processStopwatch = Stopwatch.StartNew();
        for (var i = 0; i < ProcessIterations; i++)
        {
            _ = session.Process(hop);
        }
        processStopwatch.Stop();

        const int LockIterations = 1_000_000;
        var sync = new object();
        var lockStopwatch = Stopwatch.StartNew();
        for (var i = 0; i < LockIterations; i++)
        {
            lock (sync)
            {
            }
        }
        lockStopwatch.Stop();

        var processNanoseconds = processStopwatch.Elapsed.TotalNanoseconds / ProcessIterations;
        var lockNanoseconds = lockStopwatch.Elapsed.TotalNanoseconds / LockIterations;
        var overheadPercent = lockNanoseconds / processNanoseconds * 100;
        output.WriteLine(
            $"Uncontended lock: {lockNanoseconds:N1} ns; Process hop: {processNanoseconds:N0} ns; " +
            $"overhead: {overheadPercent:N3}%");
        Assert.True(overheadPercent < 1, $"Lock overhead was {overheadPercent:N3}%.");
    }

    private static InferenceSession CreateOnnxSession()
    {
        var modelPath = Path.Combine(FindRepoRoot(), TestModelPath);
        using var options = new SessionOptions
        {
            InterOpNumThreads = 1,
            IntraOpNumThreads = 1,
        };
        return new InferenceSession(modelPath, options);
    }

    private static float[] CreateSamples(int count)
    {
        var samples = new float[count];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = (MathF.Sin(i * 0.037f) * 0.2f) + ((i % 31) * 0.001f);
        }
        return samples;
    }

    private static (
        float[] Output,
        float[] ConvCache,
        float[] TraCache,
        float[] InterCache) ProcessWithReferenceImplementation(
            InferenceSession onnxSession,
            float[] input,
            IReadOnlyList<int> chunkSizes)
    {
        const int WindowSize = 512;
        const int FreqBins = 257;
        var window = new float[WindowSize];
        for (var i = 0; i < window.Length; i++)
        {
            window[i] = MathF.Sqrt(0.5f * (1 - MathF.Cos(2 * MathF.PI * i / WindowSize)));
        }

        var inputBuffer = new float[WindowSize];
        var inputBufferPosition = 0;
        var overlap = new float[WindowSize];
        var convCache = new float[2 * 1 * 16 * 16 * 33];
        var traCache = new float[2 * 3 * 1 * 1 * 16];
        var interCache = new float[2 * 1 * 33 * 16];
        var output = new List<float>();
        var inputOffset = 0;

        foreach (var chunkSize in chunkSizes)
        {
            foreach (var sample in input.AsSpan(inputOffset, chunkSize))
            {
                inputBuffer[inputBufferPosition++] = sample;
                if (inputBufferPosition < WindowSize)
                {
                    continue;
                }

                var windowed = new float[WindowSize];
                var fft = new Complex[WindowSize];
                for (var i = 0; i < WindowSize; i++)
                {
                    windowed[i] = inputBuffer[i] * window[i];
                    fft[i] = new Complex(windowed[i], 0);
                }
                ReferenceFft(fft, false);

                var mix = new DenseTensor<float>(new[] { 1, FreqBins, 1, 2 });
                for (var i = 0; i < FreqBins; i++)
                {
                    mix[0, i, 0, 0] = (float)fft[i].Real;
                    mix[0, i, 0, 1] = (float)fft[i].Imaginary;
                }

                var inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor("mix", mix),
                    NamedOnnxValue.CreateFromTensor(
                        "conv_cache",
                        new DenseTensor<float>(convCache, [2, 1, 16, 16, 33])),
                    NamedOnnxValue.CreateFromTensor(
                        "tra_cache",
                        new DenseTensor<float>(traCache, [2, 3, 1, 1, 16])),
                    NamedOnnxValue.CreateFromTensor(
                        "inter_cache",
                        new DenseTensor<float>(interCache, [2, 1, 33, 16])),
                };
                using var results = onnxSession.Run(inputs);
                var enhanced = results.First(result => result.Name == "enh").AsTensor<float>();
                convCache = results.First(result => result.Name == "conv_cache_out").AsTensor<float>().ToArray();
                traCache = results.First(result => result.Name == "tra_cache_out").AsTensor<float>().ToArray();
                interCache = results.First(result => result.Name == "inter_cache_out").AsTensor<float>().ToArray();

                var inverse = new Complex[WindowSize];
                for (var i = 0; i < FreqBins; i++)
                {
                    inverse[i] = new Complex(enhanced[0, i, 0, 0], enhanced[0, i, 0, 1]);
                }
                for (var i = 1; i < FreqBins - 1; i++)
                {
                    inverse[WindowSize - i] = Complex.Conjugate(inverse[i]);
                }
                ReferenceFft(inverse, true);

                for (var i = 0; i < WindowSize; i++)
                {
                    overlap[i] += (float)inverse[i].Real * window[i];
                }
                output.AddRange(overlap.AsSpan(0, HopSize).ToArray());
                Array.Copy(overlap, HopSize, overlap, 0, WindowSize - HopSize);
                Array.Clear(overlap, WindowSize - HopSize, HopSize);
                Array.Copy(inputBuffer, HopSize, inputBuffer, 0, WindowSize - HopSize);
                inputBufferPosition = WindowSize - HopSize;
            }
            inputOffset += chunkSize;
        }

        Assert.Equal(input.Length, inputOffset);
        return (output.ToArray(), convCache, traCache, interCache);
    }

    private static void ReferenceFft(Complex[] values, bool inverse)
    {
        for (int i = 1, j = 0; i < values.Length; i++)
        {
            var bit = values.Length >> 1;
            for (; (j & bit) != 0; bit >>= 1)
            {
                j ^= bit;
            }
            j ^= bit;
            if (i < j)
            {
                (values[i], values[j]) = (values[j], values[i]);
            }
        }

        for (var length = 2; length <= values.Length; length <<= 1)
        {
            var angle = 2 * Math.PI / length * (inverse ? -1 : 1);
            var root = new Complex(Math.Cos(angle), Math.Sin(angle));
            for (var i = 0; i < values.Length; i += length)
            {
                var factor = Complex.One;
                for (var j = 0; j < length / 2; j++)
                {
                    var even = values[i + j];
                    var odd = values[i + j + length / 2] * factor;
                    values[i + j] = even + odd;
                    values[i + j + length / 2] = even - odd;
                    factor *= root;
                }
            }
        }

        if (inverse)
        {
            for (var i = 0; i < values.Length; i++)
            {
                values[i] /= values.Length;
            }
        }
    }

    private static float[] GetPrivateArray(GtcrnStreamingSession session, string fieldName)
    {
        var field = typeof(GtcrnStreamingSession).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        return Assert.IsType<float[]>(field?.GetValue(session));
    }

    private static float[] ProcessInChunks(
        GtcrnStreamingSession session,
        float[] input,
        IReadOnlyList<int> chunkSizes)
    {
        var output = new List<float>();
        var offset = 0;
        foreach (var chunkSize in chunkSizes)
        {
            var chunk = input.AsSpan(offset, chunkSize).ToArray();
            output.AddRange(session.Process(chunk));
            offset += chunkSize;
        }

        Assert.Equal(input.Length, offset);
        return output.ToArray();
    }

    private static string FindRepoRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(directory, "lucia-dotnet.slnx")))
        {
            directory = Directory.GetParent(directory)?.FullName
                ?? throw new DirectoryNotFoundException("Repository root not found.");
        }

        return directory;
    }
}
