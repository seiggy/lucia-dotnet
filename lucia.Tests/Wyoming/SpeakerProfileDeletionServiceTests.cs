using FakeItEasy;
using lucia.Wyoming.Diarization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace lucia.Tests.Wyoming;

public sealed class SpeakerProfileDeletionServiceTests
{
    [Fact]
    public async Task DeleteAsync_CanceledWaiterKeepsSharedDeletionRegistered()
    {
        var profileStore = A.Fake<ISpeakerProfileStore>();
        var deletionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowDeletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        A.CallTo(() => profileStore.GetAsync("profile-1", CancellationToken.None))
            .Returns(new SpeakerProfile { Id = "profile-1", Name = "Test" });
        A.CallTo(() => profileStore.DeleteAsync("profile-1", CancellationToken.None))
            .Invokes(deletionStarted.SetResult)
            .Returns(allowDeletion.Task);

        var options = A.Fake<IOptionsMonitor<VoiceProfileOptions>>();
        A.CallTo(() => options.CurrentValue).Returns(new VoiceProfileOptions
        {
            AudioClipBasePath = Path.Combine(Path.GetTempPath(), $"lucia-test-{Guid.NewGuid():N}"),
        });
        var clipService = new AudioClipService(options, NullLogger<AudioClipService>.Instance);
        var service = new SpeakerProfileDeletionService(
            profileStore,
            clipService,
            NullLogger<SpeakerProfileDeletionService>.Instance);
        using var cancellation = new CancellationTokenSource();

        var canceledWaiter = service.DeleteAsync("profile-1", cancellation.Token);
        await deletionStarted.Task;
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledWaiter);

        var secondWaiter = service.DeleteAsync("profile-1", CancellationToken.None);
        A.CallTo(() => profileStore.DeleteAsync("profile-1", CancellationToken.None))
            .MustHaveHappenedOnceExactly();

        allowDeletion.SetResult();
        await secondWaiter;
    }

    [Fact]
    public async Task DeleteAsync_WaitsForExpiredProfileDeletion()
    {
        var profileStore = A.Fake<ISpeakerProfileStore>();
        var cleanupStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowCleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanupFinished = false;
        A.CallTo(() => profileStore.GetAsync("profile-1", CancellationToken.None))
            .ReturnsLazily(() => cleanupFinished
                ? null
                : new SpeakerProfile { Id = "profile-1", Name = "Test" });
        A.CallTo(() => profileStore.DeleteExpiredProvisionalAsync(
                "profile-1",
                A<DateTimeOffset>._,
                CancellationToken.None))
            .Invokes(cleanupStarted.SetResult)
            .ReturnsLazily(async () =>
            {
                await allowCleanup.Task;
                cleanupFinished = true;
                return true;
            });

        var service = CreateService(profileStore);
        var cleanup = service.DeleteExpiredProvisionalAsync(
            "profile-1",
            DateTimeOffset.UtcNow,
            CancellationToken.None);
        await cleanupStarted.Task;

        var apiDeletion = service.DeleteAsync("profile-1", CancellationToken.None);
        Assert.False(apiDeletion.IsCompleted);

        allowCleanup.SetResult();
        Assert.True(await cleanup);
        await apiDeletion;
    }

    [Fact]
    public async Task DeleteAsync_AbsentProfileBlocksLaterClipReassignment()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"lucia-test-{Guid.NewGuid():N}");
        try
        {
            var profileStore = A.Fake<ISpeakerProfileStore>();
            A.CallTo(() => profileStore.GetAsync("deleted", CancellationToken.None))
                .Returns((SpeakerProfile?)null);
            var options = A.Fake<IOptionsMonitor<VoiceProfileOptions>>();
            A.CallTo(() => options.CurrentValue).Returns(new VoiceProfileOptions
            {
                AudioClipBasePath = tempDir,
            });
            var clipService = new AudioClipService(options, NullLogger<AudioClipService>.Instance);
            await clipService.SaveClipAsync("source", new float[] { 0.1f }, 16000, null);
            var service = new SpeakerProfileDeletionService(
                profileStore,
                clipService,
                NullLogger<SpeakerProfileDeletionService>.Instance);

            await service.DeleteAsync("deleted", CancellationToken.None);
            var restartedClipService = new AudioClipService(
                options,
                NullLogger<AudioClipService>.Instance);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => restartedClipService.MoveClipsAsync("source", "deleted"));
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
    public async Task DeleteAsync_StoreFailureRemovesPersistentTombstone()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"lucia-test-{Guid.NewGuid():N}");
        try
        {
            var profileStore = A.Fake<ISpeakerProfileStore>();
            A.CallTo(() => profileStore.GetAsync("profile-1", CancellationToken.None))
                .ThrowsAsync(new IOException("store unavailable"));
            var options = A.Fake<IOptionsMonitor<VoiceProfileOptions>>();
            A.CallTo(() => options.CurrentValue).Returns(new VoiceProfileOptions
            {
                AudioClipBasePath = tempDir,
            });
            var clipService = new AudioClipService(options, NullLogger<AudioClipService>.Instance);
            var service = new SpeakerProfileDeletionService(
                profileStore,
                clipService,
                NullLogger<SpeakerProfileDeletionService>.Instance);

            await Assert.ThrowsAsync<IOException>(
                () => service.DeleteAsync("profile-1", CancellationToken.None));
            var restartedClipService = new AudioClipService(
                options,
                NullLogger<AudioClipService>.Instance);

            await restartedClipService.SaveClipAsync(
                "profile-1",
                new float[] { 0.1f },
                16000,
                null);
            Assert.Single(restartedClipService.GetClips("profile-1"));
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
    public async Task ReconcileOrphanedClipsAsync_RemovesIncompleteClipsForExistingProfile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"lucia-test-{Guid.NewGuid():N}");
        try
        {
            var profileDir = Path.Combine(tempDir, "profile-1");
            Directory.CreateDirectory(profileDir);
            await File.WriteAllBytesAsync(Path.Combine(profileDir, "orphan.wav"), [1, 2, 3]);
            var profileStore = A.Fake<ISpeakerProfileStore>();
            A.CallTo(() => profileStore.GetAsync("profile-1", A<CancellationToken>._))
                .Returns(new SpeakerProfile { Id = "profile-1", Name = "Test" });
            var options = A.Fake<IOptionsMonitor<VoiceProfileOptions>>();
            A.CallTo(() => options.CurrentValue).Returns(new VoiceProfileOptions
            {
                AudioClipBasePath = tempDir,
            });
            var service = new SpeakerProfileDeletionService(
                profileStore,
                new AudioClipService(options, NullLogger<AudioClipService>.Instance),
                NullLogger<SpeakerProfileDeletionService>.Instance);

            await service.ReconcileOrphanedClipsAsync(CancellationToken.None);

            Assert.False(File.Exists(Path.Combine(profileDir, "orphan.wav")));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    private static SpeakerProfileDeletionService CreateService(ISpeakerProfileStore profileStore)
    {
        var options = A.Fake<IOptionsMonitor<VoiceProfileOptions>>();
        A.CallTo(() => options.CurrentValue).Returns(new VoiceProfileOptions
        {
            AudioClipBasePath = Path.Combine(Path.GetTempPath(), $"lucia-test-{Guid.NewGuid():N}"),
        });
        return new SpeakerProfileDeletionService(
            profileStore,
            new AudioClipService(options, NullLogger<AudioClipService>.Instance),
            NullLogger<SpeakerProfileDeletionService>.Instance);
    }
}
