using FakeItEasy;
using lucia.Wyoming.Diarization;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests.Wyoming;

public sealed class ProfileMergeServiceTests
{
    [Fact]
    public async Task MergeAsync_RetryAfterDeleteFailure_DoesNotDuplicateProfileData()
    {
        var source = new SpeakerProfile
        {
            Id = "source",
            Name = "Source",
            Embeddings = [[1f, 0f]],
            AverageEmbedding = [1f, 0f],
            InteractionCount = 2,
        };
        var target = new SpeakerProfile
        {
            Id = "target",
            Name = "Target",
            Embeddings = [[0f, 1f]],
            AverageEmbedding = [0f, 1f],
            InteractionCount = 3,
        };
        var store = A.Fake<ISpeakerProfileStore>();
        A.CallTo(() => store.GetAsync("source", A<CancellationToken>._)).Returns(source);
        A.CallTo(() => store.GetAsync("target", A<CancellationToken>._))
            .ReturnsLazily(() => target);
        A.CallTo(() => store.UpdateAsync(A<SpeakerProfile>._, A<CancellationToken>._))
            .Invokes(call => target = call.GetArgument<SpeakerProfile>(0));
        A.CallTo(() => store.DeleteAsync("source", A<CancellationToken>._))
            .ThrowsAsync(new IOException("transient"))
            .Once()
            .Then
            .Returns(Task.CompletedTask);
        var clipService = new AudioClipService(
            new OptionsMonitorStub<VoiceProfileOptions>(
                new VoiceProfileOptions
                {
                    AudioClipBasePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
                }),
            NullLogger<AudioClipService>.Instance);
        var deletionService = new SpeakerProfileDeletionService(
            store,
            clipService,
            NullLogger<SpeakerProfileDeletionService>.Instance);
        var service = new ProfileMergeService(
            store,
            clipService,
            deletionService,
            NullLogger<ProfileMergeService>.Instance);

        await Assert.ThrowsAsync<IOException>(
            () => service.MergeAsync("source", "target"));
        var merged = await service.MergeAsync("source", "target");

        Assert.Equal(5, merged.InteractionCount);
        Assert.Equal(2, merged.Embeddings.Length);
        Assert.Equal(["source"], merged.MergedProfileIds);
    }
}
