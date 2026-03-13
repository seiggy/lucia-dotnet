using System.Collections.Concurrent;
using lucia.Wyoming.Audio;

namespace lucia.Tests.Wyoming;

public sealed class AudioBufferTests
{
    [Fact]
    public void WriteAndRead_ReturnsSamplesInFifoOrder()
    {
        var buffer = new AudioBuffer(capacity: 8);

        buffer.Write(new float[] { 0.1f, 0.2f, 0.3f });

        var firstRead = buffer.Read(2);
        var secondRead = buffer.Read(2);

        Assert.Equal(new float[] { 0.1f, 0.2f }, firstRead);
        Assert.Equal(new float[] { 0.3f }, secondRead);
    }

    [Fact]
    public void Write_WhenCapacityExceeded_OverwritesOldestSamples()
    {
        var buffer = new AudioBuffer(capacity: 3);

        buffer.Write(new float[] { 1f, 2f, 3f, 4f, 5f });

        var samples = buffer.Read(3);

        Assert.Equal(3, buffer.Available + samples.Length);
        Assert.Equal(new float[] { 3f, 4f, 5f }, samples);
        Assert.Equal(0, buffer.Available);
    }

    [Fact]
    public async Task WriteAndRead_AreThreadSafe()
    {
        const int sampleCount = 1_024;

        var buffer = new AudioBuffer(capacity: sampleCount);
        var expected = Enumerable.Range(0, sampleCount).Select(static index => (float)index).ToArray();
        var actual = new ConcurrentQueue<float>();
        var producerCompleted = 0;

        var producer = Task.Run(async () =>
        {
            foreach (var sample in expected)
            {
                buffer.Write(new[] { sample });
                await Task.Yield();
            }

            System.Threading.Volatile.Write(ref producerCompleted, 1);
        });

        var consumer = Task.Run(async () =>
        {
            while (System.Threading.Volatile.Read(ref producerCompleted) == 0 || buffer.Available > 0)
            {
                var samples = buffer.Read(16);
                if (samples.Length == 0)
                {
                    await Task.Yield();
                    continue;
                }

                foreach (var sample in samples)
                {
                    actual.Enqueue(sample);
                }
            }
        });

        await Task.WhenAll(producer, consumer);

        Assert.Equal(expected.Length, actual.Count);
        Assert.Equal(expected, actual.ToArray());
    }

    [Fact]
    public void Clear_RemovesAllBufferedSamples()
    {
        var buffer = new AudioBuffer(capacity: 4);

        buffer.Write(new float[] { 0.5f, 1.5f, 2.5f });
        buffer.Clear();

        Assert.Equal(0, buffer.Available);
        Assert.Empty(buffer.Read(4));
    }

    [Fact]
    public void Available_ReturnsCurrentBufferedCount()
    {
        var buffer = new AudioBuffer(capacity: 5);

        buffer.Write(new float[] { 10f, 20f, 30f });
        _ = buffer.Read(2);

        Assert.Equal(1, buffer.Available);
    }
}
