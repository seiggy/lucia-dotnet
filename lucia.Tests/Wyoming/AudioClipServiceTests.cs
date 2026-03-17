using lucia.Wyoming.Diarization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace lucia.Tests.Wyoming;

public sealed class AudioClipServiceTests
{
    private static readonly float[] TestAudio = CreateTestAudio(1600); // 0.1s at 16kHz
    private const int SampleRate = 16000;

    [Fact]
    public async Task SaveClipAsync_CreatesWavAndJsonFiles()
    {
        var tempDir = CreateTempDir();
        try
        {
            var svc = CreateService(tempDir, maxClips: 10);
            var clipId = await svc.SaveClipAsync("profile-1", TestAudio, SampleRate, "hello");

            var profileDir = Path.Combine(tempDir, "profile-1");
            Assert.True(File.Exists(Path.Combine(profileDir, $"{clipId}.wav")));
            Assert.True(File.Exists(Path.Combine(profileDir, $"{clipId}.json")));
        }
        finally
        {
            DeleteDir(tempDir);
        }
    }

    [Fact]
    public async Task SaveClipAsync_FifoRotation_DeletesOldest()
    {
        var tempDir = CreateTempDir();
        try
        {
            var svc = CreateService(tempDir, maxClips: 3);

            var clipIds = new List<string>();
            for (var i = 0; i < 4; i++)
            {
                clipIds.Add(await svc.SaveClipAsync("profile-1", TestAudio, SampleRate, $"clip {i}"));
                // Small delay to ensure distinct CapturedAt timestamps
                await Task.Delay(50);
            }

            var clips = svc.GetClips("profile-1");
            Assert.Equal(3, clips.Count);

            // The oldest (first) clip should have been deleted
            var profileDir = Path.Combine(tempDir, "profile-1");
            Assert.False(File.Exists(Path.Combine(profileDir, $"{clipIds[0]}.wav")));
            Assert.False(File.Exists(Path.Combine(profileDir, $"{clipIds[0]}.json")));
        }
        finally
        {
            DeleteDir(tempDir);
        }
    }

    [Fact]
    public async Task GetClips_ReturnsInChronologicalOrder()
    {
        var tempDir = CreateTempDir();
        try
        {
            var svc = CreateService(tempDir, maxClips: 10);

            var clipIds = new List<string>();
            for (var i = 0; i < 3; i++)
            {
                clipIds.Add(await svc.SaveClipAsync("profile-1", TestAudio, SampleRate, $"clip {i}"));
                await Task.Delay(50);
            }

            var clips = svc.GetClips("profile-1");
            Assert.Equal(3, clips.Count);

            // Verify chronological order
            for (var i = 1; i < clips.Count; i++)
            {
                Assert.True(clips[i].CapturedAt >= clips[i - 1].CapturedAt);
            }

            // Verify IDs match insertion order
            for (var i = 0; i < clipIds.Count; i++)
            {
                Assert.Equal(clipIds[i], clips[i].Id);
            }
        }
        finally
        {
            DeleteDir(tempDir);
        }
    }

    [Fact]
    public async Task DeleteClip_RemovesBothFiles()
    {
        var tempDir = CreateTempDir();
        try
        {
            var svc = CreateService(tempDir, maxClips: 10);
            var clipId = await svc.SaveClipAsync("profile-1", TestAudio, SampleRate, "test");

            svc.DeleteClip("profile-1", clipId);

            var profileDir = Path.Combine(tempDir, "profile-1");
            Assert.False(File.Exists(Path.Combine(profileDir, $"{clipId}.wav")));
            Assert.False(File.Exists(Path.Combine(profileDir, $"{clipId}.json")));
        }
        finally
        {
            DeleteDir(tempDir);
        }
    }

    [Fact]
    public async Task MoveClipsAsync_TransfersFilesToTarget()
    {
        var tempDir = CreateTempDir();
        try
        {
            var svc = CreateService(tempDir, maxClips: 10);

            await svc.SaveClipAsync("source", TestAudio, SampleRate, "clip-a");
            await svc.SaveClipAsync("source", TestAudio, SampleRate, "clip-b");

            await svc.MoveClipsAsync("source", "target");

            // Source should be empty (directory deleted when empty)
            var sourceDir = Path.Combine(tempDir, "source");
            Assert.False(Directory.Exists(sourceDir));

            // Target should have the clips
            var targetClips = svc.GetClips("target");
            Assert.Equal(2, targetClips.Count);

            // ProfileId should be updated in metadata
            Assert.All(targetClips, c => Assert.Equal("target", c.ProfileId));
        }
        finally
        {
            DeleteDir(tempDir);
        }
    }

    private static AudioClipService CreateService(string basePath, int maxClips)
    {
        var options = new VoiceProfileOptions
        {
            AudioClipBasePath = basePath,
            MaxClipsPerProfile = maxClips,
        };
        var monitor = new OptionsMonitorStub<VoiceProfileOptions>(options);
        return new AudioClipService(monitor, NullLogger<AudioClipService>.Instance);
    }

    private static float[] CreateTestAudio(int sampleCount)
    {
        var audio = new float[sampleCount];
        for (var i = 0; i < sampleCount; i++)
        {
            audio[i] = MathF.Sin(2 * MathF.PI * 440 * i / SampleRate);
        }

        return audio;
    }

    private static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lucia-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDir(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed class OptionsMonitorStub<T>(T currentValue) : IOptionsMonitor<T>
    {
        public T CurrentValue => currentValue;

        public T Get(string? name) => currentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
