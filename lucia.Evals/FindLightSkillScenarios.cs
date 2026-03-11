#pragma warning disable CS0618 // FindLightAsync is obsolete but still exercised in eval scenarios

using System.Diagnostics;
using System.Text.RegularExpressions;

namespace lucia.Evals;

/// <summary>
/// FindLight skill evaluation scenarios — tests embedding similarity search
/// directly (no LLM) against real embeddings and the live Home Assistant
/// entity cache, including STT-artifact resilience.
/// </summary>
public sealed class FindLightSkillScenarios
{
    public static EvalScenarioGroup Create(EvalTestFixture fixture)
    {
        var scenarios = new List<EvalScenario>();

        foreach (var embeddingModelId in GetEmbeddingModelIds(fixture))
        {
            // ── Kitchen Lights ────────────────────────────────────────
            scenarios.Add(CreateFindLightScenario(fixture, embeddingModelId,
                "Kitchen Lights", "light.kitchen_lights_light", 1, "exact",
                tags: ["find-light", "kitchen", "exact"]));
            scenarios.Add(CreateFindLightScenario(fixture, embeddingModelId,
                "kitchen light", "light.kitchen_lights_light", 1, "lowercase",
                tags: ["find-light", "kitchen", "lowercase"]));
            scenarios.Add(CreateFindLightScenario(fixture, embeddingModelId,
                "Kitchen Light", "light.kitchen_lights_light", 1, "singular",
                tags: ["find-light", "kitchen", "singular"]));
            scenarios.Add(CreateFindLightScenario(fixture, embeddingModelId,
                "Kichen Light", "light.kitchen_lights_light", 1, "stt-typo",
                tags: ["find-light", "kitchen", "stt"]));
            scenarios.Add(CreateFindLightScenario(fixture, embeddingModelId,
                "Kitchin Lite", "light.kitchen_lights_light", 1, "stt-phonetic",
                tags: ["find-light", "kitchen", "stt"]));

            // ── Zack's Light ─────────────────────────────────────────
            scenarios.Add(CreateFindLightScenario(fixture, embeddingModelId,
                "Zack\u2019s Light", "light.zacks_light", 2, "exact",
                tags: ["find-light", "zacks", "exact"]));
            scenarios.Add(CreateFindLightScenario(fixture, embeddingModelId,
                "Zach's Light", "light.zacks_light", 2, "stt-spelling",
                tags: ["find-light", "zacks", "stt"]));
            scenarios.Add(CreateFindLightScenario(fixture, embeddingModelId,
                "Sack's Light", "light.zacks_light", 2, "stt-lisp-sack",
                tags: ["find-light", "zacks", "stt"]));
            scenarios.Add(CreateFindLightScenario(fixture, embeddingModelId,
                "Zag's Light", "light.zacks_light", 2, "stt-lisp-zag",
                tags: ["find-light", "zacks", "stt"]));
            scenarios.Add(CreateFindLightScenario(fixture, embeddingModelId,
                "Zagslight", "light.zacks_light", 2, "stt-lisp-zaglight",
                tags: ["find-light", "zacks", "stt"]));
            scenarios.Add(CreateFindLightScenario(fixture, embeddingModelId,
                "Sag's Light", "light.zacks_light", 2, "stt-lisp-sag",
                tags: ["find-light", "zacks", "stt"]));

            // ── Garage Lights ────────────────────────────────────────
            scenarios.Add(CreateFindLightScenario(fixture, embeddingModelId,
                "Garage Lights", "light.garage_lights", 3, "exact",
                tags: ["find-light", "garage", "exact"]));
            scenarios.Add(CreateFindLightScenario(fixture, embeddingModelId,
                "garage light", "light.garage_lights", 3, "lowercase",
                tags: ["find-light", "garage", "lowercase"]));
            scenarios.Add(CreateFindLightScenario(fixture, embeddingModelId,
                "Garaj Light", "light.garage_lights", 3, "stt-phonetic",
                tags: ["find-light", "garage", "stt"]));

            // ── Dianna's Lamp ────────────────────────────────────────
            scenarios.Add(CreateFindLightScenario(fixture, embeddingModelId,
                "Dianna\u2019s Lamp", "light.diannas_lamp", 3, "exact",
                tags: ["find-light", "diannas", "exact"]));
            scenarios.Add(CreateFindLightScenario(fixture, embeddingModelId,
                "Diana's Lamp", "light.diannas_lamp", 3, "stt-spelling",
                tags: ["find-light", "diannas", "stt"]));
            scenarios.Add(CreateFindLightScenario(fixture, embeddingModelId,
                "Dianna's Light", "light.diannas_lamp", 3, "stt-synonym",
                tags: ["find-light", "diannas", "stt"]));
        }

        return new EvalScenarioGroup
        {
            Name = "FindLight Skill",
            Description = "Embedding similarity search accuracy and STT-artifact resilience for light entity matching",
            Scenarios = scenarios
        };
    }

