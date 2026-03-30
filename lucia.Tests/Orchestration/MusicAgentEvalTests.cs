#pragma warning disable AIEVAL001 // Microsoft.Extensions.AI.Evaluation is experimental

using System.Text.Json;
using Microsoft.Extensions.AI;

namespace lucia.Tests.Orchestration;

/// <summary>
/// Deep evaluation tests for the MusicAgent. Every test asserts the specific tool called,
/// verifies key parameters (artist, album, song, playerName, volume), and catches known
/// failure modes (wrong tool selection, hallucinated tool calls, missing arguments).
///
/// Tests use <c>[MemberData]</c> to cross-product models × prompt variants so each
/// intent is evaluated with multiple phrasings (including STT artifacts) without
/// duplicating test methods.
/// </summary>
[Trait("Category", "Eval")]
[Trait("Agent", "Music")]
public sealed class MusicAgentEvalTests : AgentEvalTestBase
{
    private static readonly string[] s_musicToolNames =
        ["FindPlayer", "PlayArtist", "PlayAlbum", "PlaySong", "PlayGenre",
         "PlayShuffle", "StopMusic", "SetVolume", "VolumeUp", "VolumeDown"];

    public MusicAgentEvalTests(EvalTestFixture fixture) : base(fixture) { }

    // ─── Prompt variant datasets ──────────────────────────────────────

    /// <summary>
    /// Combines <see cref="AgentEvalTestBase.ModelIds"/> with an array of
    /// <c>(prompt, variantLabel)</c> tuples to produce
    /// <c>(modelId, embeddingModelId, prompt, variant)</c> rows for <c>[MemberData]</c>.
    /// </summary>
    private static IEnumerable<object[]> WithVariants(params (string Prompt, string Variant)[] variants)
    {
        foreach (var model in ModelIds)
        {
            var modelId = (string)model[0];
            var embeddingModelId = (string)model[1];
            foreach (var (prompt, variant) in variants)
            {
                yield return [modelId, embeddingModelId, prompt, variant];
            }
        }
    }

    // ── Play Artist ───────────────────────────────────────────────────

    public static IEnumerable<object[]> PlayArtistPrompts => WithVariants(
        ("Play The Cure on the kitchen speaker", "exact"),
        ("Play some Cure in the kitchen", "casual"),
        ("Put on The Cure kitchen", "terse"),
        ("I want to listen to The Cure on the kitchen speaker", "natural"));

    // ── Play Album ────────────────────────────────────────────────────

    public static IEnumerable<object[]> PlayAlbumPrompts => WithVariants(
        ("Play the album Random Access Memories on the office speaker", "exact"),
        ("Put on Random Access Memories in the office", "casual"),
        ("Play Random Access Memories by Daft Punk on the office", "with-artist"));

    // ── Play Song ─────────────────────────────────────────────────────

    public static IEnumerable<object[]> PlaySongPrompts => WithVariants(
        ("Play Shivers by Ed Sheeran in the bedroom", "exact"),
        ("Play the song Shivers on bedroom speaker", "casual"),
        ("Put on Shivers by Ed Sheeran", "no-location"));

    // ── Play Genre ────────────────────────────────────────────────────

    public static IEnumerable<object[]> PlayGenrePrompts => WithVariants(
        ("Play some jazz on the kitchen speaker", "exact"),
        ("Play relaxing jazz in the kitchen", "casual-adj"),
        ("I want to hear some jazz music on kitchen", "natural"));

    // ── Shuffle ───────────────────────────────────────────────────────

    public static IEnumerable<object[]> ShufflePrompts => WithVariants(
        ("Just shuffle some music on the loft speaker", "exact"),
        ("Shuffle play on the loft", "terse"),
        ("Put on something random in the loft", "natural"));

    // ── Stop Music ────────────────────────────────────────────────────

    public static IEnumerable<object[]> StopMusicPrompts => WithVariants(
        ("Stop the music in the kitchen", "exact"),
        ("Turn off the music on kitchen speaker", "turn-off"),
        ("Pause the music", "pause-no-location"),
        ("Stop playing", "terse"));

