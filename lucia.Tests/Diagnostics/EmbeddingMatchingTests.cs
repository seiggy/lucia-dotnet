using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using lucia.Agents.Skills;
using lucia.Tests.TestDoubles;
using Xunit;
using Xunit.Abstractions;

namespace lucia.Tests.Diagnostics;

/// <summary>
/// Diagnostic tests for understanding embedding matching behavior.
/// These verify the DeterministicEmbeddingGenerator correctly handles
/// Unicode apostrophes (U+2019) vs ASCII apostrophes (U+0027).
/// </summary>
public sealed class EmbeddingMatchingTests
{
    private readonly ITestOutputHelper _output;
    private readonly DeterministicEmbeddingGenerator _generator = new();

    public EmbeddingMatchingTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task UnicodeApostrophe_MatchesAsciiApostrophe()
    {
        // "Zack's Light" with Unicode RIGHT SINGLE QUOTATION MARK (U+2019)
        var unicodeName = "Zack\u2019s Light";
        // "Zack's Light" with ASCII apostrophe (U+0027)
        var asciiName = "Zack's Light";

        _output.WriteLine($"Unicode name: '{unicodeName}' chars: {FormatChars(unicodeName)}");
        _output.WriteLine($"ASCII name:   '{asciiName}' chars: {FormatChars(asciiName)}");

        var unicodeEmb = await _generator.GenerateAsync([unicodeName]);
        var asciiEmb = await _generator.GenerateAsync([asciiName]);

        var similarity = CosineSimilarity(unicodeEmb[0], asciiEmb[0]);
        _output.WriteLine($"Cosine similarity: {similarity:F6}");

        // Dump non-zero vector components
        DumpVector("Unicode", unicodeEmb[0]);
        DumpVector("ASCII", asciiEmb[0]);

        Assert.True(similarity > 0.99, $"Expected near-identical embeddings, got similarity {similarity:F4}");
    }

    [Theory]
    [InlineData("Zack's Light", "Sack's Light")]
    [InlineData("Zack's Light", "Sag's Light")]
    [InlineData("Zack\u2019s Light", "Sack's Light")]
    public async Task SttVariants_HaveReasonableSimilarity(string cached, string search)
    {
        var cachedEmb = await _generator.GenerateAsync([cached]);
        var searchEmb = await _generator.GenerateAsync([search]);
        var sim = CosineSimilarity(cachedEmb[0], searchEmb[0]);

        _output.WriteLine($"'{cached}' vs '{search}': similarity = {sim:F4}");

        // Should be above the 0.6 threshold used in FindLight
        Assert.True(sim > 0.6, $"Expected similarity > 0.6, got {sim:F4}");
    }

    [Fact]
    public async Task SnapshotEntity_CanBeFound()
    {
        var snapshotPath = Path.Combine(AppContext.BaseDirectory, "TestData", "ha-snapshot.json");
        if (!File.Exists(snapshotPath))
        {
            _output.WriteLine($"Snapshot not found at: {snapshotPath}");
            return;
        }

        var haClient = FakeHomeAssistantClient.FromSnapshotFile(snapshotPath);
        var entities = await haClient.GetAllEntityStatesAsync();
        var lights = entities.Where(e => e.EntityId.StartsWith("light.")).ToList();

        _output.WriteLine($"Total light entities: {lights.Count}");

        foreach (var light in lights)
        {
            var fn = light.Attributes.TryGetValue("friendly_name", out var nameObj)
                ? nameObj?.ToString() ?? "<null>"
                : "<missing>";
            _output.WriteLine($"  {light.EntityId} -> '{fn}' chars: {FormatChars(fn)}");
        }

        var zack = lights.FirstOrDefault(l => l.EntityId.Contains("zack"));
        Assert.NotNull(zack);

        var friendlyName = zack.Attributes["friendly_name"]?.ToString()!;
        _output.WriteLine($"\nTarget entity: {zack.EntityId}");
        _output.WriteLine($"Friendly name: '{friendlyName}' chars: {FormatChars(friendlyName)}");

        // Generate embeddings for both cached name and search terms
        var cachedEmb = await _generator.GenerateAsync([friendlyName]);
        var searchTerms = new[] { "Zack's Light", "Sack's Light", "Sag's Light", "zack's light", "bedroom light" };

        foreach (var search in searchTerms)
        {
            var searchEmb = await _generator.GenerateAsync([search]);
            var sim = CosineSimilarity(cachedEmb[0], searchEmb[0]);
            _output.WriteLine($"  '{friendlyName}' vs '{search}': similarity = {sim:F4} {(sim > 0.6 ? "PASS" : "FAIL")}");
        }
    }

    [Theory]
    [InlineData("Zack's Light")]
    [InlineData("Sack's Light")]
    [InlineData("zack's light")]
    public async Task FindLightAsync_EndToEnd(string searchTerm)
    {
        var snapshotPath = Path.Combine(AppContext.BaseDirectory, "TestData", "ha-snapshot.json");
        Assert.True(File.Exists(snapshotPath), $"Snapshot not found: {snapshotPath}");

        var haClient = FakeHomeAssistantClient.FromSnapshotFile(snapshotPath);
        var embGen = new DeterministicEmbeddingGenerator();
        var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Trace));
        var logger = loggerFactory.CreateLogger<LightControlSkill>();

        var skill = new LightControlSkill(haClient, embGen, logger);

        _output.WriteLine($"Calling FindLightAsync('{searchTerm}')...");
        var result = await skill.FindLightAsync(searchTerm);
        _output.WriteLine($"Result:\n{result}");

        // Should find "Zack's Light" (light.zacks_light)
        Assert.Contains("zacks_light", result, StringComparison.OrdinalIgnoreCase);
    }

    private static double CosineSimilarity(Embedding<float> a, Embedding<float> b)
    {
        var s1 = a.Vector.Span;
        var s2 = b.Vector.Span;
        double dot = 0, m1 = 0, m2 = 0;
        for (int i = 0; i < s1.Length; i++)
        {
            dot += s1[i] * s2[i];
            m1 += s1[i] * s1[i];
            m2 += s2[i] * s2[i];
        }
        return (m1 == 0 || m2 == 0) ? 0 : dot / (Math.Sqrt(m1) * Math.Sqrt(m2));
    }

    private void DumpVector(string label, Embedding<float> emb)
    {
        var span = emb.Vector.Span;
        var parts = new List<string>();
        for (int i = 0; i < span.Length; i++)
        {
            if (span[i] != 0)
                parts.Add($"[{i}]={span[i]:F4}");
        }
        _output.WriteLine($"{label} non-zero dims ({parts.Count}): {string.Join(", ", parts)}");
    }

    private static string FormatChars(string s) =>
        string.Join(" ", s.Select(c => $"U+{(int)c:X4}"));
}
