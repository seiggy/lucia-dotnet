using FakeItEasy;
using lucia.AgentHost.Apis;
using lucia.AgentHost.Models;
using lucia.Agents.Abstractions;
using Microsoft.AspNetCore.Http;

namespace lucia.Tests.Apis;

public sealed class VoiceConfigApiTests
{
    private readonly IConfigStoreWriter _configStore = A.Fake<IConfigStoreWriter>();

    [Fact]
    public async Task UpdateVoiceConfigAsync_InvalidProvisionalThreshold_PersistsNothing()
    {
        var result = await VoiceConfigApi.UpdateVoiceConfigAsync(
            new VoiceConfigUpdateRequest
            {
                SpeakerVerificationThreshold = 0.40f,
                ProvisionalMatchThreshold = 1.5f,
                IgnoreUnknownVoices = true,
            },
            _configStore);

        var status = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, status.StatusCode);
        A.CallTo(_configStore).MustNotHaveHappened();
    }

    [Fact]
    public async Task UpdateVoiceConfigAsync_ValidThresholds_PersistsBoth()
    {
        var result = await VoiceConfigApi.UpdateVoiceConfigAsync(
            new VoiceConfigUpdateRequest
            {
                SpeakerVerificationThreshold = 0.40f,
                ProvisionalMatchThreshold = 0.30f,
            },
            _configStore);

        Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        A.CallTo(() => _configStore.SetAsync(
                "Wyoming:VoiceProfiles:SpeakerVerificationThreshold", "0.40", A<string>._, A<bool>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _configStore.SetAsync(
                "Wyoming:VoiceProfiles:ProvisionalMatchThreshold", "0.30", A<string>._, A<bool>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }
}
