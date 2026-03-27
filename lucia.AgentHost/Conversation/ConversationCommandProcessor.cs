using System.Diagnostics;
using System.Text.Json;

using lucia.AgentHost.Conversation.Execution;
using lucia.AgentHost.Conversation.Models;
using lucia.AgentHost.Conversation.Templates;
using lucia.AgentHost.Conversation.Tracing;
using lucia.Agents.CommandTracing;
using lucia.Agents.Orchestration;
using lucia.Agents.Orchestration.Models;
using lucia.Wyoming.CommandRouting;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
    private readonly IPersonalityResponseRenderer? _personalityRenderer;
    private readonly IOptionsMonitor<CommandRoutingOptions> _routingOptions;
    private readonly IOptionsMonitor<PersonalityPromptOptions> _personalityOptions;
    private readonly ContextReconstructor _contextReconstructor;
    private readonly ConversationTelemetry _telemetry;
    private readonly ICommandTraceRepository _traceRepository;
    private readonly CommandTraceChannel _traceChannel;
    private readonly ILogger<ConversationCommandProcessor> _logger;
    private readonly IServiceProvider _serviceProvider;

    public ConversationCommandProcessor(
        ICommandRouter commandRouter,
        IDirectSkillExecutor skillExecutor,
        ResponseTemplateRenderer templateRenderer,
        ContextReconstructor contextReconstructor,
        ConversationTelemetry telemetry,
        ICommandTraceRepository traceRepository,
        CommandTraceChannel traceChannel,
        IServiceProvider serviceProvider,
        ILogger<ConversationCommandProcessor> logger,
        IOptionsMonitor<CommandRoutingOptions> routingOptions,
        IOptionsMonitor<PersonalityPromptOptions> personalityOptions,
        IPersonalityResponseRenderer? personalityRenderer = null)
    {
        _commandRouter = commandRouter;
        _skillExecutor = skillExecutor;
        _templateRenderer = templateRenderer;
        _personalityRenderer = personalityRenderer;
        _routingOptions = routingOptions;
        _personalityOptions = personalityOptions;
        _contextReconstructor = contextReconstructor;
        _telemetry = telemetry;
        _traceRepository = traceRepository;
        _traceChannel = traceChannel;
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

        // Strip optional speaker identification tag from voice platform
        var (speakerId, cleanText) = SpeakerTagParser.Parse(request.Text);
        var context = speakerId is not null
            ? request.Context with { SpeakerId = speakerId }
            : request.Context;
        var cleanRequest = request with { Text = cleanText, Context = context };

        // Ensure a stable conversationId for multi-turn continuity
        var conversationId = context.ConversationId
            ?? Guid.NewGuid().ToString("N");

        // Step 1: Try command pattern matching (on clean text, without speaker tag)
        var routeResult = await _commandRouter.RouteAsync(cleanText, ct).ConfigureAwait(false);

        if (routeResult.IsMatch && routeResult.MatchedPattern is not null)
        {
            return await HandleCommandMatchAsync(request.Text, cleanRequest, routeResult, conversationId, activity, sw, ct)
                .ConfigureAwait(false);
        }

        // Step 2: No match — fall back to LLM (with clean text + speaker in context)
        return await HandleLlmFallbackAsync(request.Text, cleanRequest, routeResult, conversationId, activity, sw, ct)
            .ConfigureAwait(false);
    }

    private async Task<ProcessingResult> HandleCommandMatchAsync(
        string originalText,
        ConversationRequest request,
        CommandRouteResult routeResult,
        string conversationId,
        Activity? activity,
        Stopwatch sw,
        CancellationToken ct)
    {
        var pattern = routeResult.MatchedPattern!;
        activity?.SetTag("conversation.routing_path", "command_parser");
        activity?.SetTag("conversation.skill_id", pattern.SkillId);
        activity?.SetTag("conversation.action", pattern.Action);
        activity?.SetTag("conversation.confidence", routeResult.Confidence);
        if (request.Context.SpeakerId is not null)
            activity?.SetTag("conversation.speaker_id", request.Context.SpeakerId);

        LogCommandMatch(pattern.SkillId, pattern.Action, routeResult.Confidence);

        // Execute skill directly (true LLM bypass)
        var executionResult = await _skillExecutor
            .ExecuteAsync(routeResult, request.Context, ct)
            .ConfigureAwait(false);

        if (!executionResult.Success)
        {
            LogCommandExecutionFailed(pattern.SkillId, pattern.Action, executionResult.Error);
            _telemetry.RecordCommandError(pattern.SkillId, pattern.Action);

            // Tag bail reason for observability when the fast-path explicitly deferred
            if (executionResult.BailReason is not null)
                activity?.SetTag("fast_path_bail_reason", executionResult.BailReason);

            // Fall back to LLM on skill execution failure; single trace records both the failed execution and LLM fallback
            return await HandleLlmFallbackAsync(originalText, request, routeResult, conversationId, activity, sw, ct, executionResult)
                .ConfigureAwait(false);
        }

        // Render templated response
        var renderResult = await _templateRenderer
            .RenderWithTraceAsync(pattern.SkillId, pattern.Action, executionResult.Captures, ct)
            .ConfigureAwait(false);
        var responseText = renderResult.Text;

        // Apply personality rewriting when enabled
        var personalityOpts = _personalityOptions.CurrentValue;
        if (personalityOpts.UsePersonalityResponses)
        {
            if (_personalityRenderer is not null)
            {
                LogPersonalityBranchEntered(pattern.SkillId, pattern.Action);
                responseText = await _personalityRenderer
                    .RenderAsync(pattern.SkillId, pattern.Action, responseText, executionResult.Captures,
                        executionResult.ResponseText, ct)
                    .ConfigureAwait(false);
            }
            else
            {
                LogPersonalityRendererMissing(pattern.SkillId, pattern.Action);
            }
        }
        else
        {
            LogPersonalityBranchSkipped(pattern.SkillId, pattern.Action);
        }

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

        // Record successful trace
        await SaveCommandTraceAsync(originalText, request, routeResult, executionResult, responseText, sw, CommandTraceOutcome.CommandHandled, templateRender: renderResult.TraceData)
            .ConfigureAwait(false);

        return ProcessingResult.CommandHandled(
            ConversationResponse.FromCommand(responseText, commandDetail, conversationId));
    }

    private async Task<ProcessingResult> HandleLlmFallbackAsync(
        string originalText,
        ConversationRequest request,
        CommandRouteResult routeResult,
        string conversationId,
        Activity? activity,
        Stopwatch sw,
        CancellationToken ct,
        SkillExecutionResult? failedExecution = null)
    {
        activity?.SetTag("conversation.routing_path", "llm_fallback");

        LogLlmFallback(request.Text);

        var engine = _serviceProvider.GetService(typeof(LuciaEngine)) as LuciaEngine;
        if (engine is null)
        {
            _logger.LogError("LuciaEngine not available for LLM fallback");
            sw.Stop();

            await SaveLlmFallbackTraceAsync(originalText, request, routeResult, sw, null, CommandTraceOutcome.LlmFallback, failedExecution)
                .ConfigureAwait(false);

            return ProcessingResult.LlmFallback(
                conversationId,
                _contextReconstructor.Reconstruct(request));
        }

        var prompt = _contextReconstructor.Reconstruct(request);

        var result = await engine
            .ProcessRequestAsync(prompt, sessionId: conversationId, cancellationToken: ct)
            .ConfigureAwait(false);

        sw.Stop();
        _telemetry.RecordLlmFallback(sw.Elapsed.TotalMilliseconds);

        LogLlmComplete(sw.ElapsedMilliseconds);

        await SaveLlmFallbackTraceAsync(originalText, request, routeResult, sw, prompt, CommandTraceOutcome.LlmCompleted, failedExecution)
            .ConfigureAwait(false);

        return ProcessingResult.LlmCompleted(
            ConversationResponse.FromLlm(result.Text, conversationId, result.NeedsInput));
    }

    private async Task SaveCommandTraceAsync(
        string originalText,
        ConversationRequest request,
        CommandRouteResult routeResult,
        SkillExecutionResult? executionResult,
        string? responseText,
        Stopwatch sw,
        CommandTraceOutcome outcome,
        string? error = null,
        CommandTraceTemplateRender? templateRender = null)
    {
        try
        {
            var pattern = routeResult.MatchedPattern;
            var normalizedTranscript = routeResult.NormalizedTranscript ?? string.Empty;
            var tokens = TranscriptNormalizer.Tokenize(normalizedTranscript);

            var highlights = pattern is not null && routeResult.MatchedTemplate is not null && routeResult.CapturedValues is not null
                ? TokenHighlightBuilder.Build(normalizedTranscript, tokens, routeResult.MatchedTemplate, routeResult.CapturedValues)
                : [];

            var trace = new CommandTrace
            {
                Id = Guid.NewGuid().ToString("N"),
                Timestamp = DateTimeOffset.UtcNow,
                RawText = originalText,
                CleanText = request.Text,
                NormalizedText = normalizedTranscript,
                SpeakerId = request.Context.SpeakerId,
                RequestContext = new CommandTraceContext
                {
                    ConversationId = request.Context.ConversationId,
                    DeviceId = request.Context.DeviceId,
                    DeviceArea = request.Context.DeviceArea,
                    DeviceType = request.Context.DeviceType,
                    UserId = request.Context.UserId,
                    SpeakerId = request.Context.SpeakerId,
                    Location = request.Context.Location,
                },
                Match = new CommandTraceMatch
                {
                    IsMatch = routeResult.IsMatch,
                    Confidence = routeResult.Confidence,
                    PatternId = pattern?.Id,
                    SkillId = pattern?.SkillId,
                    Action = pattern?.Action,
                    TemplateUsed = routeResult.MatchedTemplate,
                    CapturedValues = routeResult.CapturedValues,
                    MatchDurationMs = routeResult.MatchDuration.TotalMilliseconds,
                    TokenHighlights = highlights,
                },
                Execution = executionResult is not null
                    ? new CommandTraceExecution
                    {
                        SkillId = executionResult.SkillId,
                        Action = executionResult.Action,
                        DurationMs = executionResult.ExecutionDuration.TotalMilliseconds,
                        Success = executionResult.Success,
                        Error = executionResult.Error,
                        ParametersJson = executionResult.Captures.Count > 0
                            ? JsonSerializer.Serialize(executionResult.Captures)
                            : null,
                        ResponseText = executionResult.ResponseText,
                        ToolCalls = executionResult.ToolCalls,
                    }
                    : null,
                TemplateRender = templateRender,
                Outcome = outcome,
                TotalDurationMs = sw.Elapsed.TotalMilliseconds,
                ResponseText = responseText,
                Error = error,
            };

            await _traceRepository.SaveAsync(trace).ConfigureAwait(false);
            _traceChannel.Write(trace);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save command trace");
        }
    }

    private async Task SaveLlmFallbackTraceAsync(
        string originalText,
        ConversationRequest request,
        CommandRouteResult routeResult,
        Stopwatch sw,
        string? prompt,
        CommandTraceOutcome outcome,
        SkillExecutionResult? failedExecution = null)
    {
        try
        {
            var trace = new CommandTrace
            {
                Id = Guid.NewGuid().ToString("N"),
                Timestamp = DateTimeOffset.UtcNow,
                RawText = originalText,
                CleanText = request.Text,
                NormalizedText = routeResult.NormalizedTranscript,
                SpeakerId = request.Context.SpeakerId,
                RequestContext = new CommandTraceContext
                {
                    ConversationId = request.Context.ConversationId,
                    DeviceId = request.Context.DeviceId,
                    DeviceArea = request.Context.DeviceArea,
                    DeviceType = request.Context.DeviceType,
                    UserId = request.Context.UserId,
                    SpeakerId = request.Context.SpeakerId,
                    Location = request.Context.Location,
                },
                Match = new CommandTraceMatch
                {
                    IsMatch = routeResult.IsMatch,
                    Confidence = routeResult.Confidence,
                    MatchDurationMs = routeResult.MatchDuration.TotalMilliseconds,
                },
                Execution = failedExecution is not null
                    ? new CommandTraceExecution
                    {
                        SkillId = failedExecution.SkillId,
                        Action = failedExecution.Action,
                        DurationMs = failedExecution.ExecutionDuration.TotalMilliseconds,
                        Success = false,
                        Error = failedExecution.Error,
                        ToolCalls = failedExecution.ToolCalls,
                    }
                    : null,
                LlmFallback = new CommandTraceLlmFallback
                {
                    Prompt = prompt,
                    DurationMs = sw.Elapsed.TotalMilliseconds,
                },
                Outcome = outcome,
                TotalDurationMs = sw.Elapsed.TotalMilliseconds,
            };

            await _traceRepository.SaveAsync(trace).ConfigureAwait(false);
            _traceChannel.Write(trace);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save LLM fallback command trace");
        }
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

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Personality rendering enabled for {SkillId}/{Action} — entering personality branch")]
    private partial void LogPersonalityBranchEntered(string skillId, string action);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "UsePersonalityResponses is enabled but IPersonalityResponseRenderer is not registered — using canned response for {SkillId}/{Action}")]
    private partial void LogPersonalityRendererMissing(string skillId, string action);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "UsePersonalityResponses is disabled for {SkillId}/{Action} — using canned response")]
    private partial void LogPersonalityBranchSkipped(string skillId, string action);
}