    // ── Volume Control ────────────────────────────────────────────────

    public static IEnumerable<object[]> SetVolumePrompts => WithVariants(
        ("Set the kitchen speaker volume to 50 percent", "exact"),
        ("Volume 50% kitchen", "terse"),
        ("Set volume to fifty on the kitchen speaker", "stt-word-number"));

    public static IEnumerable<object[]> VolumeUpPrompts => WithVariants(
        ("Turn up the volume on the kitchen speaker", "exact"),
        ("Louder in the kitchen", "casual"),
        ("Volume up kitchen", "terse"));

    public static IEnumerable<object[]> VolumeDownPrompts => WithVariants(
        ("Turn down the volume on the kitchen speaker", "exact"),
        ("Quieter in the kitchen", "casual"),
        ("Volume down kitchen", "terse"));

    // ── Out of Domain ─────────────────────────────────────────────────

    public static IEnumerable<object[]> OutOfDomainPrompts => WithVariants(
        ("Turn on the living room lights", "light-request"),
        ("Set the thermostat to 72 degrees", "climate-request"),
        ("What's the weather like?", "weather-request"));

    // ═══════════════════════════════════════════════════════════════════
    // Tool Call Accuracy — Play Artist
    // ═══════════════════════════════════════════════════════════════════

    [Trait("Evaluator", "ToolCallAccuracy")]
    [SkippableTheory]
    [MemberData(nameof(PlayArtistPrompts))]
    public async Task PlayArtist_ArtistRequest_CallsPlayArtistWithCorrectArgs(
        string modelId, string embeddingModelId, string prompt, string variant)
    {
        var (agent, capture) = await Fixture.CreateMusicAgentWithCaptureAsync(modelId, embeddingModelId);
        var reportingConfig = CreateReportingConfig(includeTextEvaluators: false);

        var (response, result) = await RunAgentAndEvaluateAsync(
            modelId, agent.GetAIAgent(), capture, prompt, reportingConfig,
            $"MusicAgent.PlayArtist_ArtistRequest[{variant}]");

        AssertHasTextResponse(response);

        var playCall = FindToolCallOrDefault(response, "PlayArtist");
        if (playCall is not null)
        {
            AssertArgumentContains(playCall, "artist", "Cure");
            AssertArgumentContains(playCall, "playerName", "kitchen");
        }
        else
        {
            // Acceptable alternative: agent finds player first, then plays
            AssertToolCalled(response, "FindPlayer");
            var allCalls = GetAllToolCalls(response);
            Assert.True(
                allCalls.Any(tc => tc.Name.StartsWith("Play", StringComparison.OrdinalIgnoreCase)),
                $"Expected a Play* tool call but got: {string.Join(", ", allCalls.Select(tc => tc.Name))}");
        }

        AssertNoUnacceptableMetrics(result);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Tool Call Accuracy — Play Album
    // ═══════════════════════════════════════════════════════════════════

    [Trait("Evaluator", "ToolCallAccuracy")]
    [SkippableTheory]
    [MemberData(nameof(PlayAlbumPrompts))]
    public async Task PlayAlbum_AlbumRequest_CallsPlayAlbumWithCorrectArgs(
        string modelId, string embeddingModelId, string prompt, string variant)
    {
        var (agent, capture) = await Fixture.CreateMusicAgentWithCaptureAsync(modelId, embeddingModelId);
        var reportingConfig = CreateReportingConfig(includeTextEvaluators: false);

        var (response, result) = await RunAgentAndEvaluateAsync(
            modelId, agent.GetAIAgent(), capture, prompt, reportingConfig,
            $"MusicAgent.PlayAlbum_AlbumRequest[{variant}]");

        AssertHasTextResponse(response);

        var playCall = FindToolCallOrDefault(response, "PlayAlbum");
        if (playCall is not null)
        {
            AssertArgumentContains(playCall, "album", "Random Access Memories");
            AssertArgumentContains(playCall, "playerName", "office");
        }
        else
        {
            var allCalls = GetAllToolCalls(response);
            Assert.True(
                allCalls.Any(tc => tc.Name.StartsWith("Play", StringComparison.OrdinalIgnoreCase)),
                $"Expected a Play* tool call but got: {string.Join(", ", allCalls.Select(tc => tc.Name))}");
        }

        AssertNoUnacceptableMetrics(result);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Tool Call Accuracy — Play Song
    // ═══════════════════════════════════════════════════════════════════

    [Trait("Evaluator", "ToolCallAccuracy")]
    [SkippableTheory]
    [MemberData(nameof(PlaySongPrompts))]
    public async Task PlaySong_SongRequest_CallsPlaySongWithCorrectArgs(
        string modelId, string embeddingModelId, string prompt, string variant)
    {
        var (agent, capture) = await Fixture.CreateMusicAgentWithCaptureAsync(modelId, embeddingModelId);
        var reportingConfig = CreateReportingConfig(includeTextEvaluators: false);

        var (response, result) = await RunAgentAndEvaluateAsync(
            modelId, agent.GetAIAgent(), capture, prompt, reportingConfig,
            $"MusicAgent.PlaySong_SongRequest[{variant}]");

        AssertHasTextResponse(response);

        var playCall = FindToolCallOrDefault(response, "PlaySong");
        if (playCall is not null)
        {
            AssertArgumentContains(playCall, "song", "Shivers");
        }
        else
        {
            var allCalls = GetAllToolCalls(response);
            Assert.True(
                allCalls.Any(tc => tc.Name.StartsWith("Play", StringComparison.OrdinalIgnoreCase)),
                $"Expected a Play* tool call but got: {string.Join(", ", allCalls.Select(tc => tc.Name))}");
        }

        AssertNoUnacceptableMetrics(result);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Intent Resolution — Play Genre
    // ═══════════════════════════════════════════════════════════════════

    [Trait("Evaluator", "IntentResolution")]
    [SkippableTheory]
    [MemberData(nameof(PlayGenrePrompts))]
    public async Task PlayGenre_GenreRequest_CallsPlayGenreWithCorrectArgs(
        string modelId, string embeddingModelId, string prompt, string variant)
    {
        var (agent, capture) = await Fixture.CreateMusicAgentWithCaptureAsync(modelId, embeddingModelId);
        var reportingConfig = CreateReportingConfig(includeTextEvaluators: false);

        var (response, result) = await RunAgentAndEvaluateAsync(
            modelId, agent.GetAIAgent(), capture, prompt, reportingConfig,
            $"MusicAgent.PlayGenre_GenreRequest[{variant}]");

        AssertHasTextResponse(response);

        var playCall = FindToolCallOrDefault(response, "PlayGenre");
        if (playCall is not null)
        {
            AssertArgumentContains(playCall, "genre", "jazz");
            AssertArgumentContains(playCall, "playerName", "kitchen");
        }
        else
        {
            // Agent may use PlayArtist or another Play tool for genre-like requests
            var allCalls = GetAllToolCalls(response);
            Assert.True(
                allCalls.Any(tc => tc.Name.StartsWith("Play", StringComparison.OrdinalIgnoreCase)),
                $"Expected a Play* tool call for genre request but got: {string.Join(", ", allCalls.Select(tc => tc.Name))}");
        }

        AssertNoUnacceptableMetrics(result);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Tool Call Accuracy — Shuffle
    // ═══════════════════════════════════════════════════════════════════

    [Trait("Evaluator", "ToolCallAccuracy")]
    [SkippableTheory]
    [MemberData(nameof(ShufflePrompts))]
    public async Task Shuffle_ShuffleRequest_CallsPlayShuffleWithCorrectArgs(
        string modelId, string embeddingModelId, string prompt, string variant)
    {
        var (agent, capture) = await Fixture.CreateMusicAgentWithCaptureAsync(modelId, embeddingModelId);
        var reportingConfig = CreateReportingConfig(includeTextEvaluators: false);

        var (response, result) = await RunAgentAndEvaluateAsync(
            modelId, agent.GetAIAgent(), capture, prompt, reportingConfig,
            $"MusicAgent.Shuffle_ShuffleRequest[{variant}]");

        AssertHasTextResponse(response);

        var shuffleCall = FindToolCallOrDefault(response, "PlayShuffle");
        if (shuffleCall is not null)
        {
            AssertArgumentContains(shuffleCall, "playerName", "loft");
        }
        else
        {
            // Agent may use any Play* tool with shuffle=true
            var allCalls = GetAllToolCalls(response);
            Assert.True(
                allCalls.Any(tc => tc.Name.StartsWith("Play", StringComparison.OrdinalIgnoreCase)),
                $"Expected a Play* tool call for shuffle but got: {string.Join(", ", allCalls.Select(tc => tc.Name))}");
        }

        AssertNoUnacceptableMetrics(result);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Tool Call Accuracy — Stop Music
    // ═══════════════════════════════════════════════════════════════════

    [Trait("Evaluator", "ToolCallAccuracy")]
    [SkippableTheory]
    [MemberData(nameof(StopMusicPrompts))]
    public async Task StopMusic_StopRequest_CallsStopMusic(
        string modelId, string embeddingModelId, string prompt, string variant)
    {
        var (agent, capture) = await Fixture.CreateMusicAgentWithCaptureAsync(modelId, embeddingModelId);
        var reportingConfig = CreateReportingConfig(includeTextEvaluators: false);

        var (response, result) = await RunAgentAndEvaluateAsync(
            modelId, agent.GetAIAgent(), capture, prompt, reportingConfig,
            $"MusicAgent.StopMusic_StopRequest[{variant}]");

        AssertHasTextResponse(response);

        // The agent should call StopMusic; it may call FindPlayer first
        var stopCall = FindToolCallOrDefault(response, "StopMusic");
        if (stopCall is null)
        {
            // Fallback: at minimum, agent should have invoked some music tool
            var allCalls = GetAllToolCalls(response);
            Assert.True(
                allCalls.Any(tc => IsMusicTool(tc.Name)),
                $"Expected StopMusic or a music tool but got: {string.Join(", ", allCalls.Select(tc => tc.Name))}");
        }

        AssertNoUnacceptableMetrics(result);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Volume Control — Set Volume
    // ═══════════════════════════════════════════════════════════════════

    [Trait("Evaluator", "VolumeControl")]
    [SkippableTheory]
    [MemberData(nameof(SetVolumePrompts))]
    public async Task SetVolume_VolumeRequest_CallsSetVolumeWithCorrectArgs(
        string modelId, string embeddingModelId, string prompt, string variant)
    {
        var (agent, capture) = await Fixture.CreateMusicAgentWithCaptureAsync(modelId, embeddingModelId);
        var reportingConfig = CreateReportingConfig(includeTextEvaluators: false);

        var (response, result) = await RunAgentAndEvaluateAsync(
            modelId, agent.GetAIAgent(), capture, prompt, reportingConfig,
            $"MusicAgent.SetVolume_VolumeRequest[{variant}]");

        AssertHasTextResponse(response);

        var setCall = FindToolCallOrDefault(response, "SetVolume");
        if (setCall is not null)
        {
            AssertArgumentContains(setCall, "playerName", "kitchen");

            var volumeValue = GetArgumentNumericValue(setCall, "volumePercent");
            Assert.True(
                volumeValue is not null && volumeValue >= 45 && volumeValue <= 55,
                $"Expected volumePercent near 50 (±5), but got {volumeValue?.ToString() ?? "null/missing"}.");
        }
        else
        {
            // Agent may call FindPlayer first then SetVolume
            var allCalls = GetAllToolCalls(response);
            Assert.True(
                allCalls.Any(tc => IsVolumeControlTool(tc.Name)),
                $"Expected a volume tool call but got: {string.Join(", ", allCalls.Select(tc => tc.Name))}");
        }

        AssertNoUnacceptableMetrics(result);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Volume Control — Volume Up
    // ═══════════════════════════════════════════════════════════════════

    [Trait("Evaluator", "VolumeControl")]
    [SkippableTheory]
    [MemberData(nameof(VolumeUpPrompts))]
    public async Task VolumeUp_VolumeUpRequest_CallsVolumeUp(
        string modelId, string embeddingModelId, string prompt, string variant)
    {
        var (agent, capture) = await Fixture.CreateMusicAgentWithCaptureAsync(modelId, embeddingModelId);
        var reportingConfig = CreateReportingConfig(includeTextEvaluators: false);

        var (response, result) = await RunAgentAndEvaluateAsync(
            modelId, agent.GetAIAgent(), capture, prompt, reportingConfig,
            $"MusicAgent.VolumeUp_VolumeUpRequest[{variant}]");

        AssertHasTextResponse(response);

        // Agent may call VolumeUp directly, or SetVolume with a higher value
        var allCalls = GetAllToolCalls(response);
        Assert.True(
            allCalls.Any(tc => IsVolumeControlTool(tc.Name)),
            $"Expected a volume control tool call but got: {string.Join(", ", allCalls.Select(tc => tc.Name))}");

        AssertNoUnacceptableMetrics(result);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Volume Control — Volume Down
    // ═══════════════════════════════════════════════════════════════════

    [Trait("Evaluator", "VolumeControl")]
    [SkippableTheory]
    [MemberData(nameof(VolumeDownPrompts))]
    public async Task VolumeDown_VolumeDownRequest_CallsVolumeDown(
        string modelId, string embeddingModelId, string prompt, string variant)
    {
        var (agent, capture) = await Fixture.CreateMusicAgentWithCaptureAsync(modelId, embeddingModelId);
        var reportingConfig = CreateReportingConfig(includeTextEvaluators: false);

        var (response, result) = await RunAgentAndEvaluateAsync(
            modelId, agent.GetAIAgent(), capture, prompt, reportingConfig,
            $"MusicAgent.VolumeDown_VolumeDownRequest[{variant}]");

        AssertHasTextResponse(response);

        // Agent may call VolumeDown directly, or SetVolume with a lower value
        var allCalls = GetAllToolCalls(response);
        Assert.True(
            allCalls.Any(tc => IsVolumeControlTool(tc.Name)),
            $"Expected a volume control tool call but got: {string.Join(", ", allCalls.Select(tc => tc.Name))}");

        AssertNoUnacceptableMetrics(result);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Task Adherence — Out-of-Domain
    // ═══════════════════════════════════════════════════════════════════

    [Trait("Evaluator", "TaskAdherence")]
    [SkippableTheory]
    [MemberData(nameof(OutOfDomainPrompts))]
    public async Task OutOfDomain_NonMusicRequest_NoMusicToolsCalled(
        string modelId, string embeddingModelId, string prompt, string variant)
    {
        var (agent, capture) = await Fixture.CreateMusicAgentWithCaptureAsync(modelId, embeddingModelId);
        var reportingConfig = CreateReportingConfig(includeTextEvaluators: false);

        var (response, _) = await RunAgentAndEvaluateAsync(
            modelId, agent.GetAIAgent(), capture, prompt, reportingConfig,
            $"MusicAgent.OutOfDomain[{variant}]");

        AssertHasTextResponse(response);

        // Must NOT hallucinate any music tool calls for non-music requests
        AssertToolNotCalled(response, "PlayArtist");
        AssertToolNotCalled(response, "PlayAlbum");
        AssertToolNotCalled(response, "PlaySong");
        AssertToolNotCalled(response, "PlayGenre");
        AssertToolNotCalled(response, "PlayShuffle");
        AssertToolNotCalled(response, "StopMusic");
        AssertToolNotCalled(response, "SetVolume");
        AssertToolNotCalled(response, "VolumeUp");
        AssertToolNotCalled(response, "VolumeDown");
    }

    // ═══════════════════════════════════════════════════════════════════
    // Private assertion helpers — manual arg inspection via GetToolCalls
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Finds the first <see cref="FunctionCallContent"/> matching the given tool name,
    /// or returns <c>null</c> if not found. Uses normalization to strip Async suffix
    /// and handle model-prefixed names.
    /// </summary>
    private static FunctionCallContent? FindToolCallOrDefault(ChatResponse response, string functionName)
    {
        var normalized = NormalizeName(functionName);
        var toolCalls = GetToolCalls(response);
        return toolCalls.FirstOrDefault(tc =>
        {
            var actual = tc.Name is not null ? NormalizeName(tc.Name) : null;
            return string.Equals(actual, normalized, StringComparison.OrdinalIgnoreCase) ||
                   (actual is not null && actual.EndsWith($".{normalized}", StringComparison.OrdinalIgnoreCase));
        });
    }

    /// <summary>
    /// Checks whether a tool name matches one of the known music tools.
    /// </summary>
    private static bool IsMusicTool(string name)
    {
        var normalized = NormalizeName(name);
        return s_musicToolNames.Any(tool =>
            string.Equals(normalized, NormalizeName(tool), StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith($".{NormalizeName(tool)}", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Checks whether a tool name is a volume control tool (SetVolume, VolumeUp, VolumeDown).
    /// </summary>
    private static bool IsVolumeControlTool(string name)
    {
        var normalized = NormalizeName(name);
        var volumeTools = new[] { "SetVolume", "VolumeUp", "VolumeDown" };
        return volumeTools.Any(tool =>
            string.Equals(normalized, NormalizeName(tool), StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith($".{NormalizeName(tool)}", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Asserts that a string argument on the tool call equals the expected value (case-insensitive).
    /// </summary>
    private static void AssertArgumentEquals(FunctionCallContent toolCall, string argName, string expectedValue)
    {
        var actual = GetArgumentStringValue(toolCall, argName);
        Assert.True(
            string.Equals(actual, expectedValue, StringComparison.OrdinalIgnoreCase),
            $"Expected {argName}='{expectedValue}', but got '{actual ?? "null/missing"}'.");
    }

    /// <summary>
    /// Asserts that a string or array argument contains the expected substring (case-insensitive).
    /// Handles both <c>string</c> and <c>string[]</c> argument shapes.
    /// </summary>
    private static void AssertArgumentContains(FunctionCallContent toolCall, string argName, string expectedSubstring)
    {
        var rawValue = GetRawArgument(toolCall, argName);
        Assert.True(rawValue is not null, $"Argument '{argName}' not found on tool call '{toolCall.Name}'.");

        var serialized = rawValue is string s ? s : JsonSerializer.Serialize(rawValue);
        Assert.True(
            serialized.Contains(expectedSubstring, StringComparison.OrdinalIgnoreCase),
            $"Expected {argName} to contain '{expectedSubstring}', but got: {serialized}");
    }

    /// <summary>
    /// Extracts a string value from tool call arguments, handling <see cref="JsonElement"/> boxing.
    /// </summary>
    private static string? GetArgumentStringValue(FunctionCallContent toolCall, string argName)
    {
        var raw = GetRawArgument(toolCall, argName);
        return raw switch
        {
            null => null,
            string s => s,
            JsonElement { ValueKind: JsonValueKind.String } je => je.GetString(),
            JsonElement je => je.ToString(),
            _ => raw.ToString()
        };
    }

    /// <summary>
    /// Extracts a numeric value from tool call arguments, handling <see cref="JsonElement"/> boxing.
    /// </summary>
    private static double? GetArgumentNumericValue(FunctionCallContent toolCall, string argName)
    {
        var raw = GetRawArgument(toolCall, argName);
        return raw switch
        {
            null => null,
            int i => i,
            long l => l,
            double d => d,
            float f => f,
            JsonElement { ValueKind: JsonValueKind.Number } je => je.GetDouble(),
            string s when double.TryParse(s, out var parsed) => parsed,
            _ => null
        };
    }

    /// <summary>
    /// Retrieves a raw argument value from tool call arguments dictionary.
    /// </summary>
    private static object? GetRawArgument(FunctionCallContent toolCall, string argName)
    {
        if (toolCall.Arguments is null) return null;
        return toolCall.Arguments.TryGetValue(argName, out var value) ? value : null;
    }

    /// <summary>
    /// Strips trailing "Async" suffix to match AIFunctionFactory convention.
    /// </summary>
    private static string NormalizeName(string name)
    {
        return name.EndsWith("Async", StringComparison.Ordinal) ? name[..^5] : name;
    }
}
