using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace lucia.EvalHarness.Personality;

/// <summary>
/// Runs personality eval scenarios against an LLM via <see cref="IChatClient"/>,
/// then scores each result using an LLM-as-Judge approach.
/// </summary>
public sealed class PersonalityEvalRunner
{
    private static readonly string PersonalityRewritePrompt =
        "Rephrase the following smart home assistant response in your voice. " +
        "Keep the SAME meaning \u2014 if it's an error, keep it as an error. If it's a question, keep it as a question. " +
        "If it reports success, keep the success. Preserve the original intent faithfully — do not add caveats or change the outcome. " +
        "Just change the tone and style to match your personality. Be brief.\n\n" +
        "Response to rephrase:\n";

    /// <summary>
    /// Loads personality eval scenarios from the bundled JSON data file.
    /// </summary>
    public static IReadOnlyList<PersonalityEvalScenario> LoadScenarios()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Personality", "Data", "personality-eval-scenarios.json");
        if (!File.Exists(path))
            throw new FileNotFoundException($"Personality eval scenarios not found at: {path}");

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<PersonalityEvalScenario>>(json) ?? [];
    }

    /// <summary>
    /// Loads personality profiles from the bundled JSON data file.
    /// </summary>
    public static IReadOnlyList<PersonalityProfile> LoadProfiles()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Personality", "Data", "personality-profiles.json");
        if (!File.Exists(path))
            throw new FileNotFoundException($"Personality profiles not found at: {path}");

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<PersonalityProfile>>(json) ?? [];
    }

    /// <summary>
    /// Runs all applicable (scenario × profile) combinations against a single model,
    /// scoring each with the LLM judge.
    /// </summary>
    public async Task<PersonalityEvalReport> RunAsync(
        IChatClient chatClient,
        string modelName,
        IChatClient judgeChatClient,
        string judgeModelName,
        IReadOnlyList<PersonalityEvalScenario> scenarios,
        IReadOnlyList<PersonalityProfile> profiles,
        Action<PersonalityScenarioResult>? onProgress = null,
        CancellationToken ct = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var results = new List<PersonalityScenarioResult>();
        var traceDir = Path.Combine("personality-eval-traces", $"{modelName}_{startedAt:yyyyMMdd_HHmmss}");
        var judge = new PersonalityJudge(judgeChatClient, traceDir);

        foreach (var scenario in scenarios)
        {
            var applicableProfiles = GetApplicableProfiles(scenario, profiles);

            foreach (var profile in applicableProfiles)
            {
                ct.ThrowIfCancellationRequested();
                var result = await EvaluateSingleAsync(chatClient, modelName, judge, scenario, profile, ct);
                results.Add(result);
                onProgress?.Invoke(result);
            }
        }

        return new PersonalityEvalReport
        {
            ModelName = modelName,
            JudgeModelName = judgeModelName,
            Results = results,
            StartedAt = startedAt,
            CompletedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Counts the total number of (scenario × profile) combinations that will be evaluated.
    /// </summary>
    public static int CountCombinations(
        IReadOnlyList<PersonalityEvalScenario> scenarios,
        IReadOnlyList<PersonalityProfile> profiles)
    {
        return scenarios.Sum(s =>
        {
            var profileIds = s.PersonalityProfileIds;
            if (profileIds is null or { Count: 0 })
                return profiles.Count;
            return profiles.Count(p => profileIds.Contains(p.Id));
        });
    }

    private static IReadOnlyList<PersonalityProfile> GetApplicableProfiles(
        PersonalityEvalScenario scenario,
        IReadOnlyList<PersonalityProfile> allProfiles)
    {
        if (scenario.PersonalityProfileIds is null or { Count: 0 })
            return allProfiles;

        return allProfiles
            .Where(p => scenario.PersonalityProfileIds.Contains(p.Id))
            .ToList();
    }

    private static async Task<PersonalityScenarioResult> EvaluateSingleAsync(
        IChatClient chatClient,
        string modelName,
        PersonalityJudge judge,
        PersonalityEvalScenario scenario,
        PersonalityProfile profile,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        string llmResponse;
        var userMessage = PersonalityRewritePrompt + scenario.AgentResponse;

        // Step 1: Get personality rewrite from model-under-test
        try
        {
            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, profile.Instructions),
                new(ChatRole.User, userMessage)
            };

            var response = await chatClient.GetResponseAsync(messages, cancellationToken: ct);
            llmResponse = response.Text ?? string.Empty;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            return new PersonalityScenarioResult
            {
                ScenarioId = scenario.Id,
                ScenarioDescription = scenario.Description,
                Category = scenario.Category,
                ProfileId = profile.Id,
                ProfileName = profile.Name,
                ModelName = modelName,
                Score = 0,
                LlmResponse = string.Empty,
                DurationMs = sw.ElapsedMilliseconds,
                ErrorMessage = $"Model call failed: {ex.Message}"
            };
        }

        // Step 2: Build conversation trace
        var trace = new ConversationTrace
        {
            SystemPrompt = profile.Instructions,
            UserMessage = userMessage,
            AssistantResponse = llmResponse,
            OriginalResponse = scenario.AgentResponse
        };

        // Step 3: Send trace to judge
        var judgeResult = await judge.EvaluateAsync(trace, $"{scenario.Id}_{profile.Id}", ct);
        sw.Stop();

        return new PersonalityScenarioResult
        {
            ScenarioId = scenario.Id,
            ScenarioDescription = scenario.Description,
            Category = scenario.Category,
            ProfileId = profile.Id,
            ProfileName = profile.Name,
            ModelName = modelName,
            Score = judgeResult.CombinedScore,
            LlmResponse = llmResponse,
            DurationMs = sw.ElapsedMilliseconds,
            JudgeResult = judgeResult,
            Trace = trace
        };
    }
}
