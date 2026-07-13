using System.Text.Json;
using lucia.EvalHarness.Evaluation;
using Microsoft.Extensions.AI;

namespace lucia.EvalHarness.Personality;

/// <summary>
/// Uses a larger LLM (the judge model) to score a personality rewrite
/// on personality adherence and meaning preservation.
/// </summary>
public sealed class PersonalityJudge
{
    private static readonly string JudgeSystemPrompt =
        """
        You are evaluating a smart home assistant's personality engine. The personality engine takes a factual response and rewrites it in a specific personality voice.

        Score the rewritten response on two criteria (1-5 each):

        **Personality Adherence** (1-5):
        1 = No personality at all, generic/robotic
        2 = Slight hints of personality but mostly generic
        3 = Recognizable personality but inconsistent
        4 = Strong personality voice, minor lapses
        5 = Perfect character voice throughout

        **Meaning Preservation** (1-5):
        1 = Meaning completely changed or lost (e.g., success became refusal)
        2 = Major information lost or distorted
        3 = Core meaning preserved but details missing
        4 = All important details preserved with minor rewording
        5 = Perfect preservation of all factual content

        Respond in this exact JSON format:
        {"personalityScore": <1-5>, "personalityReason": "<1 sentence>", "meaningScore": <1-5>, "meaningReason": "<1 sentence>"}
        """;

    private readonly IChatClient? _judgeChatClient;
    private readonly string? _traceDir;

    public PersonalityJudge(IChatClient? judgeChatClient, string? traceOutputDir = null)
    {
        _judgeChatClient = judgeChatClient;
        _traceDir = traceOutputDir;
        if (_traceDir is not null)
            Directory.CreateDirectory(_traceDir);
    }

    /// <summary>
    /// Sends the conversation trace to the judge model and parses the scored result.
    /// </summary>
    public async Task<JudgeResult> EvaluateAsync(
        ConversationTrace trace,
        string? scenarioId = null,
        CancellationToken ct = default)
    {
        var formattedTrace = trace.Format();
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, JudgeSystemPrompt),
            new(ChatRole.User, formattedTrace)
        };

        if (_judgeChatClient is null)
        {
            return Unavailable(JudgeAvailability.NotConfigured);
        }

        try
        {
            var response = await _judgeChatClient.GetResponseAsync(messages, cancellationToken: ct);
            var text = response.Text ?? string.Empty;
            var result = ParseJudgeResponse(text);
            DumpTrace(scenarioId, formattedTrace, text, null);
            return result;
        }
        catch (Exception exception)
            when (JudgeAvailability.TryClassify(exception, ct, out var status))
        {
            DumpTrace(scenarioId, formattedTrace, null, exception);
            return Unavailable(status);
        }
    }

    private void DumpTrace(string? scenarioId, string judgeInput, string? judgeOutput, Exception? error)
    {
        if (_traceDir is null)
            return;

        var slug = scenarioId ?? $"unknown-{DateTime.UtcNow:HHmmss}";
        var path = Path.Combine(_traceDir, $"{slug}.txt");

        try
        {
            var content = $"""
                ═══ JUDGE TRACE: {slug} ═══
                Timestamp: {DateTime.UtcNow:O}

                ─── JUDGE INPUT (sent to judge model) ───
                [System Prompt]
                {JudgeSystemPrompt}

                [User Message — Conversation Trace]
                {judgeInput}

                ─── JUDGE OUTPUT ───
                {(error is not null ? $"ERROR: {error.GetType().Name}: {error.Message}" : judgeOutput ?? "(empty)")}
                ═══ END TRACE ═══
                """;
            File.WriteAllText(path, content);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Console.Error.WriteLine($"Personality judge trace could not be written: {exception.GetType().Name}");
        }
    }

    private static JudgeResult ParseJudgeResponse(string text)
    {
        // Extract JSON from potential markdown code fences or surrounding text
        var jsonStart = text.IndexOf('{');
        var jsonEnd = text.LastIndexOf('}');

        if (jsonStart < 0 || jsonEnd < 0 || jsonEnd <= jsonStart)
        {
            return Unavailable(JudgeAvailability.InvalidResponse);
        }

        var json = text[jsonStart..(jsonEnd + 1)];

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind is not JsonValueKind.Object ||
                !TryReadScore(root, "personalityScore", out var personalityScore) ||
                !TryReadScore(root, "meaningScore", out var meaningScore) ||
                !TryReadReason(root, "personalityReason", out var personalityReason) ||
                !TryReadReason(root, "meaningReason", out var meaningReason))
            {
                return Unavailable(JudgeAvailability.InvalidResponse);
            }

            return new JudgeResult
            {
                PersonalityScore = personalityScore,
                PersonalityReason = personalityReason,
                MeaningScore = meaningScore,
                MeaningReason = meaningReason
            };
        }
        catch (JsonException)
        {
            return Unavailable(JudgeAvailability.InvalidResponse);
        }
    }

    private static bool TryReadScore(JsonElement root, string propertyName, out int score)
    {
        score = 0;
        return root.TryGetProperty(propertyName, out var element) &&
            element.ValueKind is JsonValueKind.Number &&
            element.TryGetInt32(out score) &&
            score is >= 1 and <= 5;
    }

    private static bool TryReadReason(JsonElement root, string propertyName, out string reason)
    {
        reason = string.Empty;
        if (!root.TryGetProperty(propertyName, out var element) ||
            element.ValueKind is not JsonValueKind.String)
        {
            return false;
        }

        reason = element.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(reason);
    }

    private static JudgeResult Unavailable(string status) =>
        new()
        {
            Status = status,
            UnavailableReason = JudgeAvailability.Reason(status)
        };
}
