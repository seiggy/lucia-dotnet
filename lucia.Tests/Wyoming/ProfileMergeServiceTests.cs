using FakeItEasy;
using lucia.Wyoming.Diarization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace lucia.Tests.Wyoming;

public sealed class ProfileMergeServiceTests
{
    [Fact]
    public async Task MergeAsync_IncompatibleEmbeddingDimensionsKeepsSourceClips()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"lucia-test-{Guid.NewGuid():N}");
        try
        {
            var store = new InMemorySpeakerProfileStore();
            await store.CreateAsync(
                new SpeakerProfile
                {
                    Id = "source",
                    Name = "Source",
                    AverageEmbedding = [1f, 2f, 3f],
                    Embeddings = [[1f, 2f, 3f]],
                },
                CancellationToken.None);
            await store.CreateAsync(
                new SpeakerProfile
                {
                    Id = "target",
                    Name = "Target",
                    AverageEmbedding = [1f, 2f],
                    Embeddings = [[1f, 2f]],
                },
                CancellationToken.None);
            var clipService = new AudioClipService(
                new OptionsMonitorStub<VoiceProfileOptions>(new VoiceProfileOptions
                {
                    AudioClipBasePath = tempDir,
                }),
                NullLogger<AudioClipService>.Instance);
            await clipService.SaveClipAsync("source", new float[] { 0.1f }, 16000, null);
            var service = new ProfileMergeService(
                store,
                clipService,
                NullLogger<ProfileMergeService>.Instance);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.MergeAsync("source", "target"));

            Assert.Single(clipService.GetClips("source"));
            Assert.Empty(clipService.GetClips("target"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task MergeAsync_TargetUpdateFailureKeepsSourceClips()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"lucia-test-{Guid.NewGuid():N}");
        try
        {
            var source = new SpeakerProfile
            {
                Id = "source",
                Name = "Source",
                AverageEmbedding = [1f, 2f],
                Embeddings = [[1f, 2f]],
            };
            var target = source with { Id = "target", Name = "Target" };
            var store = A.Fake<ISpeakerProfileStore>();
            A.CallTo(() => store.GetAsync("source", A<CancellationToken>._)).Returns(source);
            A.CallTo(() => store.GetAsync("target", A<CancellationToken>._)).Returns(target);
            A.CallTo(() => store.UpdateAtomicAsync(
                    "source",
                    A<Func<SpeakerProfile, SpeakerProfile>>._,
                    A<CancellationToken>._))
                .ReturnsLazily(call =>
                {
                    var transform = call.GetArgument<Func<SpeakerProfile, SpeakerProfile>>(1)!;
                    return Task.FromResult<SpeakerProfile?>(transform(source));
                });
            A.CallTo(() => store.UpdateAtomicAsync(
                    "target",
                    A<Func<SpeakerProfile, SpeakerProfile>>._,
                    A<CancellationToken>._))
                .ThrowsAsync(new InvalidOperationException("update failed"));
            var clipService = new AudioClipService(
                new OptionsMonitorStub<VoiceProfileOptions>(new VoiceProfileOptions
                {
                    AudioClipBasePath = tempDir,
                }),
                NullLogger<AudioClipService>.Instance);
            await clipService.SaveClipAsync("source", new float[] { 0.1f }, 16000, null);
            var service = new ProfileMergeService(
                store,
                clipService,
                NullLogger<ProfileMergeService>.Instance);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.MergeAsync("source", "target"));

            Assert.Single(clipService.GetClips("source"));
            Assert.Empty(clipService.GetClips("target"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task MergeAsync_CancellationAfterTargetUpdateCompletesCommittedMerge()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"lucia-test-{Guid.NewGuid():N}");
        try
        {
            var source = new SpeakerProfile
            {
                Id = "source",
                Name = "Source",
                AverageEmbedding = [1f, 2f],
                Embeddings = [[1f, 2f]],
            };
            var target = source with { Id = "target", Name = "Target" };
            var store = A.Fake<ISpeakerProfileStore>();
            A.CallTo(() => store.GetAsync("source", A<CancellationToken>._)).Returns(source);
            A.CallTo(() => store.GetAsync("target", A<CancellationToken>._)).Returns(target);
            using var cancellation = new CancellationTokenSource();
            A.CallTo(() => store.UpdateAtomicAsync(
                    A<string>._,
                    A<Func<SpeakerProfile, SpeakerProfile>>._,
                    A<CancellationToken>._))
                .ReturnsLazily(call =>
                {
                    var id = call.GetArgument<string>(0)!;
                    var transform = call.GetArgument<Func<SpeakerProfile, SpeakerProfile>>(1)!;
                    if (id == "source")
                    {
                        return Task.FromResult<SpeakerProfile?>(transform(source));
                    }

                    cancellation.Cancel();
                    return Task.FromResult<SpeakerProfile?>(transform(target));
                });
            var clipService = new AudioClipService(
                new OptionsMonitorStub<VoiceProfileOptions>(new VoiceProfileOptions
                {
                    AudioClipBasePath = tempDir,
                }),
                NullLogger<AudioClipService>.Instance);
            await clipService.SaveClipAsync("source", new float[] { 0.1f }, 16000, null);
            var service = new ProfileMergeService(
                store,
                clipService,
                NullLogger<ProfileMergeService>.Instance);

            await service.MergeAsync("source", "target", cancellation.Token);

            Assert.Empty(clipService.GetClips("source"));
            Assert.Single(clipService.GetClips("target"));
            A.CallTo(() => store.DeleteAsync("source", CancellationToken.None))
                .MustHaveHappenedOnceExactly();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task MergeAsync_RetryDoesNotDuplicateCommittedProfileData()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"lucia-test-{Guid.NewGuid():N}");
        try
        {
            var store = new InMemorySpeakerProfileStore();
            await store.CreateAsync(
                new SpeakerProfile
                {
                    Id = "source",
                    Name = "Source",
                    AverageEmbedding = [1f, 2f],
                    Embeddings = [[1f, 2f]],
                    InteractionCount = 3,
                },
                CancellationToken.None);
            await store.CreateAsync(
                new SpeakerProfile
                {
                    Id = "target",
                    Name = "Target",
                    AverageEmbedding = [3f, 4f],
                    Embeddings = [[3f, 4f]],
                    InteractionCount = 5,
                },
                CancellationToken.None);
            var clipService = new AudioClipService(
                new OptionsMonitorStub<VoiceProfileOptions>(new VoiceProfileOptions
                {
                    AudioClipBasePath = tempDir,
                }),
                NullLogger<AudioClipService>.Instance);
            await clipService.SaveClipAsync("source", new float[] { 0.1f }, 16000, null);
            clipService.BlockProfileClips("target");
            var service = new ProfileMergeService(
                store,
                clipService,
                NullLogger<ProfileMergeService>.Instance);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.MergeAsync("source", "target"));
            clipService.AllowProfileClips("target");
            await service.MergeAsync("source", "target");
            var retried = await service.MergeAsync("source", "target");
            var merged = await store.GetAsync("target", CancellationToken.None);

            Assert.NotNull(merged);
            Assert.Equal("target", retried.Id);
            Assert.Equal(2, merged.Embeddings.Length);
            Assert.Equal(8, merged.InteractionCount);
            Assert.Equal(["source"], merged.MergedProfileIds);
            Assert.Single(clipService.GetClips("target"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task MergeAsync_ConcurrentTargetsAllowOnlyOneSourceOwner()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"lucia-test-{Guid.NewGuid():N}");
        try
        {
            var source = new SpeakerProfile
            {
                Id = "source",
                Name = "Source",
                AverageEmbedding = [1f, 2f],
                Embeddings = [[1f, 2f]],
            };
            var targets = new Dictionary<string, SpeakerProfile>
            {
                ["target-1"] = source with { Id = "target-1", Name = "Target 1" },
                ["target-2"] = source with { Id = "target-2", Name = "Target 2" },
            };
            var sourceExists = true;
            var firstUpdateStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var allowFirstUpdate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var store = A.Fake<ISpeakerProfileStore>();
            A.CallTo(() => store.GetAsync(A<string>._, A<CancellationToken>._))
                .ReturnsLazily(call =>
                {
                    var id = call.GetArgument<string>(0)!;
                    SpeakerProfile? profile = id == "source"
                        ? sourceExists ? source : null
                        : targets[id];
                    return Task.FromResult(profile);
                });
            A.CallTo(() => store.UpdateAtomicAsync(
                    A<string>._,
                    A<Func<SpeakerProfile, SpeakerProfile>>._,
                    A<CancellationToken>._))
                .ReturnsLazily(async call =>
                {
                    var id = call.GetArgument<string>(0)!;
                    var transform = call.GetArgument<Func<SpeakerProfile, SpeakerProfile>>(1)!;
                    if (id == "source")
                    {
                        if (!sourceExists)
                        {
                            return null;
                        }

                        source = transform(source);
                        return source;
                    }

                    if (id == "target-1" && firstUpdateStarted.TrySetResult())
                    {
                        await allowFirstUpdate.Task;
                    }

                    return (SpeakerProfile?)transform(targets[id]);
                });
            A.CallTo(() => store.DeleteAsync("source", CancellationToken.None))
                .Invokes(() => sourceExists = false);
            var clipService = new AudioClipService(
                new OptionsMonitorStub<VoiceProfileOptions>(new VoiceProfileOptions
                {
                    AudioClipBasePath = tempDir,
                }),
                NullLogger<AudioClipService>.Instance);
            await clipService.SaveClipAsync("source", new float[] { 0.1f }, 16000, null);
            var service = new ProfileMergeService(
                store,
                clipService,
                NullLogger<ProfileMergeService>.Instance);

            var first = service.MergeAsync("source", "target-1");
            await firstUpdateStarted.Task;
            var second = service.MergeAsync("source", "target-2");
            allowFirstUpdate.SetResult();

            await first;
            await Assert.ThrowsAsync<KeyNotFoundException>(() => second);
            Assert.Single(clipService.GetClips("target-1"));
            Assert.Empty(clipService.GetClips("target-2"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
