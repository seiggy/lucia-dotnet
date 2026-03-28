using System.Text.Json;
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

    private readonly IChatClient _judgeChatClient;
    private readonly string? _traceDir;

    public PersonalityJudge(IChatClient judgeChatClient, string? traceOutputDir = null)
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

        try
        {
            var response = await _judgeChatClient.GetResponseAsync(messages, cancellationToken: ct);
            var text = response.Text ?? string.Empty;
            var result = ParseJudgeResponse(text);
            DumpTrace(scenarioId, formattedTrace, text, null);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            DumpTrace(scenarioId, formattedTrace, null, ex);
            return new JudgeResult
            {
                PersonalityScore = 0,
                PersonalityReason = $"Judge call failed: {ex.Message}",
                MeaningScore = 0,
                MeaningReason = $"Judge call failed: {ex.Message}"
            };
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
        catch
        {
            // Don't let trace dumping crash the eval
        }
    }

    private static JudgeResult ParseJudgeResponse(string text)
    {
        // Extract JSON from potential markdown code fences or surrounding text
        var jsonStart = text.IndexOf('{');
        var jsonEnd = text.LastIndexOf('}');

        if (jsonStart < 0 || jsonEnd < 0 || jsonEnd <= jsonStart)
        {
            return new JudgeResult
            {
                PersonalityScore = 0,
                PersonalityReason = "Judge returned unparseable response",
                MeaningScore = 0,
                MeaningReason = $"Raw response: {Truncate(text, 200)}"
            };
        }

        var json = text[jsonStart..(jsonEnd + 1)];

        try
        {
            var result = JsonSerializer.Deserialize<JudgeResult>(json);
            if (result is null)
            {
                return new JudgeResult
                {
                    PersonalityScore = 0,
                    PersonalityReason = "Judge returned null JSON",
                    MeaningScore = 0,
                    MeaningReason = "Judge returned null JSON"
                };
            }

            // Clamp scores to valid range
            result.PersonalityScore = Math.Clamp(result.PersonalityScore, 0, 5);
            result.MeaningScore = Math.Clamp(result.MeaningScore, 0, 5);
            return result;
        }
        catch (JsonException)
        {
            return new JudgeResult
            {
                PersonalityScore = 0,
                PersonalityReason = "Judge returned invalid JSON",
                MeaningScore = 0,
                MeaningReason = $"Raw JSON: {Truncate(json, 200)}"
            };
        }
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        return text.Length <= maxLength ? text : text[..maxLength] + "\u2026";
    }
}
