using System.Text;
using lucia.Agents.Abstractions;
using lucia.HomeAssistant.Models;
using lucia.HomeAssistant.Services;
using lucia.Tests.TestDoubles;

namespace lucia.EvalHarness.Evaluation;

/// <summary>
/// Result of validating a scenario's expected tool calls and state against actual execution.
/// </summary>
public sealed class ScenarioValidationResult
{
    public required string ScenarioId { get; init; }
    public required bool Passed { get; init; }
    public required double Score { get; init; }
    public required List<string> Issues { get; init; }
    public required List<string> Successes { get; init; }

    /// <summary>Describes all validation outcomes as a single string.</summary>
    public string Summary => Passed
        ? $"PASS ({Successes.Count} checks passed)"
        : $"FAIL: {string.Join("; ", Issues)}";
}

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

                snapshotService.RegisterEntity(entityId, friendlyName);
            }
        }
    }

    /// <summary>
    /// Validates the scenario execution results against expectations.
    /// </summary>
    public static async Task<ScenarioValidationResult> ValidateAsync(
        TestScenario scenario,
        IReadOnlyList<ConversationTurn> conversation,
        IHomeAssistantClient haClient)
    {
        var issues = new List<string>();
        var successes = new List<string>();

        // 1. Validate tool call chain
        ValidateToolCalls(scenario, conversation, issues, successes);

        // 2. Validate response content
        ValidateResponse(scenario, conversation, issues, successes);

        // 3. Validate final entity state
        await ValidateFinalStateAsync(scenario, haClient, issues, successes);

        var totalChecks = issues.Count + successes.Count;
        var score = totalChecks > 0 ? (double)successes.Count / totalChecks * 100 : 100;

        return new ScenarioValidationResult
        {
            ScenarioId = scenario.Id,
            Passed = issues.Count == 0,
            Score = score,
            Issues = issues,
            Successes = successes
        };
    }

    private static void ValidateToolCalls(
        TestScenario scenario,
        IReadOnlyList<ConversationTurn> conversation,
        List<string> issues,
        List<string> successes)
    {
        // Extract actual tool calls from the conversation in order
        var actualToolCalls = conversation
            .Where(t => t.Role == "assistant" && t.ToolCalls is { Count: > 0 })
            .SelectMany(t => t.ToolCalls!)
            .ToList();

        var expected = scenario.ExpectedToolCalls;

        if (expected.Count == 0)
        {
            // Scenario expects NO tool calls
            if (actualToolCalls.Count == 0)
                successes.Add("No tool calls made (as expected)");
            else
                issues.Add($"Expected no tool calls but got {actualToolCalls.Count}: {string.Join(", ", actualToolCalls.Select(t => t.Name))}");
            return;
        }

        // Check tool count
        if (actualToolCalls.Count < expected.Count)
        {
            issues.Add($"Expected {expected.Count} tool call(s) but only got {actualToolCalls.Count}");
        }

        // Walk through expected calls in order
        for (var i = 0; i < expected.Count; i++)
        {
            var exp = expected[i];

            if (i >= actualToolCalls.Count)
            {
                issues.Add($"[{i}] Expected {exp.Tool} but no more tool calls were made");
                continue;
            }

            var actual = actualToolCalls[i];

            // Check tool name — normalize away the "Async" suffix that
            // AIFunctionFactory.Create strips from method names so YAML
            // scenarios written with either form match correctly.
            var normalizedActual = NormalizeFunctionName(actual.Name);
            var normalizedExpected = NormalizeFunctionName(exp.Tool);

            if (!string.Equals(normalizedActual, normalizedExpected, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add($"[{i}] Expected tool '{exp.Tool}' but got '{actual.Name}'");
                continue;
            }

            successes.Add($"[{i}] Correct tool: {exp.Tool}");

            // Check arguments
            foreach (var (argName, expectedValue) in exp.Arguments)
            {
                if (actual.Arguments is null || !actual.Arguments.TryGetValue(argName, out var actualValue))
                {
                    // Try case-insensitive lookup
                    var match = actual.Arguments?.FirstOrDefault(
                        kvp => string.Equals(kvp.Key, argName, StringComparison.OrdinalIgnoreCase));
                    actualValue = match?.Value;
                }

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
    }

    private static void ValidateResponse(
        TestScenario scenario,
        IReadOnlyList<ConversationTurn> conversation,
        List<string> issues,
        List<string> successes)
    {
        // Get the final assistant text response
        var finalResponse = conversation
            .LastOrDefault(t => t.Role == "assistant" && t.Content is not null && t.ToolCalls is null)
            ?.Content ?? "";

        foreach (var mustContain in scenario.ResponseMustContain)
        {
            if (finalResponse.Contains(mustContain, StringComparison.OrdinalIgnoreCase))
                successes.Add($"Response contains '{mustContain}'");
            else
                issues.Add($"Response missing expected text '{mustContain}'. Got: '{Truncate(finalResponse, 100)}'");
        }

        foreach (var mustNotContain in scenario.ResponseMustNotContain)
        {
            if (finalResponse.Contains(mustNotContain, StringComparison.OrdinalIgnoreCase))
                issues.Add($"Response contains prohibited text '{mustNotContain}'");
            else
                successes.Add($"Response correctly omits '{mustNotContain}'");
        }
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
