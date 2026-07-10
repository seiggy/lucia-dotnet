using System.Net;
using System.Net.Sockets;
using lucia.Wyoming.CommandRouting;
using lucia.Wyoming.Audio;
using lucia.Wyoming.Diarization;
using lucia.Wyoming.Models;
using lucia.Wyoming.Stt;
using lucia.Wyoming.Vad;
using lucia.Wyoming.WakeWord;
using lucia.Wyoming.Wyoming;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Xunit.Abstractions;

namespace lucia.Tests.Wyoming;

public sealed class WyomingSessionIntegrationTests(ITestOutputHelper output)
{
    [Fact]
    public async Task RunAsync_DescribeEvent_ReturnsInfoEvent()
    {
        var options = CreateOptions();
        var (listener, client, serverClient, services, session, writer, parser) = await CreateConnectedSessionAsync(options);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var runTask = session.RunAsync(cts.Token);

        try
        {
            await writer.WriteEventAsync(new DescribeEvent(), cts.Token);

            var response = await parser.ReadEventAsync(cts.Token);

            var info = Assert.IsType<InfoEvent>(response);
            Assert.NotNull(info.Asr);
            Assert.NotNull(info.Tts);
            Assert.NotNull(info.Wake);
            Assert.NotNull(info.Version);
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

        Assert.Equal(WyomingSessionState.Disconnected, session.State);
    }

    [Fact]
    public async Task RunAsync_DetectAudioAndTranscribe_ReturnsDetectionAndTranscript()
    {
        var options = CreateOptions();
        var detectionTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(123456789L);
        var wakeSession = new TestWakeWordSession(
            new WakeWordResult
            {
                Keyword = "hey_lucia",
                Confidence = 0.93f,
                Timestamp = detectionTimestamp,
            });
        var wakeDetector = new TestWakeWordDetector(wakeSession);
        var sttSession = new TestSttSession(
            new SttResult
            {
                Text = "turn on the lights",
                Confidence = 0.82f,
            });
        var sttEngine = new TestSttEngine(sttSession);
        var vadSession = new TestVadSession(
            new VadSegment
            {
                Samples = [0.25f, -0.25f, 0.25f, -0.25f],
                StartTime = TimeSpan.Zero,
                EndTime = TimeSpan.FromMilliseconds(250),
                SampleRate = 16_000,
            });
        var vadEngine = new TestVadEngine(vadSession);

        var (listener, client, serverClient, services, session, writer, parser) = await CreateConnectedSessionAsync(
            options,
            wakeDetector,
            sttEngine,
            vadEngine);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var runTask = session.RunAsync(cts.Token);

        try
        {
            await writer.WriteEventAsync(new DetectEvent { Names = ["hey_lucia"] }, cts.Token);
            await writer.WriteEventAsync(
                new AudioStartEvent
                {
                    Rate = 16_000,
                    Width = 2,
                    Channels = 1,
                },
                cts.Token);
            await writer.WriteEventAsync(
                new AudioChunkEvent
                {
                    Rate = 16_000,
                    Width = 2,
                    Channels = 1,
                    Payload = PcmConverter.Float32ToInt16([0.25f, -0.25f, 0.25f, -0.25f]),
                },
                cts.Token);

            var firstResponse = await parser.ReadEventAsync(cts.Token);
            if (firstResponse is ErrorEvent error)
            {
                throw new Xunit.Sdk.XunitException($"Received error event {error.Code}: {error.Text}");
            }

            var detection = Assert.IsType<DetectionEvent>(firstResponse);
            Assert.Equal("hey_lucia", detection.Name);
            Assert.Equal(detectionTimestamp.ToUnixTimeMilliseconds(), detection.Timestamp);

            await writer.WriteEventAsync(new AudioStopEvent(), cts.Token);
            await writer.WriteEventAsync(new TranscribeEvent { Name = "default", Language = "en" }, cts.Token);

            var transcript = Assert.IsType<TranscriptEvent>(await parser.ReadEventAsync(cts.Token));
            Assert.Equal("<Unknown1 />turn on the lights", transcript.Text);
            Assert.True(transcript.Confidence > 0, "Confidence should be positive");
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

        Assert.Equal(1, wakeDetector.CreateSessionCount);
        Assert.Equal(1, sttEngine.CreateSessionCount);
        Assert.Equal(1, vadEngine.CreateSessionCount);
        Assert.Equal(1, wakeSession.AcceptAudioChunkCount);
        Assert.Equal(1, sttSession.AcceptAudioChunkCount);
        Assert.Equal(1, vadSession.AcceptAudioChunkCount);
        Assert.Equal(1, vadSession.FlushCallCount);
    }

    [Fact]
    public async Task RunAsync_TranscribeWithKnownSpeaker_UpdatesProfileAndRoutesTranscript()
    {
        var options = CreateOptions();
        var detectionTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(123456789L);
        var wakeSession = new TestWakeWordSession(
            new WakeWordResult
            {
                Keyword = "hey_lucia",
                Confidence = 0.93f,
                Timestamp = detectionTimestamp,
            });
        var wakeDetector = new TestWakeWordDetector(wakeSession);
        var sttSession = new TestSttSession(
            new SttResult
            {
                Text = "turn on the office lights",
                Confidence = 0.88f,
            });
        var sttEngine = new TestSttEngine(sttSession);
        var vadSegment = new VadSegment
        {
            Samples = [0.1f, 0.2f, 0.3f, 0.4f],
            StartTime = TimeSpan.Zero,
            EndTime = TimeSpan.FromMilliseconds(250),
            SampleRate = 16_000,
        };
        var vadSession = new TestVadSession(vadSegment);
        var vadEngine = new TestVadEngine(vadSession);
        var embedding = Enumerable.Range(0, 128)
            .Select(static i => (float)i / 128)
            .ToArray();
        var speaker = new SpeakerIdentification
        {
            ProfileId = "alice",
            Name = "Alice",
            Similarity = 0.95f,
            IsAuthorized = true,
        };
        var diarizationEngine = new TestDiarizationEngine(speaker, embedding);
        var profileStore = new InMemorySpeakerProfileStore();
        var profile = new SpeakerProfile
        {
            Id = "alice",
            Name = "Alice",
            AverageEmbedding = embedding,
            Embeddings = [embedding],
        };
        await profileStore.CreateAsync(profile, CancellationToken.None);

        var router = new TestCommandRouter(CommandRouteResult.NoMatch(TimeSpan.Zero));
        var voiceOptions = Options.Create(new VoiceProfileOptions());
        var adaptiveUpdater = new AdaptiveProfileUpdater(
            profileStore,
            voiceOptions,
            NullLogger<AdaptiveProfileUpdater>.Instance);

        var (listener, client, serverClient, services, session, writer, parser) = await CreateConnectedSessionAsync(
            options,
            wakeDetector,
            sttEngine,
            vadEngine,
            configureServices: serviceCollection =>
            {
                serviceCollection.AddSingleton<IDiarizationEngine>(diarizationEngine);
                serviceCollection.AddSingleton<ISpeakerProfileStore>(profileStore);
                serviceCollection.AddSingleton(adaptiveUpdater);
                serviceCollection.AddSingleton<ICommandRouter>(router);
            });
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var runTask = session.RunAsync(cts.Token);

        try
        {
            await WriteWakeAndSpeechAsync(writer, cts.Token);

            _ = Assert.IsType<DetectionEvent>(await parser.ReadEventAsync(cts.Token));

            await writer.WriteEventAsync(new AudioStopEvent(), cts.Token);

            var transcript = Assert.IsType<TranscriptEvent>(await parser.ReadEventAsync(cts.Token));
            Assert.Equal("<Alice />turn on the office lights", transcript.Text);
            Assert.True(transcript.Confidence > 0, "Confidence should be positive");
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

        Assert.Equal(1, diarizationEngine.ExtractEmbeddingCallCount);
        Assert.Equal(1, diarizationEngine.IdentifySpeakerCallCount);
        Assert.Equal(16_000, diarizationEngine.LastSampleRate);
        Assert.Equal(new[] { 0.25f, -0.25f, 0.25f, -0.25f }, diarizationEngine.LastAudioSamples);

        var updatedProfile = await profileStore.GetAsync("alice", CancellationToken.None);
        Assert.NotNull(updatedProfile);
        Assert.Equal(1, updatedProfile.InteractionCount);
    }

    [Fact]
    public async Task RunAsync_UnknownSpeakerWithFilter_TracksSpeakerAndSuppressesTranscript()
    {
        var options = new WyomingOptions
        {
            ReadTimeoutSeconds = 1,
        };
        var wakeSession = new TestWakeWordSession(
            new WakeWordResult
            {
                Keyword = "hey_lucia",
                Confidence = 0.93f,
                Timestamp = DateTimeOffset.UtcNow,
            });
        var wakeDetector = new TestWakeWordDetector(wakeSession);
        var sttSession = new TestSttSession(
            new SttResult
            {
                Text = "unlock the front door",
                Confidence = 0.91f,
            });
        var sttEngine = new TestSttEngine(sttSession);
        var vadEngine = new TestVadEngine(
            new TestVadSession(
                new VadSegment
                {
                    Samples = [0.15f, 0.05f, -0.05f, -0.15f],
                    StartTime = TimeSpan.Zero,
                    EndTime = TimeSpan.FromMilliseconds(250),
                    SampleRate = 16_000,
                }));
        var diarizationEngine = new TestDiarizationEngine();
        var profileStore = new InMemorySpeakerProfileStore();
        var voiceOptions = Options.Create(
            new VoiceProfileOptions
            {
                IgnoreUnknownVoices = true,
            });
        var router = new TestCommandRouter(CommandRouteResult.NoMatch(TimeSpan.Zero));
        var unknownTracker = new UnknownSpeakerTracker(
            profileStore,
            voiceOptions,
            NullLogger<UnknownSpeakerTracker>.Instance);
        var speakerFilter = new SpeakerVerificationFilter(
            voiceOptions,
            NullLogger<SpeakerVerificationFilter>.Instance);

        var (listener, client, serverClient, services, session, writer, parser) = await CreateConnectedSessionAsync(
            options,
            wakeDetector,
            sttEngine,
            vadEngine,
            configureServices: serviceCollection =>
            {
                serviceCollection.AddSingleton<IDiarizationEngine>(diarizationEngine);
                serviceCollection.AddSingleton<ISpeakerProfileStore>(profileStore);
                serviceCollection.AddSingleton(unknownTracker);
                serviceCollection.AddSingleton(speakerFilter);
                serviceCollection.AddSingleton<ICommandRouter>(router);
            });
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var runTask = session.RunAsync(cts.Token);

        try
        {
            await WriteWakeAndSpeechAsync(writer, cts.Token);

            _ = Assert.IsType<DetectionEvent>(await parser.ReadEventAsync(cts.Token));

            await writer.WriteEventAsync(new AudioStopEvent(), cts.Token);

            // AudioStop now triggers the full STT pipeline; read the transcript response
            var response = await parser.ReadEventAsync(cts.Token);
            var transcriptEvent = Assert.IsType<TranscriptEvent>(response);

            // Unknown speaker with IgnoreUnknownVoices=true: transcript is suppressed
            Assert.Equal("<Unknown1 />unlock the front door", transcriptEvent.Text);
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

        Assert.Equal(1, diarizationEngine.ExtractEmbeddingCallCount);

        var provisionalProfiles = await profileStore.GetProvisionalProfilesAsync(CancellationToken.None);
        Assert.Single(provisionalProfiles);
    }

    [Fact]
    public async Task RunAsync_NoRouteMatchAndFallbackDisabled_ReturnsOriginalTranscript()
    {
        var options = CreateOptions();
        var wakeSession = new TestWakeWordSession(
            new WakeWordResult
            {
                Keyword = "hey_lucia",
                Confidence = 0.93f,
                Timestamp = DateTimeOffset.UtcNow,
            });
        var wakeDetector = new TestWakeWordDetector(wakeSession);
        var sttSession = new TestSttSession(
            new SttResult
            {
                Text = "what is the weather today",
                Confidence = 0.85f,
            });
        var sttEngine = new TestSttEngine(sttSession);
        var vadEngine = new TestVadEngine(
            new TestVadSession(
                new VadSegment
                {
                    Samples = [0.1f, -0.1f, 0.1f, -0.1f],
                    StartTime = TimeSpan.Zero,
                    EndTime = TimeSpan.FromMilliseconds(250),
                    SampleRate = 16_000,
                }));
        var router = new TestCommandRouter(CommandRouteResult.NoMatch(TimeSpan.Zero))
        {
            FallbackToLlmEnabled = false,
        };

        var (listener, client, serverClient, services, session, writer, parser) = await CreateConnectedSessionAsync(
            options,
            wakeDetector,
            sttEngine,
            vadEngine,
            configureServices: serviceCollection =>
            {
                serviceCollection.AddSingleton<ICommandRouter>(router);
            });
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var runTask = session.RunAsync(cts.Token);

        try
        {
            await WriteWakeAndSpeechAsync(writer, cts.Token);

            _ = Assert.IsType<DetectionEvent>(await parser.ReadEventAsync(cts.Token));

            await writer.WriteEventAsync(new AudioStopEvent(), cts.Token);
            await writer.WriteEventAsync(new TranscribeEvent { Name = "default", Language = "en" }, cts.Token);

            var transcript = Assert.IsType<TranscriptEvent>(await parser.ReadEventAsync(cts.Token));
            Assert.Equal("<Unknown1 />what is the weather today", transcript.Text);
            Assert.True(transcript.Confidence > 0, "Confidence should be positive");
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

    }

    /// <summary>
    /// Verifies that when WyomingServer.Dispose() calls _sttConcurrency.Dispose() while a
    /// session has already acquired the semaphore slot and is inside GetFinalResultAsync(),
    /// ReleaseSttSlot() catches ObjectDisposedException and does NOT surface it as an
    /// unhandled session error.
    /// </summary>
    [Fact]
    public async Task RunAsync_SttSemaphoreDisposedWhileSlotHeld_ReleaseHandlesDisposedSemaphore()
    {
        // SemaphoreSlim(1): WaitAsync succeeds immediately so the session acquires the slot
        // (now at Transcribing transition) and enters GetFinalResultAsync. BlockingTestSttSession
        // gives a deterministic synchronisation point to dispose the semaphore while held.
        var semaphore = new SemaphoreSlim(1, 4);
        var blockingSession = new BlockingTestSttSession(
            new SttResult { Text = "shutdown test", Confidence = 1.0f });
        var sttEngine = new TestSttEngine(blockingSession);
        var options = CreateOptions();

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        TcpClient? client = null;
        TcpClient? serverClient = null;

        try
        {
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            var acceptTask = listener.AcceptTcpClientAsync();
            client = new TcpClient();
            await client.ConnectAsync(endpoint.Address, endpoint.Port);
            serverClient = await acceptTask;

            await using var serviceProvider = new ServiceCollection()
                .AddSingleton<IOptions<WyomingOptions>>(Options.Create(options))
                .AddSingleton<ISttEngine>(sttEngine)
                .BuildServiceProvider();

            var session = new WyomingSession(
                serverClient,
                serviceProvider,
                NullLogger<WyomingSession>.Instance,
                options,
                eventBus: null,
                sttConcurrency: semaphore);

            var clientStream = client.GetStream();
            var writer = new WyomingEventWriter(clientStream);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            // Drain server responses in the background to prevent TCP backpressure from
            // blocking the session's WriteEventAsync calls.
            _ = Task.Run(async () =>
            {
                var drainParser = new WyomingEventParser(clientStream, options);
                try
                {
                    while (await drainParser.ReadEventAsync(cts.Token) is not null) { }
                }
                catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException) { }
            });

            var runTask = session.RunAsync(cts.Token);

            // Drive the session into Transcribing state; the slot is acquired at the
            // Connected→Transcribing transition in HandleAudioChunkEventAsync.
            await writer.WriteEventAsync(
                new AudioStartEvent { Rate = 16_000, Width = 2, Channels = 1 }, cts.Token);
            await writer.WriteEventAsync(
                new AudioChunkEvent
                {
                    Rate = 16_000, Width = 2, Channels = 1,
                    Payload = PcmConverter.Float32ToInt16([0.1f, -0.1f, 0.1f, -0.1f]),
                },
                cts.Token);
            await writer.WriteEventAsync(new AudioStopEvent(), cts.Token);

            // Wait until GetFinalResultAsync is executing (semaphore slot is acquired).
            // This is deterministic — no polling or arbitrary delays.
            var inferenceReached = await Task.WhenAny(
                blockingSession.InferenceStarted.Task,
                Task.Delay(TimeSpan.FromSeconds(5), cts.Token));
            Assert.True(
                inferenceReached == blockingSession.InferenceStarted.Task,
                "Session did not reach STT inference within 5 s — AudioStop may not have been processed");

            // Simulate WyomingServer.Dispose() racing with the in-flight STT session.
            // The slot is held; ReleaseSttSlot() must catch ObjectDisposedException.
            semaphore.Dispose();

            // Unblock inference so the session proceeds to ReleaseSttSlot().
            blockingSession.Unblock();

            // Close the client connection after a brief yield so the session can write
            // the TranscriptEvent; ReadEventAsync then sees EOF and RunAsync exits cleanly.
            await Task.Delay(TimeSpan.FromMilliseconds(200), cts.Token);
            client.Close();

            var completed = await Task.WhenAny(runTask, Task.Delay(TimeSpan.FromSeconds(5))) == runTask;
            Assert.True(completed, "RunAsync did not complete after inference unblock and connection close");

            if (runTask.Exception is { } aggregateEx)
            {
                var innerEx = aggregateEx.InnerException ?? aggregateEx;
                Assert.False(
                    innerEx is ObjectDisposedException,
                    $"Session must not surface ObjectDisposedException on ReleaseSttSlot(); got: {innerEx.GetType().Name}: {innerEx.Message}");
            }
        }
        finally
        {
            try { client?.Close(); } catch (Exception) { }
            try { client?.Dispose(); } catch (Exception) { }
            try { serverClient?.Dispose(); } catch (Exception) { }
            listener.Stop();
        }
    }

    private static WyomingOptions CreateOptions()
    {
        return new WyomingOptions
        {
            ReadTimeoutSeconds = 5,
        };
    }

    /// <summary>
    /// Regression test for issue #178: STT semaphore MUST NOT gate the entire connection.
    /// With limit=2 STT sessions active (holding slots via BlockingTestSttSession), two
    /// further sessions that skip STT entirely (AudioStart + AudioStop with no chunks)
    /// must receive their empty TranscriptEvent immediately — they must not block waiting
    /// for a slot that is unrelated to their work.
    /// </summary>
    [Fact]
    public async Task RunAsync_NonSttEventsHandled_WhenAllSttSlotsExhausted()
    {
        const int limit = 2;
        var semaphore = new SemaphoreSlim(limit, limit);
        var blocking1 = new BlockingTestSttSession(new SttResult { Text = "stt1", Confidence = 1f });
        var blocking2 = new BlockingTestSttSession(new SttResult { Text = "stt2", Confidence = 1f });
        var options = new WyomingOptions { ReadTimeoutSeconds = 10 };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // Two sessions that will block inside GetFinalResultAsync (holding the STT slots).
        var (l1, c1, sc1, svc1, s1, w1, p1) = await CreateConnectedSessionAsync(
            options, sttEngine: new TestSttEngine(blocking1), sttConcurrency: semaphore);
        var (l2, c2, sc2, svc2, s2, w2, p2) = await CreateConnectedSessionAsync(
            options, sttEngine: new TestSttEngine(blocking2), sttConcurrency: semaphore);
        // Two sessions with no STT engine — they will receive an empty TranscriptEvent
        // from HandleAudioStopEventAsync (Connected + format set, no chunks = no STT).
        var (l3, c3, sc3, svc3, s3, w3, p3) = await CreateConnectedSessionAsync(
            options, sttConcurrency: semaphore);
        var (l4, c4, sc4, svc4, s4, w4, p4) = await CreateConnectedSessionAsync(
            options, sttConcurrency: semaphore);

        try
        {
            var run1 = s1.RunAsync(cts.Token);
            var run2 = s2.RunAsync(cts.Token);
            var run3 = s3.RunAsync(cts.Token);
            var run4 = s4.RunAsync(cts.Token);

            // Drive STT sessions into GetFinalResultAsync to occupy both slots.
            var audioChunkPayload = PcmConverter.Float32ToInt16([0.1f, -0.1f]);
            await w1.WriteEventAsync(new AudioStartEvent { Rate = 16_000, Width = 2, Channels = 1 }, cts.Token);
            await w1.WriteEventAsync(new AudioChunkEvent { Rate = 16_000, Width = 2, Channels = 1, Payload = audioChunkPayload }, cts.Token);
            await w1.WriteEventAsync(new AudioStopEvent(), cts.Token);

            await w2.WriteEventAsync(new AudioStartEvent { Rate = 16_000, Width = 2, Channels = 1 }, cts.Token);
            await w2.WriteEventAsync(new AudioChunkEvent { Rate = 16_000, Width = 2, Channels = 1, Payload = audioChunkPayload }, cts.Token);
            await w2.WriteEventAsync(new AudioStopEvent(), cts.Token);

            // Wait until both STT sessions are provably inside GetFinalResultAsync.
            var r1 = await Task.WhenAny(blocking1.InferenceStarted.Task, Task.Delay(TimeSpan.FromSeconds(5), cts.Token));
            var r2 = await Task.WhenAny(blocking2.InferenceStarted.Task, Task.Delay(TimeSpan.FromSeconds(5), cts.Token));
            Assert.True(r1 == blocking1.InferenceStarted.Task && r2 == blocking2.InferenceStarted.Task,
                "STT sessions did not reach inference within 5 s — slot may not be held");

            // Both STT slots are now occupied. Non-STT sessions send AudioStart + AudioStop
            // with NO audio chunks; this triggers the Connected-state path in
            // HandleAudioStopEventAsync, which writes an empty TranscriptEvent with NO
            // semaphore involvement. If the semaphore were still gating whole connections
            // (the original #178 bug), these reads would deadlock.
            await w3.WriteEventAsync(new AudioStartEvent { Rate = 16_000, Width = 2, Channels = 1 }, cts.Token);
            await w3.WriteEventAsync(new AudioStopEvent(), cts.Token);
            await w4.WriteEventAsync(new AudioStartEvent { Rate = 16_000, Width = 2, Channels = 1 }, cts.Token);
            await w4.WriteEventAsync(new AudioStopEvent(), cts.Token);

            var evt3 = await p3.ReadEventAsync(cts.Token);
            var evt4 = await p4.ReadEventAsync(cts.Token);
            Assert.IsType<TranscriptEvent>(evt3);
            Assert.IsType<TranscriptEvent>(evt4);

            // Unblock the STT sessions so they can complete cleanly.
            blocking1.Unblock();
            blocking2.Unblock();

            // Drain STT-session responses so they can exit.
            _ = Task.Run(async () =>
            {
                try { while (await p1.ReadEventAsync(cts.Token) is not null) { } }
                catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException) { }
            });
            _ = Task.Run(async () =>
            {
                try { while (await p2.ReadEventAsync(cts.Token) is not null) { } }
                catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException) { }
            });

            c1.Close(); await run1;
            c2.Close(); await run2;
            c3.Close(); await run3;
            c4.Close(); await run4;
        }
        finally
        {
            foreach (var (l, c, sc, svc) in new[] {
                (l1, c1, sc1, svc1), (l2, c2, sc2, svc2),
                (l3, c3, sc3, svc3), (l4, c4, sc4, svc4) })
            {
                try { c.Close(); } catch (Exception) { }
                c.Dispose();
                sc.Dispose();
                l.Stop();
                await svc.DisposeAsync();
            }
            semaphore.Dispose();
        }
    }

    /// <summary>
    /// Verifies that <c>MaxConcurrentSttSessions = 2</c> actually bounds STT inference
    /// when 4 sessions drive STT simultaneously. All 4 sessions must complete (no hang),
    /// and the peak concurrent inference count must never exceed the configured limit.
    /// </summary>
    [Fact]
    public async Task RunAsync_FourConcurrentSessions_InferenceBoundedBySemaphoreLimit()
    {
        const int limit = 2;
        const int sessionCount = 4;

        // Shared semaphore — mirrors what WyomingServer passes to each WyomingSession.
        var semaphore = new SemaphoreSlim(limit, limit);
        // Shared counter array: [0] = current concurrent, [1] = peak observed.
        var counters = new int[2];
        var options = new WyomingOptions { ReadTimeoutSeconds = 10 };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // Launch all sessions concurrently, each with its own TCP connection.
        var sessionTasks = Enumerable.Range(0, sessionCount).Select(async _ =>
        {
            // Each call to CreateSession() produces a fresh tracker instance that increments
            // the shared counters when inference is running.
            var engine = new FactoryTestSttEngine(
                () => new ConcurrencyTrackingTestSttSession(counters, inferenceDelayMs: 100));

            var (listener, client, serverClient, services, session, writer, parser) =
                await CreateConnectedSessionAsync(options, sttEngine: engine, sttConcurrency: semaphore);

            try
            {
                using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                var runTask = session.RunAsync(sessionCts.Token);

                // Drive the session through a full STT pass.
                await writer.WriteEventAsync(
                    new AudioStartEvent { Rate = 16_000, Width = 2, Channels = 1 }, cts.Token);
                await writer.WriteEventAsync(
                    new AudioChunkEvent
                    {
                        Rate = 16_000, Width = 2, Channels = 1,
                        Payload = PcmConverter.Float32ToInt16([0.1f, -0.1f]),
                    },
                    cts.Token);
                await writer.WriteEventAsync(new AudioStopEvent(), cts.Token);

                // Confirm the session produced a transcript (speaker tag may be prepended).
                var transcript = Assert.IsType<TranscriptEvent>(await parser.ReadEventAsync(cts.Token));
                Assert.Contains("concurrent test", transcript.Text, StringComparison.Ordinal);

                client.Close();
                await runTask;
                return transcript;
            }
            finally
            {
                try { client.Close(); } catch (Exception) { }
                client.Dispose();
                serverClient.Dispose();
                listener.Stop();
                await services.DisposeAsync();
            }
        }).ToList();

        // All 4 must complete without hanging.
        await Task.WhenAll(sessionTasks);

        // Peak concurrent STT must never exceed the semaphore limit.
        Assert.InRange(counters[1], 1, limit);
    }

    private static async Task<(
        TcpListener Listener,
        TcpClient Client,
        TcpClient ServerClient,
        ServiceProvider Services,
        WyomingSession Session,
        WyomingEventWriter Writer,
        WyomingEventParser Parser)> CreateConnectedSessionAsync(
        WyomingOptions options,
        IWakeWordDetector? wakeWordDetector = null,
        ISttEngine? sttEngine = null,
        IVadEngine? vadEngine = null,
        Action<ServiceCollection>? configureServices = null,
        SemaphoreSlim? sttConcurrency = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOptions<WyomingOptions>>(Options.Create(options));

        if (wakeWordDetector is not null)
        {
            services.AddSingleton<IWakeWordDetector>(wakeWordDetector);
        }

        if (sttEngine is not null)
        {
            services.AddSingleton<ISttEngine>(sttEngine);
        }

        if (vadEngine is not null)
        {
            services.AddSingleton<IVadEngine>(vadEngine);
        }

        configureServices?.Invoke(services);

        var serviceProvider = services.BuildServiceProvider();
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();

        var endpoint = (System.Net.IPEndPoint)listener.LocalEndpoint;
        var acceptTask = listener.AcceptTcpClientAsync();
        var client = new TcpClient();
        await client.ConnectAsync(endpoint.Address, endpoint.Port);
        var serverClient = await acceptTask;

        var session = new WyomingSession(
            serverClient,
            serviceProvider,
            NullLogger<WyomingSession>.Instance,
            options,
            eventBus: null,
            sttConcurrency: sttConcurrency);
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

    private static async Task WriteWakeAndSpeechAsync(WyomingEventWriter writer, CancellationToken ct)
    {
        await writer.WriteEventAsync(new DetectEvent { Names = ["hey_lucia"] }, ct);
        await writer.WriteEventAsync(
            new AudioStartEvent
            {
                Rate = 16_000,
                Width = 2,
                Channels = 1,
            },
            ct);
        await writer.WriteEventAsync(
            new AudioChunkEvent
            {
                Rate = 16_000,
                Width = 2,
                Channels = 1,
                Payload = PcmConverter.Float32ToInt16([0.25f, -0.25f, 0.25f, -0.25f]),
            },
            ct);
    }

    /// <summary>
    /// Full end-to-end Wyoming protocol test using real ONNX models.
    /// Connects as a Wyoming client, streams the actual WAV sample as protocol audio
    /// chunks, and verifies the transcript response — exactly as Home Assistant would.
    /// </summary>
    [SkippableFact]
    [Trait("Category", "Integration")]
    public async Task FullPipeline_WyomingClient_RealModels_ProducesTranscript()
    {
        var sttDir = ResolveFromRepoRoot(SttModelDir);
        var gtcrnPath = ResolveFromRepoRoot(GtcrnModelPath);
        var wavPath = ResolveFromRepoRoot(SampleWavPath);
        var expectedTextPath = Path.ChangeExtension(wavPath, ".txt");

        Skip.If(!File.Exists(wavPath), $"WAV not found at {wavPath}");

        var offlineModelDir = FindBestOfflineModelDir(sttDir);
        Skip.If(offlineModelDir is null, "No offline STT model available");

        var expectedLine = File.Exists(expectedTextPath)
            ? (await File.ReadAllTextAsync(expectedTextPath)).Trim() : null;
        var expectedText = expectedLine?.Contains(':') == true
            ? expectedLine[(expectedLine.IndexOf(':') + 1)..].Trim()
            : expectedLine ?? "";

        var (rawSamples, sampleRate) = ReadWav(wavPath);

        // Build real services — same as AddWyomingServer but targeted
        var options = new WyomingOptions { ReadTimeoutSeconds = 30 };
        var services = new ServiceCollection();
        services.AddSingleton<IOptions<WyomingOptions>>(Options.Create(options));
        services.AddSingleton(Options.Create(new lucia.Wyoming.Models.SttModelOptions
        {
            ModelBasePath = ResolveFromRepoRoot(SttModelDir),
            SampleRate = sampleRate,
        }));
        services.AddSingleton(Options.Create(new HybridSttOptions
        {
            ModelPath = offlineModelDir!,
            SampleRate = sampleRate,
            NumThreads = 4,
            RefreshIntervalMs = 400,
            MinAudioMs = 300,
        }));
        services.AddSingleton(Options.Create(new lucia.Wyoming.Audio.SpeechEnhancementOptions
        {
            Enabled = File.Exists(gtcrnPath),
            ModelBasePath = Path.GetDirectoryName(Path.GetDirectoryName(gtcrnPath)) ?? "",
        }));
        var vadModelDir = ResolveFromRepoRoot("lucia.AgentHost/models/vad");
        var hasVad = Directory.Exists(vadModelDir)
            && Directory.EnumerateFiles(vadModelDir, "*.onnx", SearchOption.AllDirectories).Any();

        services.AddSingleton(Options.Create(new lucia.Wyoming.Vad.VadOptions
        {
            ModelBasePath = vadModelDir,
            ActiveModel = "silero_vad_v5",
            ModelPath = hasVad
                ? Directory.EnumerateFiles(vadModelDir, "*.onnx", SearchOption.AllDirectories).First()
                : "",
        }));
        services.AddSingleton(Options.Create(new lucia.Wyoming.WakeWord.WakeWordOptions()));
        services.AddSingleton(Options.Create(new lucia.Wyoming.Diarization.DiarizationOptions()));
        services.AddSingleton(Options.Create(new GraniteOptions()));
        services.AddSingleton(Options.Create(new OfflineSttOptions()));

        services.AddSingleton<IModelChangeNotifier>(new InlineModelChangeNotifier());

        // Register real engines
        services.AddSingleton<ISttEngine>(sp => new HybridSttEngine(
            sp.GetRequiredService<IOptions<HybridSttOptions>>(),
            sp.GetRequiredService<IOptions<lucia.Wyoming.Models.SttModelOptions>>(),
            sp.GetRequiredService<lucia.Wyoming.Models.IModelChangeNotifier>(),
            lucia.Tests.TestDoubles.TestOnnxProvider.Instance,
            NullLogger<HybridSttEngine>.Instance));

        services.AddSingleton<lucia.Wyoming.Vad.IVadEngine>(sp =>
            new lucia.Wyoming.Vad.SherpaVadEngine(
                sp.GetRequiredService<IOptions<lucia.Wyoming.Vad.VadOptions>>(),
                sp.GetRequiredService<IModelChangeNotifier>(),
                NullLogger<lucia.Wyoming.Vad.SherpaVadEngine>.Instance));

        // Register GTCRN enhancement if model available
        if (File.Exists(gtcrnPath))
        {
            services.AddSingleton<lucia.Wyoming.Audio.ISpeechEnhancer>(sp =>
                new lucia.Wyoming.Audio.GtcrnSpeechEnhancer(
                    sp.GetRequiredService<IOptions<lucia.Wyoming.Audio.SpeechEnhancementOptions>>(),
                    sp.GetRequiredService<lucia.Wyoming.Models.IModelChangeNotifier>(),
                    lucia.Tests.TestDoubles.TestOnnxProvider.Instance,
                    NullLogger<lucia.Wyoming.Audio.GtcrnSpeechEnhancer>.Instance));
        }

        var serviceProvider = services.BuildServiceProvider();

        // Verify engines loaded
        var engine = serviceProvider.GetService<ISttEngine>();
        Skip.If(engine is null || !engine.IsReady, "Hybrid STT engine not ready");
        var vadEngine = serviceProvider.GetService<lucia.Wyoming.Vad.IVadEngine>();
        Skip.If(vadEngine is null || !vadEngine.IsReady, "VAD engine not ready");

        // Create TCP connection (Wyoming protocol transport)
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (System.Net.IPEndPoint)listener.LocalEndpoint;
        var acceptTask = listener.AcceptTcpClientAsync();
        var client = new TcpClient();
        await client.ConnectAsync(endpoint.Address, endpoint.Port);
        var serverClient = await acceptTask;

        var session = new WyomingSession(
            serverClient, serviceProvider,
            NullLogger<WyomingSession>.Instance, options);

        var stream = client.GetStream();
        var writer = new WyomingEventWriter(stream);
        var parser = new WyomingEventParser(stream, options);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var runTask = session.RunAsync(cts.Token);

        try
        {
            var pipelineSw = System.Diagnostics.Stopwatch.StartNew();

            // 1. Send AudioStart (direct STT mode — no wake word)
            await writer.WriteEventAsync(
                new AudioStartEvent { Rate = sampleRate, Width = 2, Channels = 1 },
                cts.Token);

            // 2. Stream the WAV as 10ms audio chunks, throttled to real-time
            //    so the hybrid engine can run progressive re-transcriptions during streaming
            const int chunkSamples = 160;
            var chunkDurationMs = chunkSamples * 1000 / sampleRate; // 10ms
            var streamingSw = System.Diagnostics.Stopwatch.StartNew();
            for (var offset = 0; offset < rawSamples.Length; offset += chunkSamples)
            {
                var remaining = Math.Min(chunkSamples, rawSamples.Length - offset);
                var chunk = rawSamples.AsSpan(offset, remaining);
                var pcmPayload = PcmConverter.Float32ToInt16(chunk.ToArray());

                await writer.WriteEventAsync(
                    new AudioChunkEvent
                    {
                        Rate = sampleRate,
                        Width = 2,
                        Channels = 1,
                        Payload = pcmPayload,
                    },
                    cts.Token);

                // Throttle to real-time: wait ~10ms per chunk so hybrid re-transcription
                // happens during streaming, not all at finalization
                await Task.Delay(chunkDurationMs, cts.Token);
            }
            streamingSw.Stop();

            // 3. Send AudioStop — triggers finalization
            var finalizeSw = System.Diagnostics.Stopwatch.StartNew();
            await writer.WriteEventAsync(new AudioStopEvent(), cts.Token);

            // 4. Read the transcript response
            var response = await parser.ReadEventAsync(cts.Token);
            finalizeSw.Stop();
            pipelineSw.Stop();

            if (response is ErrorEvent errorEvt)
            {
                output.WriteLine($"ERROR from session: [{errorEvt.Code}] {errorEvt.Text}");
                Assert.Fail($"Session returned error: [{errorEvt.Code}] {errorEvt.Text}");
            }
            var transcript = Assert.IsType<TranscriptEvent>(response);

            output.WriteLine($"Wyoming transcript: \"{transcript.Text}\"");
            output.WriteLine($"Confidence: {transcript.Confidence}");

            var audioDurationMs = rawSamples.Length * 1000 / sampleRate;
            output.WriteLine("");
            output.WriteLine("═══ End-to-End Wyoming Pipeline Benchmark ═══");
            output.WriteLine($"  Audio duration:     {audioDurationMs}ms");
            output.WriteLine($"  Stream chunks:      {streamingSw.ElapsedMilliseconds}ms (sending {rawSamples.Length / chunkSamples} chunks over TCP)");
            output.WriteLine($"  Finalize + respond: {finalizeSw.ElapsedMilliseconds}ms (AudioStop → TranscriptEvent)");
            output.WriteLine($"  Total pipeline:     {pipelineSw.ElapsedMilliseconds}ms (AudioStart → TranscriptEvent)");
            output.WriteLine($"  Overhead:           {pipelineSw.ElapsedMilliseconds - audioDurationMs}ms above real-time");
            output.WriteLine($"  Realtime factor:    {pipelineSw.ElapsedMilliseconds / (double)audioDurationMs:F2}x");

            // Strip speaker tag for WER comparison
            var transcriptText = transcript.Text;
            var tagEnd = transcriptText.IndexOf("/>", StringComparison.Ordinal);
            if (tagEnd >= 0)
                transcriptText = transcriptText[(tagEnd + 2)..].Trim();

            var wer = ComputeWordErrorRate(expectedText, transcriptText);
            output.WriteLine($"WER: {wer:P1}");
            output.WriteLine($"Expected: \"{expectedText}\"");

            Assert.False(string.IsNullOrWhiteSpace(transcript.Text),
                "Wyoming session returned empty transcript");

            Assert.True(transcript.Text.StartsWith("<", StringComparison.Ordinal),
                $"Transcript should have speaker tag prefix, got: \"{transcript.Text}\"");

            Assert.True(wer <= 0.10,
                $"Wyoming pipeline WER {wer:P1} exceeds 10%. " +
                $"Expected: \"{expectedText}\", Got: \"{transcriptText}\"");
        }
        finally
        {
            client.Close();
            try { await runTask; }
            catch (ObjectDisposedException) { /* Expected — client closed the connection */ }
            catch (IOException) { /* Expected — connection reset */ }
            serverClient.Dispose();
            client.Dispose();
            listener.Stop();
            await serviceProvider.DisposeAsync();
        }
    }

    private static string ResolveFromRepoRoot(string relativePath)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "lucia-dotnet.slnx")))
            dir = Path.GetDirectoryName(dir);
        return dir is not null ? Path.Combine(dir, relativePath) : relativePath;
    }

    private static (float[] Samples, int SampleRate) ReadWav(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);

        reader.ReadChars(4); // RIFF
        reader.ReadInt32();
        reader.ReadChars(4); // WAVE

        int sampleRate = 0;
        while (stream.Position < stream.Length)
        {
            var chunkId = new string(reader.ReadChars(4));
            var chunkSize = reader.ReadInt32();
            if (chunkId == "fmt ")
            {
                reader.ReadInt16(); // format
                reader.ReadInt16(); // channels
                sampleRate = reader.ReadInt32();
                reader.ReadBytes(chunkSize - 8);
            }
            else if (chunkId == "data")
            {
                var totalSamples = chunkSize / 2;
                var samples = new float[totalSamples];
                for (var i = 0; i < totalSamples; i++)
                    samples[i] = reader.ReadInt16() / 32768f;
                return (samples, sampleRate);
            }
            else
            {
                reader.ReadBytes(chunkSize);
            }
        }

        throw new InvalidDataException("No data chunk");
    }

    private static string? FindBestOfflineModelDir(string sttDir)
    {
        if (!Directory.Exists(sttDir)) return null;
        return Directory.EnumerateDirectories(sttDir)
            .Where(d => Directory.EnumerateFiles(d, "tokens.txt", SearchOption.AllDirectories).Any())
            .Where(d => (Path.GetFileName(d) ?? "").Contains("parakeet", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(d => (Path.GetFileName(d) ?? "").Contains("0.6b", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .FirstOrDefault();
    }

    private static double ComputeWordErrorRate(string reference, string hypothesis)
    {
        var refWords = reference.ToUpperInvariant()
            .Replace("'S", "S", StringComparison.Ordinal)
            .Replace(",", "", StringComparison.Ordinal)
            .Replace(".", "", StringComparison.Ordinal)
            .Replace("ZACH", "ZACK", StringComparison.Ordinal)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var hypWords = hypothesis.ToUpperInvariant()
            .Replace("'S", "S", StringComparison.Ordinal)
            .Replace(",", "", StringComparison.Ordinal)
            .Replace(".", "", StringComparison.Ordinal)
            .Replace("ZACH", "ZACK", StringComparison.Ordinal)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (refWords.Length == 0) return hypWords.Length == 0 ? 0.0 : 1.0;
        var d = new int[refWords.Length + 1, hypWords.Length + 1];
        for (var i = 0; i <= refWords.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= hypWords.Length; j++) d[0, j] = j;
        for (var i = 1; i <= refWords.Length; i++)
            for (var j = 1; j <= hypWords.Length; j++)
            {
                var cost = string.Equals(refWords[i - 1], hypWords[j - 1], StringComparison.OrdinalIgnoreCase) ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            }
        return (double)d[refWords.Length, hypWords.Length] / refWords.Length;
    }

    private const string SttModelDir = "lucia.AgentHost/models/stt";
    private const string GtcrnModelPath = "lucia.AgentHost/models/speech-enhancement/gtcrn_simple/gtcrn_simple.onnx";
    private const string SampleWavPath = "samples/unfiltered_sample.wav";

    private sealed class InlineModelChangeNotifier : IModelChangeNotifier
    {
#pragma warning disable CS0067
        public event Action<ActiveModelChangedEvent>? ActiveModelChanged;
#pragma warning restore CS0067
    }
}
