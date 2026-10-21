using System.Text;
using AgentEval.Assertions;
using AgentEval.Models;
using AgentEval.Output;
using lucia.Agents.Abstractions;
using lucia.HomeAssistant.Models;
using lucia.HomeAssistant.Services;
using lucia.Tests.TestDoubles;

namespace lucia.EvalHarness.Evaluation;

/// <summary>
/// Validates a completed scenario execution against its expected tool calls,
/// response content, and final entity state.
/// </summary>
public static class ScenarioValidator
{
    /// <summary>
    /// Sets up the FakeHomeAssistantClient with the scenario's initial entity states.
    /// When an <see cref="IEntityLocationService"/> is provided (backed by
    /// <see cref="SnapshotEntityLocationService"/>), entities are also registered
    /// there so that Find-style tools can discover them.
    /// Call this before running the agent.
    /// </summary>
    public static async Task SetupInitialStateAsync(
        IHomeAssistantClient haClient,
        TestScenario scenario,
        IEntityLocationService? locationService = null)
    {
        foreach (var (entityId, setup) in scenario.InitialState)
        {
            await haClient.SetEntityStateAsync(
                entityId,
                setup.State,
                setup.Attributes);

            // Register in the location service so entity/area search tools can find it
            if (locationService is SnapshotEntityLocationService snapshotService)
            {
                var friendlyName = setup.Attributes?.TryGetValue("friendly_name", out var fn) == true
                    ? fn?.ToString()
                    : null;

                var areaId = setup.Area is null
                    ? null
                    : snapshotService.ExactMatchArea(setup.Area)?.AreaId
                      ?? throw new InvalidOperationException(
                          $"Scenario '{scenario.Id}' assigns '{entityId}' to unknown snapshot area '{setup.Area}'.");
                snapshotService.RegisterEntity(entityId, friendlyName, areaId);
            }
        }
    }

    /// <summary>
    /// Validates the scenario execution results against expectations.
    /// </summary>
    public static async Task<ScenarioValidationResult> ValidateAsync(
        TestScenario scenario,
        IReadOnlyList<ConversationTurn> conversation,
        IHomeAssistantClient haClient,
        IReadOnlyCollection<string>? availableToolNames = null)
    {
        var issues = new List<string>();
        var successes = new List<string>();

        // 1. Validate tool call chain
        var policyResults = ValidateToolCalls(scenario, conversation, issues, successes, availableToolNames);

        // 2. Validate response content
        ValidateResponse(scenario, conversation, issues, successes);

        // 3. Validate final entity state
        await ValidateFinalStateAsync(scenario, haClient, issues, successes);

        var totalChecks = issues.Count + successes.Count;
        double? score = policyResults.Any(result => result.IsInconclusive)
            ? null
            : totalChecks > 0 ? (double)successes.Count / totalChecks * 100 : 100;

        return new ScenarioValidationResult
        {
            ScenarioId = scenario.Id,
            Passed = issues.Count == 0 && policyResults.All(result => result.Passed),
            Score = score,
            Issues = issues,
            Successes = successes,
            ToolPolicyResults = policyResults
        };
    }

    private static IReadOnlyList<AssertionResult> ValidateToolCalls(
        TestScenario scenario,
        IReadOnlyList<ConversationTurn> conversation,
        List<string> issues,
        List<string> successes,
        IReadOnlyCollection<string>? availableToolNames)
    {
        // Extract actual tool calls from the conversation in order
        var actualToolCalls = conversation
            .Where(t => t.Role == "assistant" && t.ToolCalls is { Count: > 0 })
            .SelectMany(t => t.ToolCalls!)
            .ToList();

        var policyResults = ValidateForbiddenTools(
            scenario, actualToolCalls, availableToolNames, issues, successes);

        var expected = scenario.ExpectedToolCalls;

        if (expected.Count == 0)
        {
            // Scenario expects NO tool calls
            if (actualToolCalls.Count == 0)
                successes.Add("No tool calls made (as expected)");
            else
                issues.Add($"Expected no tool calls but got {actualToolCalls.Count}: {string.Join(", ", actualToolCalls.Select(t => t.Name))}");
            return policyResults;
        }

        // Check tool count
        var requiredCount = expected.Count(static call => !call.Optional);
        if (actualToolCalls.Count < requiredCount)
        {
            issues.Add($"Expected {requiredCount} tool call(s) but only got {actualToolCalls.Count}");
        }

        // Walk through expected calls in order. An absent optional call does not
        // consume an actual call, so the next expectation sees the same one.
        var actualIndex = 0;
        for (var i = 0; i < expected.Count; i++)
        {
            var exp = expected[i];
            var actualAvailable = actualIndex < actualToolCalls.Count;

            if (exp.Optional
                && (!actualAvailable || !NamesMatch(exp, actualToolCalls[actualIndex].Name)))
            {
                continue;
            }

            if (!actualAvailable)
            {
                issues.Add($"[{i}] Expected {exp.Tool} but no more tool calls were made");
                continue;
            }

            var group = new List<ToolCallInfo> { actualToolCalls[actualIndex++] };
            if (exp.Optional)
            {
                // Models often repeat an optional lookup. Every repeat is still checked.
                while (actualIndex < actualToolCalls.Count && NamesMatch(exp, actualToolCalls[actualIndex].Name))
                {
                    group.Add(actualToolCalls[actualIndex++]);
                }
            }

            foreach (var call in group)
            {
                ValidateCall(i, exp, call, issues, successes);
            }
        }

        // Preserve strict rejection for optional-only scenarios, and allow any
        // scenario to reject calls left after its declared chain explicitly.
        if (requiredCount == 0 || scenario.RejectUnlistedToolCalls)
        {
            for (; actualIndex < actualToolCalls.Count; actualIndex++)
            {
                issues.Add($"[{actualIndex}] Unexpected tool call '{actualToolCalls[actualIndex].Name}'");
            }
        }

        return policyResults;
    }

