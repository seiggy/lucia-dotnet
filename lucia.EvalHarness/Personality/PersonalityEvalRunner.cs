using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace lucia.EvalHarness.Personality;

/// <summary>
/// Runs personality eval scenarios against an LLM via <see cref="IChatClient"/>.
/// Replicates the personality rewrite prompt from
/// <c>ResultAggregatorExecutor.ApplyPersonalityAsync</c> and validates
/// the response against scenario expectations and profile constraints.
/// </summary>
public sealed class PersonalityEvalRunner
{
    private static readonly string PersonalityRewritePrompt =
        "Rephrase the following smart home assistant response in your voice. " +
        "Keep the SAME meaning \u2014 if it's an error, keep it as an error. If it's a question, keep it as a question. " +
        "If it reports success, keep the success. Never refuse, never say you can't do things, never add disclaimers. " +
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
    /// Runs all applicable (scenario × profile) combinations against a single model.
    /// </summary>
    public async Task<PersonalityEvalReport> RunAsync(
        IChatClient chatClient,
        string modelName,
        IReadOnlyList<PersonalityEvalScenario> scenarios,
        IReadOnlyList<PersonalityProfile> profiles,
        Action<PersonalityScenarioResult>? onProgress = null,
        CancellationToken ct = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var results = new List<PersonalityScenarioResult>();

        foreach (var scenario in scenarios)
        {
            var applicableProfiles = GetApplicableProfiles(scenario, profiles);

            foreach (var profile in applicableProfiles)
            {
                ct.ThrowIfCancellationRequested();
                var result = await EvaluateSingleAsync(chatClient, modelName, scenario, profile, ct);
                results.Add(result);
                onProgress?.Invoke(result);
            }
        }

        return new PersonalityEvalReport
        {
            ModelName = modelName,
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
        PersonalityEvalScenario scenario,
        PersonalityProfile profile,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        string llmResponse;

        try
        {
            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, profile.Instructions),
                new(ChatRole.User, PersonalityRewritePrompt + scenario.AgentResponse)
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
                Passed = false,
                FailedChecks = [$"LLM call failed: {ex.Message}"],
                LlmResponse = string.Empty,
                DurationMs = sw.ElapsedMilliseconds
            };
        }

        sw.Stop();
        var failedChecks = ValidateResponse(llmResponse, scenario, profile);

        return new PersonalityScenarioResult
        {
            ScenarioId = scenario.Id,
            ScenarioDescription = scenario.Description,
            Category = scenario.Category,
            ProfileId = profile.Id,
            ProfileName = profile.Name,
            ModelName = modelName,
            Passed = failedChecks.Count == 0,
            FailedChecks = failedChecks,
            LlmResponse = llmResponse,
            DurationMs = sw.ElapsedMilliseconds
        };
    }

    private static List<string> ValidateResponse(
        string response,
        PersonalityEvalScenario scenario,
        PersonalityProfile profile)
    {
        var failures = new List<string>();
        var lower = response.ToLowerInvariant();

        // mustContain — catches information loss
        foreach (var term in scenario.Expectations.MustContain)
        {
            if (!lower.Contains(term.ToLowerInvariant()))
                failures.Add($"Missing required term: '{term}'");
        }

        // mustNotContain — catches refusal injection
        foreach (var term in scenario.Expectations.MustNotContain)
        {
            if (lower.Contains(term.ToLowerInvariant()))
                failures.Add($"Contains forbidden term: '{term}'");
        }

        // maxLength — catches excessive verbosity
        if (response.Length > scenario.Expectations.MaxLength)
            failures.Add($"Response too long: {response.Length} > {scenario.Expectations.MaxLength}");

        // isQuestion — catches question destruction
        if (scenario.Expectations.IsQuestion && !response.TrimEnd().EndsWith('?'))
            failures.Add("Expected a question (ending with '?') but got a statement");

        // sentimentPreserved — catches meaning inversion
        ValidateSentiment(response, scenario.Expectations.SentimentPreserved, failures);

        // Profile antiPatterns — catches character-breaking responses
        foreach (var pattern in profile.AntiPatterns)
        {
            if (lower.Contains(pattern.ToLowerInvariant()))
                failures.Add($"Contains anti-pattern: '{pattern}'");
        }

        // Profile voiceCharacteristics — at least one must appear
        if (profile.VoiceCharacteristics.Count > 0)
        {
            var hasVoice = profile.VoiceCharacteristics.Any(v =>
                lower.Contains(v.ToLowerInvariant()));
            if (!hasVoice)
                failures.Add($"No voice characteristics found (expected one of: {string.Join(", ", profile.VoiceCharacteristics)})");
        }

        return failures;
    }

    private static void ValidateSentiment(
        string response,
        string? expectedSentiment,
        List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(expectedSentiment))
            return;

        var lower = response.ToLowerInvariant();

        var negativeIndicators = new[]
        {
            "error", "couldn't", "could not", "unable", "failed",
            "unavailable", "not found", "can't", "cannot", "invalid", "out of range"
        };
        var positiveIndicators = new[]
        {
            "done", "set to", "turned on", "turned off", "activated",
            "playing", "timer set", "lit up"
        };

        var hasNegative = negativeIndicators.Any(lower.Contains);
        var hasPositive = positiveIndicators.Any(lower.Contains);

        switch (expectedSentiment.ToLowerInvariant())
        {
            case "positive" when hasNegative && !hasPositive:
                failures.Add("Sentiment should be positive but response contains only negative indicators");
                break;
            case "negative" when hasPositive && !hasNegative:
                failures.Add("Sentiment should be negative but response contains only positive indicators");
                break;
        }
    }
}
