using System.Text.Json;

namespace lucia.VoiceBenchmarks.Tests;

public sealed class SpeakerBenchmarkManifestTests
{
    [Fact]
    public void Load_RejectsManifestWithoutOneEnrollAndOneTestPerSpeaker()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"voice-benchmark-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        var manifestPath = Path.Combine(tempDirectory, "manifest.json");

        var manifest = new
        {
            clips = new[]
            {
                new { path = "speaker-a/enroll.wav", speaker_id = "speaker-a", split = "enroll" },
                new { path = "speaker-a/test.wav", speaker_id = "speaker-a", split = "test" },
                new { path = "speaker-b/enroll.wav", speaker_id = "speaker-b", split = "enroll" }
            }
        };

        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest));

        var loaded = SpeakerBenchmarkManifest.Load(manifestPath);
        var errors = loaded.Validate();

        Assert.Contains(errors, error => error.Contains("speaker-b", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("one enrollment", errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveRelativePath_UsesManifestDirectory()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"voice-benchmark-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        var manifestPath = Path.Combine(tempDirectory, "manifest.json");

        var resolved = SpeakerBenchmarkManifest.ResolveRelativePath(manifestPath, "nested/clip.wav");

        Assert.Equal(Path.Combine(tempDirectory, "nested", "clip.wav"), resolved);
    }

    [Fact]
    public void Validate_RejectsSingleSpeakerBecauseEerNeedsImpostorScores()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"voice-benchmark-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        var manifestPath = Path.Combine(tempDirectory, "manifest.json");
        File.WriteAllText(
            manifestPath,
            """
            {
              "clips": [
                { "path": "enroll.wav", "speaker_id": "speaker-a", "split": "enroll" },
                { "path": "test.wav", "speaker_id": "speaker-a", "split": "test" }
              ]
            }
            """);

        var errors = SpeakerBenchmarkManifest.Load(manifestPath).Validate();

        Assert.Contains(errors, error => error.Contains("two speakers", StringComparison.OrdinalIgnoreCase));
    }
}
