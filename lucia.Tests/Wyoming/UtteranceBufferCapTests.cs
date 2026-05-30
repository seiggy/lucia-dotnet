using System.Net;
using System.Net.Sockets;
using lucia.Wyoming.Audio;
using lucia.Wyoming.Stt;
using lucia.Wyoming.Wyoming;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace lucia.Tests.Wyoming;

/// <summary>
/// Verifies that <see cref="WyomingOptions.MaxUtteranceSamples"/> caps cumulative
/// utterance audio to prevent memory exhaustion (DoS via unbounded audio streaming).
/// </summary>
public sealed class UtteranceBufferCapTests
{
    /// <summary>
    /// When a client streams more audio than the configured cap, the session must
    /// stop accepting further samples, log a warning, and still finalize the utterance
    /// gracefully — returning a transcript rather than crashing.
    /// </summary>
    [Fact]
    public async Task RunAsync_AudioExceedsBufferCap_DropsExcessAndFinalizesGracefully()
    {
        // 8-sample cap; we'll send 3 chunks × 6 samples = 18 samples total (> cap).
        const int capSamples = 8;
        const int samplesPerChunk = 6;
        const int chunkCount = 3;

        var trackingStt = new CapEnforcementSttSession(new SttResult { Text = "hello", Confidence = 0.9f });
        var sttEngine = new TestSttEngine(trackingStt);

        var options = new WyomingOptions
        {
            ReadTimeoutSeconds = 5,
            MaxUtteranceSamples = capSamples,
        };

        var (listener, client, serverClient, services, session, writer, parser) =
            await CreateConnectedSessionAsync(options, sttEngine: sttEngine);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var runTask = session.RunAsync(cts.Token);

        try
        {
            // Direct STT flow: AudioStart → N×AudioChunk → AudioStop
            await writer.WriteEventAsync(
                new AudioStartEvent { Rate = 16_000, Width = 2, Channels = 1 }, cts.Token);

            var chunkSamples = Enumerable.Repeat(0.1f, samplesPerChunk).ToArray();
            var payload = PcmConverter.Float32ToInt16(chunkSamples);

            for (var i = 0; i < chunkCount; i++)
            {
                await writer.WriteEventAsync(
                    new AudioChunkEvent
                    {
                        Rate = 16_000,
                        Width = 2,
                        Channels = 1,
                        Payload = payload,
                    },
                    cts.Token);
            }

            await writer.WriteEventAsync(new AudioStopEvent(), cts.Token);

            // Session must still return a transcript (graceful finalization, not a crash).
            var transcript = Assert.IsType<TranscriptEvent>(await parser.ReadEventAsync(cts.Token));
            Assert.Contains("hello", transcript.Text, StringComparison.Ordinal);
        }
        finally
        {
            client.Close();
            await runTask;
            serverClient.Dispose();
            client.Dispose();
            listener.Stop();
            await services.DisposeAsync();
        }

        // STT must not have received more samples than the cap allows.
        Assert.True(
            trackingStt.TotalSamplesReceived <= capSamples,
            $"STT received {trackingStt.TotalSamplesReceived} samples but cap was {capSamples}. " +
            "Utterance buffer cap was not enforced.");
    }

    /// <summary>
    /// When the cap is set to 0 (disabled), the session should accumulate all samples
    /// without truncation and complete normally.
    /// </summary>
    [Fact]
    public async Task RunAsync_BufferCapDisabled_AcceptsAllSamples()
    {
        const int samplesPerChunk = 10;
        const int chunkCount = 5;
        const int expectedTotal = samplesPerChunk * chunkCount;

        var trackingStt = new CapEnforcementSttSession(new SttResult { Text = "test", Confidence = 0.8f });
        var sttEngine = new TestSttEngine(trackingStt);

        var options = new WyomingOptions
        {
            ReadTimeoutSeconds = 5,
            MaxUtteranceSamples = 0, // disabled
        };

        var (listener, client, serverClient, services, session, writer, parser) =
            await CreateConnectedSessionAsync(options, sttEngine: sttEngine);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var runTask = session.RunAsync(cts.Token);

        try
        {
            await writer.WriteEventAsync(
                new AudioStartEvent { Rate = 16_000, Width = 2, Channels = 1 }, cts.Token);

            var chunkSamples = Enumerable.Repeat(0.1f, samplesPerChunk).ToArray();
            var payload = PcmConverter.Float32ToInt16(chunkSamples);

            for (var i = 0; i < chunkCount; i++)
            {
                await writer.WriteEventAsync(
                    new AudioChunkEvent
                    {
                        Rate = 16_000,
                        Width = 2,
                        Channels = 1,
                        Payload = payload,
                    },
                    cts.Token);
            }

            await writer.WriteEventAsync(new AudioStopEvent(), cts.Token);

            var transcript = Assert.IsType<TranscriptEvent>(await parser.ReadEventAsync(cts.Token));
            Assert.Contains("test", transcript.Text, StringComparison.Ordinal);
        }
        finally
        {
            client.Close();
            await runTask;
            serverClient.Dispose();
            client.Dispose();
            listener.Stop();
            await services.DisposeAsync();
        }

        // When cap is disabled all samples must reach STT.
        Assert.Equal(expectedTotal, trackingStt.TotalSamplesReceived);
    }

    // --- helpers ----------------------------------------------------------------

    private static async Task<(
        TcpListener Listener,
        TcpClient Client,
        TcpClient ServerClient,
        ServiceProvider Services,
        WyomingSession Session,
        WyomingEventWriter Writer,
        WyomingEventParser Parser)> CreateConnectedSessionAsync(
        WyomingOptions options,
        ISttEngine? sttEngine = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOptions<WyomingOptions>>(Options.Create(options));

        if (sttEngine is not null)
        {
            services.AddSingleton<ISttEngine>(sttEngine);
        }

        var serviceProvider = services.BuildServiceProvider();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        var acceptTask = listener.AcceptTcpClientAsync();
        var client = new TcpClient();
        await client.ConnectAsync(endpoint.Address, endpoint.Port);
        var serverClient = await acceptTask;

        var session = new WyomingSession(
            serverClient,
            serviceProvider,
            NullLogger<WyomingSession>.Instance,
            options);

        var stream = client.GetStream();

        return (
            listener,
            client,
            serverClient,
            serviceProvider,
            session,
            new WyomingEventWriter(stream),
            new WyomingEventParser(stream, options));
    }

    /// <summary>
    /// STT session that tracks the total number of float samples handed to
    /// <see cref="AcceptAudioChunk"/>, enabling cap-enforcement assertions.
    /// </summary>
    private sealed class CapEnforcementSttSession(SttResult finalResult) : ISttSession
    {
        public int TotalSamplesReceived { get; private set; }

        public bool IsEndOfUtterance => false;

        public void AcceptAudioChunk(ReadOnlySpan<float> samples, int sampleRate)
        {
            TotalSamplesReceived += samples.Length;
        }

        public SttResult GetPartialResult() => new();

        public Task<SttResult> GetFinalResultAsync() => Task.FromResult(finalResult);

        public void Dispose() { }
    }
}
