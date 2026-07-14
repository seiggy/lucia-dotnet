using lucia.Wyoming.Audio;
using Microsoft.ML.OnnxRuntime;
using Xunit.Abstractions;

namespace lucia.Tests.Wyoming;

public sealed class GtcrnStreamingSessionTests(ITestOutputHelper output)
{
    private const int HopSize = 256;
    private const string TestModelPath = "lucia.Tests/TestData/gtcrn-streaming-test.onnx";

    [Fact]
    public void Process_MatchesGoldenOutputAcrossChunkBoundariesAndRepeatedCalls()
    {
        var input = CreateSamples(1536);

        using var onnxSession = CreateOnnxSession();
        using var singleChunkSession = new GtcrnStreamingSession(onnxSession);
        using var variedChunkSession = new GtcrnStreamingSession(onnxSession);

        var expected = singleChunkSession.Process(input);
        var actual = ProcessInChunks(variedChunkSession, input, [17, 239, 1, 512, 300, 467]);

        Assert.Equal(1280, expected.Length);
        Assert.Equal(expected.Length, actual.Length);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], actual[i], 6);
        }

        var goldenIndexes = new[] { 0, 1, 127, 255, 256, 257, 511, 512, 767, 768, 1023, 1279 };
        var goldenValues = new[]
        {
            0f,
            3.1611333E-07f,
            -0.0972824f,
            0.004955358f,
            -0.0014409225f,
            -0.007822973f,
            0.026258962f,
            0.03462642f,
            0.0016595022f,
            -0.00470737f,
            0.029593695f,
            -0.0324495f,
        };
        for (var i = 0; i < goldenIndexes.Length; i++)
        {
            Assert.InRange(MathF.Abs(expected[goldenIndexes[i]] - goldenValues[i]), 0, 1e-6f);
        }

        Assert.Empty(variedChunkSession.Process([]));
        Assert.Empty(variedChunkSession.Process([0.1f]));
        variedChunkSession.Dispose();
        Assert.Throws<ObjectDisposedException>(() => variedChunkSession.Process([0.1f]));
    }

    [Fact]
    public void Process_WarmHopAllocationsRemainBelowBudget()
    {
        var hop = CreateSamples(HopSize);

        using var onnxSession = CreateOnnxSession();
        using var session = new GtcrnStreamingSession(onnxSession);
        _ = session.Process(CreateSamples(HopSize * 2));
        for (var i = 0; i < 16; i++)
        {
            _ = session.Process(hop);
        }

        const int Iterations = 64;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Iterations; i++)
        {
            _ = session.Process(hop);
        }
        var bytesPerHop = (GC.GetAllocatedBytesForCurrentThread() - before) / Iterations;

        output.WriteLine($"Warm allocations: {bytesPerHop:N0} bytes per {HopSize}-sample hop");
        Assert.True(bytesPerHop < 10_000, $"Allocated {bytesPerHop:N0} bytes per hop.");
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