    private static IReadOnlyList<AssertionResult> ValidateForbiddenTools(
        TestScenario scenario,
        IReadOnlyList<ToolCallInfo> actualToolCalls,
        IReadOnlyCollection<string>? availableToolNames,
        List<string> issues,
        List<string> successes)
    {
        if (scenario.ForbiddenTools.Count == 0)
        {
            return [];
        }

        var report = new ToolUsageReport();
        for (var i = 0; i < actualToolCalls.Count; i++)
        {
            // Preserve trace positions, but never pass argument values to policy diagnostics.
            report.AddCall(new ToolCallRecord
            {
                Name = NormalizeFunctionName(actualToolCalls[i].Name),
                CallId = actualToolCalls[i].CallId ?? $"call-{i}",
                Order = i + 1
            });
        }

        if (availableToolNames is not null)
        {
            report.DeclareAvailableTools(availableToolNames.Select(NormalizeFunctionName));
        }

        var results = new List<AssertionResult>();
        foreach (var tool in scenario.ForbiddenTools.Select(NormalizeFunctionName).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            using var scope = AgentEvalScope.Collecting();
            report.Should().NeverCallTool(tool, because: $"Scenario '{scenario.Id}' forbids this tool.");
            var result = scope.Results.Single();
            results.Add(result);

            if (result.Outcome == AssertionOutcome.Failed)
            {
                foreach (var call in report.GetCallsByName(tool))
                {
                    var position = call.Order - 1;
                    issues.Add($"[{position}] Forbidden tool call '{actualToolCalls[position].Name}'");
                }
            }
            else if (result.Outcome == AssertionOutcome.Passed)
            {
                successes.Add($"{result.Assertion}: offered to the model and never called");
            }
        }

        return results;
    }

    private static void ValidateCall(
        int i,
        ExpectedToolCall exp,
        ToolCallInfo actual,
        List<string> issues,
        List<string> successes)
    {
        var variants = new[] { exp }.Concat(exp.Alternatives).ToList();
        exp = variants.FirstOrDefault(candidate =>
                  string.Equals(NormalizeFunctionName(candidate.Tool), NormalizeFunctionName(actual.Name), StringComparison.OrdinalIgnoreCase) &&
                  candidate.Arguments.All(argument => MatchesArgument(GetArgument(actual, argument.Key), argument.Value)))
              ?? variants.FirstOrDefault(candidate =>
                  string.Equals(NormalizeFunctionName(candidate.Tool), NormalizeFunctionName(actual.Name), StringComparison.OrdinalIgnoreCase))
              ?? exp;

        // Check tool name — normalize away the "Async" suffix that
        // AIFunctionFactory.Create strips from method names so YAML
        // scenarios written with either form match correctly.
        var normalizedActual = NormalizeFunctionName(actual.Name);
        var normalizedExpected = NormalizeFunctionName(exp.Tool);

        if (!string.Equals(normalizedActual, normalizedExpected, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"[{i}] Expected tool '{exp.Tool}' but got '{actual.Name}'");
            return;
        }

        successes.Add($"[{i}] Correct tool: {exp.Tool}");

        // Check arguments
        foreach (var (argName, expectedValue) in exp.Arguments)
        {
            var actualValue = GetArgument(actual, argName);

            if (actualValue is null)
            {
                issues.Add($"[{i}] {exp.Tool}: missing argument '{argName}'");
                continue;
            }

            if (expectedValue == "*")
            {
                successes.Add($"[{i}] {exp.Tool}.{argName} present (any value)");
            }
            else if (expectedValue.StartsWith("contains:", StringComparison.OrdinalIgnoreCase))
            {
                var substring = expectedValue["contains:".Length..];
                if (actualValue.Contains(substring, StringComparison.OrdinalIgnoreCase))
                    successes.Add($"[{i}] {exp.Tool}.{argName} contains '{substring}'");
                else
                    issues.Add($"[{i}] {exp.Tool}.{argName}: expected to contain '{substring}' but got '{actualValue}'");
            }
            else
            {
                if (string.Equals(actualValue, expectedValue, StringComparison.OrdinalIgnoreCase))
                    successes.Add($"[{i}] {exp.Tool}.{argName} = '{expectedValue}'");
                else
                    issues.Add($"[{i}] {exp.Tool}.{argName}: expected '{expectedValue}' but got '{actualValue}'");
            }
        }
    }

