using System.Diagnostics;

using lucia.AgentHost.Conversation.Execution;
using lucia.AgentHost.Conversation.Models;
using lucia.AgentHost.Conversation.Templates;
using lucia.Agents.Orchestration;
using lucia.Agents.Orchestration.Models;
using lucia.Wyoming.CommandRouting;

using Microsoft.Extensions.Logging;

namespace lucia.AgentHost.Conversation;

/// <summary>
/// Main processing pipeline for the /api/conversation endpoint.
/// Attempts fast-path command parsing first; falls back to LLM orchestration when no match.
/// </summary>
public sealed partial class ConversationCommandProcessor
{
    private readonly ICommandRouter _commandRouter;
    private readonly IDirectSkillExecutor _skillExecutor;
    private readonly ResponseTemplateRenderer _templateRenderer;
    private readonly ContextReconstructor _contextReconstructor;
    private readonly ConversationTelemetry _telemetry;
    private readonly ILogger<ConversationCommandProcessor> _logger;
    private readonly IServiceProvider _serviceProvider;

    public ConversationCommandProcessor(
        ICommandRouter commandRouter,
        IDirectSkillExecutor skillExecutor,
        ResponseTemplateRenderer templateRenderer,
        ContextReconstructor contextReconstructor,
        ConversationTelemetry telemetry,
        IServiceProvider serviceProvider,
        ILogger<ConversationCommandProcessor> logger)
    {
        _commandRouter = commandRouter;
        _skillExecutor = skillExecutor;
        _templateRenderer = templateRenderer;
        _contextReconstructor = contextReconstructor;
        _telemetry = telemetry;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Process a conversation request. Returns a result indicating whether
    /// the command was handled locally or needs LLM streaming.
    /// </summary>
    public async Task<ProcessingResult> ProcessAsync(
        ConversationRequest request, CancellationToken ct = default)
    {
        using var activity = _telemetry.StartConversationActivity(request.Text);
        var sw = Stopwatch.StartNew();

        LogProcessingStart(request.Text, request.Context.DeviceArea);

        // Step 1: Try command pattern matching
        var routeResult = await _commandRouter.RouteAsync(request.Text, ct).ConfigureAwait(false);

        if (routeResult.IsMatch && routeResult.MatchedPattern is not null)
        {
            return await HandleCommandMatchAsync(request, routeResult, activity, sw, ct)
                .ConfigureAwait(false);
        }

        // Step 2: No match — fall back to LLM
        return await HandleLlmFallbackAsync(request, activity, sw, ct)
            .ConfigureAwait(false);
    }

    private async Task<ProcessingResult> HandleCommandMatchAsync(
        ConversationRequest request,
        CommandRouteResult routeResult,
        Activity? activity,
        Stopwatch sw,
        CancellationToken ct)
    {
        var pattern = routeResult.MatchedPattern!;
        activity?.SetTag("conversation.routing_path", "command_parser");
        activity?.SetTag("conversation.skill_id", pattern.SkillId);
        activity?.SetTag("conversation.action", pattern.Action);
        activity?.SetTag("conversation.confidence", routeResult.Confidence);

        LogCommandMatch(pattern.SkillId, pattern.Action, routeResult.Confidence);

        // Execute skill directly (true LLM bypass)
        var executionResult = await _skillExecutor
            .ExecuteAsync(routeResult, request.Context, ct)
            .ConfigureAwait(false);

        if (!executionResult.Success)
        {
            LogCommandExecutionFailed(pattern.SkillId, pattern.Action, executionResult.Error);
            _telemetry.RecordCommandError(pattern.SkillId, pattern.Action);

            // Fall back to LLM on skill execution failure
            return await HandleLlmFallbackAsync(request, activity, sw, ct)
                .ConfigureAwait(false);
        }

        // Render templated response
        var responseText = await _templateRenderer
            .RenderAsync(pattern.SkillId, pattern.Action, executionResult.Captures, ct)
            .ConfigureAwait(false);

        sw.Stop();
        _telemetry.RecordCommandParsed(pattern.SkillId, pattern.Action, sw.Elapsed.TotalMilliseconds);

        var commandDetail = new CommandDetail
        {
            SkillId = pattern.SkillId,
            Action = pattern.Action,
            Confidence = routeResult.Confidence,
            Captures = executionResult.Captures,
            ExecutionMs = (long)sw.Elapsed.TotalMilliseconds,
        };

        LogCommandSuccess(pattern.SkillId, pattern.Action, sw.ElapsedMilliseconds);

        return ProcessingResult.CommandHandled(
            ConversationResponse.FromCommand(responseText, commandDetail, request.Context.ConversationId));
    }

    private async Task<ProcessingResult> HandleLlmFallbackAsync(
        ConversationRequest request,
        Activity? activity,
        Stopwatch sw,
        CancellationToken ct)
    {
        activity?.SetTag("conversation.routing_path", "llm_fallback");

        LogLlmFallback(request.Text);

        var engine = _serviceProvider.GetService(typeof(LuciaEngine)) as LuciaEngine;
        if (engine is null)
        {
            _logger.LogError("LuciaEngine not available for LLM fallback");
            sw.Stop();
            return ProcessingResult.LlmFallback(
                request.Context.ConversationId,
                _contextReconstructor.Reconstruct(request));
        }

        var prompt = _contextReconstructor.Reconstruct(request);

        var result = await engine
            .ProcessRequestAsync(prompt, sessionId: request.Context.ConversationId, cancellationToken: ct)
            .ConfigureAwait(false);

        sw.Stop();
        _telemetry.RecordLlmFallback(sw.Elapsed.TotalMilliseconds);

        LogLlmComplete(sw.ElapsedMilliseconds);

        return ProcessingResult.LlmCompleted(
            ConversationResponse.FromLlm(result.Text, request.Context.ConversationId));
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Processing conversation: '{Text}' (area: {DeviceArea})")]
    private partial void LogProcessingStart(string text, string? deviceArea);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Command match: {SkillId}/{Action} (confidence: {Confidence:F2})")]
    private partial void LogCommandMatch(string skillId, string action, float confidence);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Command execution failed for {SkillId}/{Action}: {Error}")]
    private partial void LogCommandExecutionFailed(string skillId, string action, string? error);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Command executed successfully: {SkillId}/{Action} in {ElapsedMs}ms")]
    private partial void LogCommandSuccess(string skillId, string action, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "No command match for '{Text}', falling back to LLM")]
    private partial void LogLlmFallback(string text);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "LLM fallback completed in {ElapsedMs}ms")]
    private partial void LogLlmComplete(long elapsedMs);
}
