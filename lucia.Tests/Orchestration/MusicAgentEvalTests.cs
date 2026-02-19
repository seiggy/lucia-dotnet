#pragma warning disable AIEVAL001 // Microsoft.Extensions.AI.Evaluation is experimental

using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Reporting;

namespace lucia.Tests.Orchestration;

/// <summary>
/// Evaluation tests for the MusicAgent. Exercises the real <see cref="lucia.MusicAgent.MusicAgent"/>
/// code path — including <c>ChatClientAgent</c> with <c>FunctionInvokingChatClient</c> — so tools
/// are actually invoked against faked Home Assistant dependencies.
/// </summary>
[Trait("Category", "Eval")]
[Trait("Agent", "Music")]
public sealed class MusicAgentEvalTests : AgentEvalTestBase
{
    public MusicAgentEvalTests(EvalTestFixture fixture) : base(fixture) { }

    // ─── Tool Call Accuracy (via real agent execution) ─────────────────

    [Trait("Evaluator", "ToolCallAccuracy")]
    [SkippableTheory]
    [MemberData(nameof(ModelIds))]
    public async Task PlayArtist_ArtistRequest_ProducesResponse(string modelId)
    {
        var (agent, capture) = Fixture.CreateMusicAgentWithCapture(modelId);
        var reportingConfig = CreateReportingConfig();

        var (response, result) = await RunAgentAndEvaluateAsync(
            modelId,
            agent.GetAIAgent(),
            capture,
            "Play The Cure on the kitchen speaker",
            reportingConfig,
            "MusicAgent.PlayArtist_ArtistRequest");

        AssertHasTextResponse(response);
        AssertNoUnacceptableMetrics(result);
    }

    [Trait("Evaluator", "ToolCallAccuracy")]
    [SkippableTheory]
    [MemberData(nameof(ModelIds))]
    public async Task PlayAlbum_AlbumRequest_ProducesResponse(string modelId)
    {
        var (agent, capture) = Fixture.CreateMusicAgentWithCapture(modelId);
        var reportingConfig = CreateReportingConfig();

        var (response, result) = await RunAgentAndEvaluateAsync(
            modelId,
            agent.GetAIAgent(),
            capture,
            "Play the album Random Access Memories on the office speaker",
            reportingConfig,
            "MusicAgent.PlayAlbum_AlbumRequest");

        AssertHasTextResponse(response);
        AssertNoUnacceptableMetrics(result);
    }

    [Trait("Evaluator", "ToolCallAccuracy")]
    [SkippableTheory]
    [MemberData(nameof(ModelIds))]
    public async Task PlaySong_SongRequest_ProducesResponse(string modelId)
    {
        var (agent, capture) = Fixture.CreateMusicAgentWithCapture(modelId);
        var reportingConfig = CreateReportingConfig();

        var (response, result) = await RunAgentAndEvaluateAsync(
            modelId,
            agent.GetAIAgent(),
            capture,
            "Play Shivers by Ed Sheeran in the bedroom",
            reportingConfig,
            "MusicAgent.PlaySong_SongRequest");

        AssertHasTextResponse(response);
        AssertNoUnacceptableMetrics(result);
    }

    [Trait("Evaluator", "ToolCallAccuracy")]
    [SkippableTheory]
    [MemberData(nameof(ModelIds))]
    public async Task Shuffle_ShuffleRequest_ProducesResponse(string modelId)
    {
        var (agent, capture) = Fixture.CreateMusicAgentWithCapture(modelId);
        var reportingConfig = CreateReportingConfig();

        var (response, result) = await RunAgentAndEvaluateAsync(
            modelId,
            agent.GetAIAgent(),
            capture,
            "Just shuffle some music on the loft speaker",
            reportingConfig,
            "MusicAgent.Shuffle_ShuffleRequest");

        AssertHasTextResponse(response);
        AssertNoUnacceptableMetrics(result);
    }

    [Trait("Evaluator", "ToolCallAccuracy")]
    [SkippableTheory]
    [MemberData(nameof(ModelIds))]
    public async Task StopMusic_StopRequest_ProducesResponse(string modelId)
    {
        var (agent, capture) = Fixture.CreateMusicAgentWithCapture(modelId);
        var reportingConfig = CreateReportingConfig();

        var (response, result) = await RunAgentAndEvaluateAsync(
            modelId,
            agent.GetAIAgent(),
            capture,
            "Stop the music in the kitchen",
            reportingConfig,
            "MusicAgent.StopMusic_StopRequest");

        AssertHasTextResponse(response);
        AssertNoUnacceptableMetrics(result);
    }

    // ─── Intent Resolution ─────────────────────────────────────────────

    [Trait("Evaluator", "IntentResolution")]
    [SkippableTheory]
    [MemberData(nameof(ModelIds))]
    public async Task PlayGenre_GenreRequest_ProducesResponse(string modelId)
    {
        var (agent, capture) = Fixture.CreateMusicAgentWithCapture(modelId);
        var reportingConfig = CreateReportingConfig();

        var (response, result) = await RunAgentAndEvaluateAsync(
            modelId,
            agent.GetAIAgent(),
            capture,
            "Play some relaxed jazz on the Satellite1 kitchen",
            reportingConfig,
            "MusicAgent.PlayGenre_GenreRequest");

        AssertHasTextResponse(response);
        AssertNoUnacceptableMetrics(result);
    }

    // ─── Task Adherence ────────────────────────────────────────────────

    [Trait("Evaluator", "TaskAdherence")]
    [SkippableTheory]
    [MemberData(nameof(ModelIds))]
    public async Task OutOfDomain_LightRequest_StaysInDomain(string modelId)
    {
        var (agent, capture) = Fixture.CreateMusicAgentWithCapture(modelId);
        var reportingConfig = CreateReportingConfig();

        var (response, _) = await RunAgentAndEvaluateAsync(
            modelId,
            agent.GetAIAgent(),
            capture,
            "Turn on the living room lights",
            reportingConfig,
            "MusicAgent.OutOfDomain_LightRequest");

        // Agent should have a text response politely declining the out-of-domain request
        AssertHasTextResponse(response);
    }
}
