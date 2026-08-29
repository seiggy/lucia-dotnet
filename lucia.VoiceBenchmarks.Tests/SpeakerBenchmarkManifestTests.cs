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
                new { path = "speaker-a/enroll.wav", speaker_id = "speaker-a", session_id = "a-enroll", split = "enroll" },
                new { path = "speaker-a/test.wav", speaker_id = "speaker-a", session_id = "a-test", split = "test" },
                new { path = "speaker-b/enroll.wav", speaker_id = "speaker-b", session_id = "b-enroll", split = "enroll" }
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
                { "path": "enroll.wav", "speaker_id": "speaker-a", "session_id": "enroll-session", "split": "enroll" },
                { "path": "test.wav", "speaker_id": "speaker-a", "session_id": "test-session", "split": "test" }
              ]
            }
            """);

        var errors = SpeakerBenchmarkManifest.Load(manifestPath).Validate();

        Assert.Contains(errors, error => error.Contains("two speakers", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_RejectsPathUsedForEnrollmentAndTest()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"voice-benchmark-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        var manifestPath = Path.Combine(tempDirectory, "manifest.json");
        File.WriteAllText(
            manifestPath,
            """
            {
              "clips": [
                { "path": "a.wav", "speaker_id": "speaker-a", "session_id": "a-enroll", "split": "enroll" },
                { "path": "a.wav", "speaker_id": "speaker-a", "session_id": "a-test", "split": "test" },
                { "path": "b-enroll.wav", "speaker_id": "speaker-b", "session_id": "b-enroll", "split": "enroll" },
                { "path": "b-test.wav", "speaker_id": "speaker-b", "session_id": "b-test", "split": "test" }
              ]
            }
            """);

        var errors = SpeakerBenchmarkManifest.Load(manifestPath).Validate();

        Assert.Contains(errors, error => error.Contains("both enrollment and test", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_RejectsEnrollmentAndTestFromSameRecordingSession()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"voice-benchmark-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        var manifestPath = Path.Combine(tempDirectory, "manifest.json");
        File.WriteAllText(
            manifestPath,
            """
            {
              "clips": [
                { "path": "a-enroll.wav", "speaker_id": "speaker-a", "session_id": "a-session", "split": "enroll" },
                { "path": "a-test.wav", "speaker_id": "speaker-a", "session_id": "a-session", "split": "test" },
                { "path": "b-enroll.wav", "speaker_id": "speaker-b", "session_id": "b-enroll", "split": "enroll" },
                { "path": "b-test.wav", "speaker_id": "speaker-b", "session_id": "b-test", "split": "test" }
              ]
            }
            """);

        var errors = SpeakerBenchmarkManifest.Load(manifestPath).Validate();

        Assert.Contains(errors, error => error.Contains("recording session", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateContentHashes_RejectsRenamedCopyAcrossSplits()
    {
        var clips = new[]
        {
            new BenchmarkClipProvenance("enroll.wav", "speaker-a", "enroll-session", "enroll", "same-hash"),
            new BenchmarkClipProvenance("renamed.wav", "speaker-a", "test-session", "test", "same-hash"),
        };

        var errors = SpeakerBenchmarkManifest.ValidateContentHashes(clips);

        Assert.Contains(errors, error => error.Contains("both enrollment and test", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateContentHashes_RejectsDuplicateWithinTestSplit()
    {
        var clips = new[]
        {
            new BenchmarkClipProvenance("test.wav", "speaker-a", "test-session", "test", "same-hash"),
            new BenchmarkClipProvenance("renamed.wav", "speaker-a", "test-session", "test", "same-hash"),
        };

        var errors = SpeakerBenchmarkManifest.ValidateContentHashes(clips);

        Assert.Contains(errors, error => error.Contains("more than once", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_UsesHostFilesystemPathCaseSemantics()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"voice-benchmark-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        var manifestPath = Path.Combine(tempDirectory, "manifest.json");
        File.WriteAllText(
            manifestPath,
            """
            {
              "clips": [
                { "path": "A.wav", "speaker_id": "a", "session_id": "a-enroll", "split": "enroll" },
                { "path": "a.wav", "speaker_id": "a", "session_id": "a-test", "split": "test" },
                { "path": "b-enroll.wav", "speaker_id": "b", "session_id": "b-enroll", "split": "enroll" },
                { "path": "b-test.wav", "speaker_id": "b", "session_id": "b-test", "split": "test" }
              ]
            }
            """);

        var errors = SpeakerBenchmarkManifest.Load(manifestPath).Validate();

        Assert.Equal(
            OperatingSystem.IsWindows(),
            errors.Any(error => error.Contains("both enrollment and test", StringComparison.OrdinalIgnoreCase)));
    }
}
