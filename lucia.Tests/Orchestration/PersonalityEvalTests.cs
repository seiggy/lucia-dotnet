using System.Text.Json;
using System.Text.Json.Serialization;

using lucia.Agents.Abstractions;
using lucia.Agents.Orchestration;
using lucia.AgentHost.Conversation.Templates;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using OllamaSharp;

using Xunit.Abstractions;

namespace lucia.Tests.Orchestration;

/// <summary>
/// Evaluation tests for the personality response engine. Each scenario sends a canned
/// agent response through a real LLM (via Ollama) personality rewrite and validates
/// that meaning, sentiment, and key information are preserved.
///
/// Known failure modes tested:
/// <list type="bullet">
///   <item>Refusal injection — LLM converts a valid error into "I'm an AI and can't…"</item>
///   <item>Meaning inversion — "lights on" becomes "lights off"</item>
///   <item>Information loss — entity names, rooms, or values are dropped</item>
///   <item>Question destruction — clarification question becomes a statement</item>
///   <item>Excessive verbosity — simple confirmation becomes a paragraph</item>
///   <item>SSML corruption — voice tags dropped or invalid markup generated</item>
/// </list>
///
/// Requires a running Ollama instance. Skipped automatically when unavailable.
/// Filter: <c>dotnet test --filter "PersonalityEval"</c>
/// </summary>
[Trait("Category", "Eval")]
[Trait("Engine", "Personality")]
public sealed class PersonalityEvalTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private readonly List<PersonalityEvalScenario> _scenarios = [];
    private readonly List<PersonalityProfile> _profiles = [];
    private IChatClient? _chatClient;
    private bool _ollamaAvailable;

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public PersonalityEvalTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public async Task InitializeAsync()
    {
        // Load scenarios from test data
        var scenarioPath = Path.Combine(AppContext.BaseDirectory, "TestData", "personality-eval-scenarios.json");
        if (!File.Exists(scenarioPath))
        {
            throw new FileNotFoundException($"Eval scenario file not found: {scenarioPath}");
        }

        var json = await File.ReadAllTextAsync(scenarioPath);
        var scenarios = JsonSerializer.Deserialize<List<PersonalityEvalScenario>>(json, s_jsonOptions);
        if (scenarios is null || scenarios.Count == 0)
        {
            throw new InvalidOperationException("No scenarios found in personality-eval-scenarios.json");
        }

        _scenarios.AddRange(scenarios);

        // Load personality profiles
        var profilePath = Path.Combine(AppContext.BaseDirectory, "TestData", "personality-profiles.json");
        if (File.Exists(profilePath))
        {
            var profileJson = await File.ReadAllTextAsync(profilePath);
            var profiles = JsonSerializer.Deserialize<List<PersonalityProfile>>(profileJson, s_jsonOptions);
            if (profiles is not null)
            {
                _profiles.AddRange(profiles);
            }
        }

        // Resolve Ollama endpoint and model from environment or defaults
        var ollamaEndpoint = Environment.GetEnvironmentVariable("OLLAMA_ENDPOINT") ?? "http://localhost:11434";
        var ollamaModel = Environment.GetEnvironmentVariable("OLLAMA_MODEL") ?? "llama3.2";

        try
        {
            var client = new OllamaApiClient(new Uri(ollamaEndpoint), ollamaModel);

            // Probe Ollama with a trivial request to verify availability
            var probeMessages = new List<ChatMessage>
            {
                new(ChatRole.User, "Say OK")
            };
            IChatClient chatClientProbe = client;
            var probe = await chatClientProbe.GetResponseAsync(
                probeMessages,
                cancellationToken: new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token);

            if (!string.IsNullOrWhiteSpace(probe.Text))
            {
                _chatClient = client;
                _ollamaAvailable = true;
                _output.WriteLine($"Ollama available at {ollamaEndpoint} with model '{ollamaModel}'");
            }
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Ollama not available at {ollamaEndpoint}: {ex.Message}");
        }
    }

    public Task DisposeAsync()
    {
        (_chatClient as IDisposable)?.Dispose();
        return Task.CompletedTask;
    }

    // ─── Scenario data source ─────────────────────────────────────────

    public static IEnumerable<object[]> SuccessPreservationScenarios =>
        LoadScenarioIds("success-preservation");

    public static IEnumerable<object[]> ErrorPreservationScenarios =>
        LoadScenarioIds("error-preservation");

    public static IEnumerable<object[]> ClarificationPreservationScenarios =>
        LoadScenarioIds("clarification-preservation");

    public static IEnumerable<object[]> MultiAgentPreservationScenarios =>
        LoadScenarioIds("multi-agent-preservation");

    public static IEnumerable<object[]> VoiceTagScenarios =>
        LoadScenarioIds("voice-tag");

    public static IEnumerable<object[]> BrevityScenarios =>
        LoadScenarioIds("brevity");

    // ═══════════════════════════════════════════════════════════════════
    // Success Preservation
    // ═══════════════════════════════════════════════════════════════════

    [SkippableTheory]
    [MemberData(nameof(SuccessPreservationScenarios))]
    public async Task SuccessPreservation_MeaningAndDetailsPreserved(string scenarioId)
    {
        var scenario = GetScenario(scenarioId);
        var rewritten = await RunPersonalityRewriteAsync(scenario);
        AssertExpectations(scenario, rewritten);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Error Preservation (catches refusal injection)
    // ═══════════════════════════════════════════════════════════════════

    [SkippableTheory]
    [MemberData(nameof(ErrorPreservationScenarios))]
    public async Task ErrorPreservation_ErrorRemainedNotRefusal(string scenarioId)
    {
        var scenario = GetScenario(scenarioId);
        var rewritten = await RunPersonalityRewriteAsync(scenario);
        AssertExpectations(scenario, rewritten);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Clarification Preservation (catches question destruction)
    // ═══════════════════════════════════════════════════════════════════

    [SkippableTheory]
    [MemberData(nameof(ClarificationPreservationScenarios))]
    public async Task ClarificationPreservation_QuestionPreserved(string scenarioId)
    {
        var scenario = GetScenario(scenarioId);
        var rewritten = await RunPersonalityRewriteAsync(scenario);
        AssertExpectations(scenario, rewritten);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Multi-Agent Preservation
    // ═══════════════════════════════════════════════════════════════════

    [SkippableTheory]
    [MemberData(nameof(MultiAgentPreservationScenarios))]
    public async Task MultiAgentPreservation_AllAgentDetailsPreserved(string scenarioId)
    {
        var scenario = GetScenario(scenarioId);
        var rewritten = await RunPersonalityRewriteAsync(scenario);
        AssertExpectations(scenario, rewritten);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Voice Tag Scenarios
    // ═══════════════════════════════════════════════════════════════════

    [SkippableTheory]
    [MemberData(nameof(VoiceTagScenarios))]
    public async Task VoiceTag_TagBehaviorCorrect(string scenarioId)
    {
        var scenario = GetScenario(scenarioId);
        var rewritten = await RunPersonalityRewriteAsync(scenario);
        AssertExpectations(scenario, rewritten);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Brevity (catches excessive verbosity for TTS)
    // ═══════════════════════════════════════════════════════════════════

    [SkippableTheory]
    [MemberData(nameof(BrevityScenarios))]
    public async Task Brevity_ResponseStaysConcise(string scenarioId)
    {
        var scenario = GetScenario(scenarioId);
        var rewritten = await RunPersonalityRewriteAsync(scenario);
        AssertExpectations(scenario, rewritten);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Full Suite (runs all scenarios in one pass for report generation)
    // ═══════════════════════════════════════════════════════════════════

    [SkippableFact]
    public async Task AllScenarios_FullSuiteReport()
    {
        Skip.If(!_ollamaAvailable, "Ollama is not available — skipping personality eval suite.");

        var passed = 0;
        var failed = 0;
        var failures = new List<string>();

        foreach (var scenario in _scenarios)
        {
            try
            {
                var rewritten = await RunPersonalityRewriteAsync(scenario, skipCheck: false);
                AssertExpectations(scenario, rewritten, throwOnFailure: false, out var scenarioFailures);

                if (scenarioFailures.Count == 0)
                {
                    passed++;
                    _output.WriteLine($"  ✅ {scenario.Id}: PASS");
                    _output.WriteLine($"     Original: {scenario.AgentResponse}");
                    _output.WriteLine($"     Rewritten: {rewritten}");
                }
                else
                {
                    failed++;
                    var failDetail = $"  ❌ {scenario.Id}: FAIL — {string.Join("; ", scenarioFailures)}";
                    failures.Add(failDetail);
                    _output.WriteLine(failDetail);
                    _output.WriteLine($"     Original: {scenario.AgentResponse}");
                    _output.WriteLine($"     Rewritten: {rewritten}");
                }
            }
            catch (Exception ex)
            {
                failed++;
                var failDetail = $"  ❌ {scenario.Id}: ERROR — {ex.Message}";
                failures.Add(failDetail);
                _output.WriteLine(failDetail);
            }
        }

        _output.WriteLine("");
        _output.WriteLine($"═══ Personality Eval Report ═══");
        _output.WriteLine($"  Total: {_scenarios.Count}  Passed: {passed}  Failed: {failed}");
        _output.WriteLine($"  Pass rate: {(double)passed / _scenarios.Count:P0}");

        if (failures.Count > 0)
        {
            _output.WriteLine("");
            _output.WriteLine("Failures:");
            foreach (var f in failures)
            {
                _output.WriteLine(f);
            }
        }

        // The full suite is informational — individual scenario tests provide the hard assertions
        Assert.True(passed > 0, "No scenarios passed — check Ollama connectivity and model.");
    }

    // ═══════════════════════════════════════════════════════════════════
    // Personality Switching — all profiles × representative scenarios
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Representative scenario IDs covering success, error, clarification, multi-agent, and brevity.
    /// </summary>
    private static readonly string[] s_switchingScenarioIds =
    [
        "personality-success-light-on",
        "personality-error-device-not-found",
        "personality-clarification-which-room",
        "personality-multi-agent-lights-music",
        "personality-multi-agent-partial-failure",
        "personality-brevity-timer-set"
    ];

    [SkippableFact]
    [Trait("Category", "Eval")]
    public async Task PersonalitySwitching_AllProfiles_PreserveMeaning()
    {
        Skip.If(!_ollamaAvailable, "Ollama is not available — skipping personality switching eval.");
        Skip.If(_profiles.Count == 0, "No personality profiles loaded — skipping personality switching eval.");

        var passed = 0;
        var failed = 0;
        var failures = new List<string>();
        var total = 0;

        foreach (var scenarioId in s_switchingScenarioIds)
        {
            var scenario = _scenarios.FirstOrDefault(s => s.Id == scenarioId);
            if (scenario is null)
            {
                _output.WriteLine($"  ⚠️ Scenario '{scenarioId}' not found — skipping.");
                continue;
            }

            // Determine which profiles apply to this scenario
            var applicableProfileIds = scenario.PersonalityProfileIds is { Count: > 0 }
                ? scenario.PersonalityProfileIds
                : _profiles.Select(p => p.Id).ToList();

            foreach (var profile in _profiles.Where(p => applicableProfileIds.Contains(p.Id)))
            {
                total++;
                var pairLabel = $"{scenario.Id} × {profile.Id}";

                try
                {
                    var rewritten = await RunPersonalityRewriteWithProfileAsync(scenario, profile);
                    var pairFailures = new List<string>();

                    // Standard scenario expectations
                    ValidateExpectations(scenario.Expectations, rewritten, pairFailures);

                    // Profile anti-patterns
                    var lower = rewritten.ToLowerInvariant();
                    foreach (var antiPattern in profile.AntiPatterns)
                    {
                        if (lower.Contains(antiPattern.ToLowerInvariant()))
                        {
                            pairFailures.Add($"Profile anti-pattern detected: '{antiPattern}'");
                        }
                    }

                    // Voice characteristic adoption — at least one must appear
                    var hasVoice = profile.VoiceCharacteristics.Count == 0
                        || profile.VoiceCharacteristics.Any(vc =>
                            lower.Contains(vc.ToLowerInvariant()));
                    if (!hasVoice)
                    {
                        pairFailures.Add(
                            $"No voice characteristics found (expected at least one of: {string.Join(", ", profile.VoiceCharacteristics)})");
                    }

                    if (pairFailures.Count == 0)
                    {
                        passed++;
                        _output.WriteLine($"  ✅ {pairLabel}: PASS");
                    }
                    else
                    {
                        failed++;
                        var detail = $"  ❌ {pairLabel}: FAIL — {string.Join("; ", pairFailures)}";
                        failures.Add(detail);
                        _output.WriteLine(detail);
                    }

                    _output.WriteLine($"     Profile: {profile.Name}");
                    _output.WriteLine($"     Original: {scenario.AgentResponse}");
                    _output.WriteLine($"     Rewritten: {rewritten}");
                }
                catch (Exception ex)
                {
                    failed++;
                    var detail = $"  ❌ {pairLabel}: ERROR — {ex.Message}";
                    failures.Add(detail);
                    _output.WriteLine(detail);
                }
            }
        }

        _output.WriteLine("");
        _output.WriteLine($"═══ Personality Switching Report ═══");
        _output.WriteLine($"  Total: {total}  Passed: {passed}  Failed: {failed}");
        if (total > 0)
        {
            _output.WriteLine($"  Pass rate: {(double)passed / total:P0}");
        }

        if (failures.Count > 0)
        {
            _output.WriteLine("");
            _output.WriteLine("Failures:");
            foreach (var f in failures)
            {
                _output.WriteLine(f);
            }
        }

        Assert.True(passed > 0, "No personality switching pairs passed — check Ollama connectivity and model.");
    }

    // ─── Core engine ──────────────────────────────────────────────────

    private async Task<string> RunPersonalityRewriteAsync(
        PersonalityEvalScenario scenario,
        bool skipCheck = true)
    {
        if (skipCheck)
        {
            Skip.If(!_ollamaAvailable, "Ollama is not available — skipping personality eval.");
        }

        var voiceTagsEnabled = scenario.VoiceTagsEnabled ?? false;

        var voiceTagInstruction = voiceTagsEnabled
            ? "Include paralinguistic voice tags in your response for improved personality, such as [laugh], [sigh], [cough]."
            : "Do not include any markup or tags in your response. Plain text only.";

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, scenario.PersonalityPrompt),
            new(ChatRole.User,
                $"Rephrase this home automation action result in your voice. Be brief and natural. Aim for 10-15 seconds of speech.\n" +
                $"{voiceTagInstruction}\n" +
                $"Action Requested: {scenario.SkillId}/{scenario.Action}\n" +
                $"Result from System: {scenario.AgentResponse}")
        };

        var response = await _chatClient!.GetResponseAsync(messages,
            cancellationToken: new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token);

        var rewritten = response.Text?.Trim() ?? string.Empty;

        _output.WriteLine($"[{scenario.Id}] Original: {scenario.AgentResponse}");
        _output.WriteLine($"[{scenario.Id}] Rewritten: {rewritten}");

        return rewritten;
    }

    private async Task<string> RunPersonalityRewriteWithProfileAsync(
        PersonalityEvalScenario scenario,
        PersonalityProfile profile)
    {
        var voiceTagsEnabled = scenario.VoiceTagsEnabled ?? false;

        var voiceTagInstruction = voiceTagsEnabled
            ? "Include paralinguistic voice tags in your response for improved personality, such as [laugh], [sigh], [cough]."
            : "Do not include any markup or tags in your response. Plain text only.";

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, profile.Instructions),
            new(ChatRole.User,
                $"Rephrase this home automation action result in your voice. Be brief and natural. Aim for 10-15 seconds of speech.\n" +
                $"{voiceTagInstruction}\n" +
                $"Action Requested: {scenario.SkillId}/{scenario.Action}\n" +
                $"Result from System: {scenario.AgentResponse}")
        };

        var response = await _chatClient!.GetResponseAsync(messages,
            cancellationToken: new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token);

        var rewritten = response.Text?.Trim() ?? string.Empty;

        _output.WriteLine($"[{scenario.Id}×{profile.Id}] Original: {scenario.AgentResponse}");
        _output.WriteLine($"[{scenario.Id}×{profile.Id}] Rewritten: {rewritten}");

        return rewritten;
    }

    // ─── Assertion helpers ────────────────────────────────────────────

    private void AssertExpectations(
        PersonalityEvalScenario scenario,
        string rewritten)
    {
        AssertExpectations(scenario, rewritten, throwOnFailure: true, out _);
    }

    private void AssertExpectations(
        PersonalityEvalScenario scenario,
        string rewritten,
        bool throwOnFailure,
        out List<string> failures)
    {
        failures = [];
        var expectations = scenario.Expectations;
        var lower = rewritten.ToLowerInvariant();

        // mustContain — case-insensitive substring checks
        foreach (var required in expectations.MustContain)
        {
            if (!lower.Contains(required.ToLowerInvariant()))
            {
                failures.Add($"Missing required content '{required}'");
            }
        }

        // mustNotContain — refusal injection detection
        foreach (var forbidden in expectations.MustNotContain)
        {
            if (lower.Contains(forbidden.ToLowerInvariant()))
            {
                failures.Add($"Contains forbidden content '{forbidden}'");
            }
        }

        // maxLength — verbosity check
        if (expectations.MaxLength > 0 && rewritten.Length > expectations.MaxLength)
        {
            failures.Add($"Response too long: {rewritten.Length} > {expectations.MaxLength}");
        }

        // isQuestion — question preservation check
        if (expectations.IsQuestion && !rewritten.TrimEnd().EndsWith('?'))
        {
            failures.Add("Expected a question (ending with '?') but response is a statement");
        }

        // sentimentPreserved — basic positive/negative/neutral check
        if (!string.IsNullOrWhiteSpace(expectations.SentimentPreserved))
        {
            var sentimentOk = expectations.SentimentPreserved.ToLowerInvariant() switch
            {
                "positive" => !ContainsNegativeSentiment(lower),
                "negative" => !ContainsFalsePositiveSentiment(lower),
                _ => true // "neutral" and "mixed" pass without additional checks
            };

            if (!sentimentOk)
            {
                failures.Add($"Sentiment not preserved: expected '{expectations.SentimentPreserved}'");
            }
        }

        if (throwOnFailure && failures.Count > 0)
        {
            var message = $"Scenario '{scenario.Id}' failed:\n" +
                          $"  Original: {scenario.AgentResponse}\n" +
                          $"  Rewritten: {rewritten}\n" +
                          $"  Failures: {string.Join("; ", failures)}";
            Assert.Fail(message);
        }
    }

    /// <summary>
    /// Validates scenario expectations and collects failures without throwing.
    /// Shared between the full-suite report and the personality-switching test.
    /// </summary>
    private void ValidateExpectations(
        PersonalityEvalExpectations expectations,
        string rewritten,
        List<string> failures)
    {
        var lower = rewritten.ToLowerInvariant();

        foreach (var required in expectations.MustContain)
        {
            if (!lower.Contains(required.ToLowerInvariant()))
            {
                failures.Add($"Missing required content '{required}'");
            }
        }

        foreach (var forbidden in expectations.MustNotContain)
        {
            if (lower.Contains(forbidden.ToLowerInvariant()))
            {
                failures.Add($"Contains forbidden content '{forbidden}'");
            }
        }

        if (expectations.MaxLength > 0 && rewritten.Length > expectations.MaxLength)
        {
            failures.Add($"Response too long: {rewritten.Length} > {expectations.MaxLength}");
        }

        if (expectations.IsQuestion && !rewritten.TrimEnd().EndsWith('?'))
        {
            failures.Add("Expected a question (ending with '?') but response is a statement");
        }

        if (!string.IsNullOrWhiteSpace(expectations.SentimentPreserved))
        {
            var sentimentOk = expectations.SentimentPreserved.ToLowerInvariant() switch
            {
                "positive" => !ContainsNegativeSentiment(lower),
                "negative" => !ContainsFalsePositiveSentiment(lower),
                _ => true
            };

            if (!sentimentOk)
            {
                failures.Add($"Sentiment not preserved: expected '{expectations.SentimentPreserved}'");
            }
        }
    }

    /// <summary>
    /// Detects if a positive response was inverted to contain negative sentiment.
    /// </summary>
    private static bool ContainsNegativeSentiment(string lower)
    {
        ReadOnlySpan<string> negativeIndicators =
        [
            "i can't", "i cannot", "unable to", "failed to", "error",
            "unfortunately", "i'm sorry", "i apologize", "not possible"
        ];

        foreach (var indicator in negativeIndicators)
        {
            if (lower.Contains(indicator))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Detects if a negative/error response was falsely rewritten as a success.
    /// </summary>
    private static bool ContainsFalsePositiveSentiment(string lower)
    {
        ReadOnlySpan<string> falsePositiveIndicators =
        [
            "all done", "everything is set", "successfully", "completed",
            "you're all set", "taken care of"
        ];

        foreach (var indicator in falsePositiveIndicators)
        {
            if (lower.Contains(indicator))
            {
                return true;
            }
        }

        return false;
    }

    // ─── Static data loading ──────────────────────────────────────────

    private PersonalityEvalScenario GetScenario(string scenarioId)
    {
        return _scenarios.FirstOrDefault(s => s.Id == scenarioId)
            ?? throw new InvalidOperationException($"Scenario '{scenarioId}' not found");
    }

    private static IEnumerable<object[]> LoadScenarioIds(string category)
    {
        var scenarioPath = Path.Combine(AppContext.BaseDirectory, "TestData", "personality-eval-scenarios.json");
        if (!File.Exists(scenarioPath))
        {
            return [];
        }

        var json = File.ReadAllText(scenarioPath);
        var scenarios = JsonSerializer.Deserialize<List<PersonalityEvalScenario>>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        return scenarios?
            .Where(s => string.Equals(s.Category, category, StringComparison.OrdinalIgnoreCase))
            .Select(s => new object[] { s.Id })
            .ToList() ?? [];
    }
}