    private static EvalScenario CreateFindLightScenario(
        EvalTestFixture fixture,
        string embeddingModelId,
        string searchTerm,
        string expectedEntityId,
        int expectedMaxResults,
        string variant,
        IReadOnlyList<string> tags)
    {
        return new EvalScenario
        {
            Name = $"FindLight \"{searchTerm}\" [{variant}] [{embeddingModelId}]",
            Group = "FindLight Skill",
            Tags = tags,
            RunAsync = async () =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    var skill = await fixture.CreateLightControlSkillAsync(embeddingModelId);
                    var result = await skill.FindLightAsync(searchTerm);
                    sw.Stop();

                    if (string.IsNullOrWhiteSpace(result))
                        return EvalScenarioResult.Fail(sw.Elapsed,
                            $"FindLightAsync returned empty for \"{searchTerm}\" ({variant})");

                    if (result.Contains("No matching lights found", StringComparison.OrdinalIgnoreCase) ||
                        result.Contains("No lights available", StringComparison.OrdinalIgnoreCase))
                        return EvalScenarioResult.Fail(sw.Elapsed,
                            $"No lights found for \"{searchTerm}\" ({variant})",
                            result);

                    if (!result.Contains(expectedEntityId, StringComparison.OrdinalIgnoreCase))
                        return EvalScenarioResult.Fail(sw.Elapsed,
                            $"Expected entity {expectedEntityId} not found in result for \"{searchTerm}\" ({variant})",
                            result);

                    var matchCount = CountMatchesInResult(result);
                    if (matchCount > expectedMaxResults)
                        return EvalScenarioResult.Fail(sw.Elapsed,
                            $"Too many matches for \"{searchTerm}\" ({variant}): got {matchCount}, expected \u2264{expectedMaxResults}",
                            result);

                    return EvalScenarioResult.Pass(sw.Elapsed,
                        $"Found {expectedEntityId} ({matchCount} match(es))",
                        result);
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return EvalScenarioResult.Fail(sw.Elapsed, ex.Message, ex.ToString());
                }
            }
        };
    }

    /// <summary>
    /// Parses the FindLightAsync result string to count how many matches were returned.
    /// Single-match format: "Found light: ..."
    /// Multi-match format: "Found N matching light(s):\n- ...\n- ..."
    /// </summary>
    private static int CountMatchesInResult(string result)
    {
        if (string.IsNullOrWhiteSpace(result))
            return 0;

        if (result.StartsWith("Found light:", StringComparison.OrdinalIgnoreCase))
            return 1;

        var dashLines = Regex.Matches(result, @"^- ", RegexOptions.Multiline);
        return dashLines.Count > 0 ? dashLines.Count : 1;
    }

    private static IEnumerable<string> GetEmbeddingModelIds(EvalTestFixture fixture)
    {
        var config = fixture.Configuration;

        if (config.Models.Count == 0)
            return [""];

        var defaultEmbedding = config.EmbeddingModels.Count > 0
            ? config.EmbeddingModels[0].DeploymentName
            : "";

        return config.Models
            .Select(m => m.EmbeddingModel ?? defaultEmbedding)
            .Distinct();
    }
}