    private static bool NamesMatch(ExpectedToolCall expected, string actualName) =>
        new[] { expected }.Concat(expected.Alternatives).Any(candidate =>
            string.Equals(
                NormalizeFunctionName(candidate.Tool),
                NormalizeFunctionName(actualName),
                StringComparison.OrdinalIgnoreCase));

    private static string? GetArgument(ToolCallInfo actual, string name) =>
        actual.Arguments?.FirstOrDefault(argument =>
            string.Equals(argument.Key, name, StringComparison.OrdinalIgnoreCase)).Value;

    private static bool MatchesArgument(string? actual, string expected) =>
        actual is not null && (expected == "*" ||
            (expected.StartsWith("contains:", StringComparison.OrdinalIgnoreCase)
                ? actual.Contains(expected["contains:".Length..], StringComparison.OrdinalIgnoreCase)
                : actual.Equals(expected, StringComparison.OrdinalIgnoreCase)));

    private static void ValidateResponse(
        TestScenario scenario,
        IReadOnlyList<ConversationTurn> conversation,
        List<string> issues,
        List<string> successes)
    {
        // Get the final assistant text response
        var finalResponse = NormalizeText(conversation
            .LastOrDefault(t => t.Role == "assistant" && t.Content is not null && t.ToolCalls is null)
            ?.Content ?? "");

        foreach (var mustContain in scenario.ResponseMustContain)
        {
            if (finalResponse.Contains(NormalizeText(mustContain), StringComparison.OrdinalIgnoreCase))
                successes.Add($"Response contains '{mustContain}'");
            else
                issues.Add($"Response missing expected text '{mustContain}'. Got: '{Truncate(finalResponse, 100)}'");
        }

        foreach (var mustNotContain in scenario.ResponseMustNotContain)
        {
            if (finalResponse.Contains(NormalizeText(mustNotContain), StringComparison.OrdinalIgnoreCase))
                issues.Add($"Response contains prohibited text '{mustNotContain}'");
            else
                successes.Add($"Response correctly omits '{mustNotContain}'");
        }
    }

    /// <summary>
    /// Models emit curly quotes and Unicode spaces such as U+202F between words.
    /// Fold them to ASCII so a correct answer is not marked missing.
    /// </summary>
    private static string NormalizeText(string text)
    {
        var builder = new StringBuilder(text.Length);
        var previousWasSpace = false;
        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                if (!previousWasSpace)
                {
                    builder.Append(' ');
                }

                previousWasSpace = true;
                continue;
            }

            previousWasSpace = false;
            builder.Append(character switch
            {
                '\u2018' or '\u2019' or '\u02BC' => '\'',
                '\u201C' or '\u201D' => '"',
                _ => character
            });
        }

        return builder.ToString().Trim();
    }

    private static async Task ValidateFinalStateAsync(
        TestScenario scenario,
        IHomeAssistantClient haClient,
        List<string> issues,
        List<string> successes)
    {
        foreach (var (entityId, expected) in scenario.ExpectedFinalState)
        {
            var state = await haClient.GetEntityStateAsync(entityId);
            if (state is null)
            {
                issues.Add($"Entity '{entityId}' not found after scenario");
                continue;
            }

            if (!string.Equals(state.State, expected.State, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add($"{entityId}: expected state '{expected.State}' but got '{state.State}'");
            }
            else
            {
                successes.Add($"{entityId} state = '{expected.State}'");
            }

            if (expected.Attributes is not null)
            {
                foreach (var (attrName, expectedValue) in expected.Attributes)
                {
                    if (!state.Attributes.TryGetValue(attrName, out var actualAttr))
                    {
                        issues.Add($"{entityId}.{attrName}: attribute missing");
                        continue;
                    }

                    var actualStr = actualAttr?.ToString() ?? "";
                    if (string.Equals(actualStr, expectedValue, StringComparison.OrdinalIgnoreCase))
                        successes.Add($"{entityId}.{attrName} = '{expectedValue}'");
                    else
                        issues.Add($"{entityId}.{attrName}: expected '{expectedValue}' but got '{actualStr}'");
                }
            }
        }
    }

    /// <summary>
    /// Strips a trailing "Async" suffix to match the convention used by
    /// <see cref="Microsoft.Extensions.AI.AIFunctionFactory"/> which removes
    /// Async from method names when creating tool definitions.
    /// </summary>
    private static string NormalizeFunctionName(string name)
    {
        return name.EndsWith("Async", StringComparison.Ordinal)
            ? name[..^5]
            : name;
    }

    private static string Truncate(string text, int maxLen) =>
        text.Length <= maxLen ? text : text[..maxLen] + "\u2026";
}
