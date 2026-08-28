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
}
