using lucia.EvalHarness.Configuration;

namespace lucia.EvalHarness.Tests;

/// <summary>
/// Verifies that all named profiles and generated sweep combinations carry a
/// deterministic default seed. Eliminates run-to-run score drift caused by
/// <c>Seed = null</c> non-determinism (issue #132).
///
/// Relationship with #130: #130 forwards seed/temperature knobs via typed
/// <c>ChatOptions</c> to providers. This suite only validates the configuration
/// layer. Do not merge forwarding logic here.
/// </summary>
public sealed class ModelParameterProfileDefaultSeedTests
{
    // ──────────────────────────────────────────────────────────────
    // Named profiles must have a non-null, deterministic seed
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void Default_Profile_HasNonNullSeed()
    {
        Assert.NotNull(ModelParameterProfile.Default.Seed);
    }

    [Fact]
    public void Precise_Profile_HasNonNullSeed()
    {
        Assert.NotNull(ModelParameterProfile.Precise.Seed);
    }

    [Fact]
    public void Creative_Profile_HasNonNullSeed()
    {
        Assert.NotNull(ModelParameterProfile.Creative.Seed);
    }

    [Fact]
    public void All_BuiltInProfiles_HaveNonNullSeed()
    {
        foreach (var (name, profile) in ModelParameterProfile.BuiltInProfiles)
        {
            Assert.True(
                profile.Seed.HasValue,
                $"Built-in profile '{name}' must have a non-null Seed for reproducibility");
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Default construction must carry a deterministic seed
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void DefaultConstruction_HasNonNullSeed()
    {
        var profile = new ModelParameterProfile { Name = "custom" };
        Assert.NotNull(profile.Seed);
    }

    [Fact]
    public void DefaultConstruction_SeedEqualsDefaultSeedConstant()
    {
        var profile = new ModelParameterProfile { Name = "custom" };
        Assert.Equal(ModelParameterProfile.DefaultSeed, profile.Seed);
    }

    // ──────────────────────────────────────────────────────────────
    // All named profiles use the same shared constant, not magic numbers
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void Default_Profile_SeedEqualsDefaultSeedConstant()
    {
        Assert.Equal(ModelParameterProfile.DefaultSeed, ModelParameterProfile.Default.Seed);
    }

    [Fact]
    public void Precise_Profile_SeedEqualsDefaultSeedConstant()
    {
        Assert.Equal(ModelParameterProfile.DefaultSeed, ModelParameterProfile.Precise.Seed);
    }

    [Fact]
    public void Creative_Profile_SeedEqualsDefaultSeedConstant()
    {
        Assert.Equal(ModelParameterProfile.DefaultSeed, ModelParameterProfile.Creative.Seed);
    }

    // ──────────────────────────────────────────────────────────────
    // Caller-provided seed must win over the default
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void Seed_CanBeExplicitlyOverriddenToNull()
    {
        var profile = new ModelParameterProfile { Name = "custom", Seed = null };
        Assert.Null(profile.Seed);
    }

    [Fact]
    public void Seed_CanBeExplicitlyOverriddenToArbitraryValue()
    {
        var profile = new ModelParameterProfile { Name = "custom", Seed = 99 };
        Assert.Equal(99, profile.Seed);
    }

    // ──────────────────────────────────────────────────────────────
    // ParameterSweepConfig: BaseSeed defaults to the shared constant
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void ParameterSweepConfig_BaseSeed_DefaultsToNonNull()
    {
        var config = new ParameterSweepConfig();
        Assert.NotNull(config.BaseSeed);
    }

    [Fact]
    public void ParameterSweepConfig_BaseSeed_DefaultsToDefaultSeedConstant()
    {
        var config = new ParameterSweepConfig();
        Assert.Equal(ModelParameterProfile.DefaultSeed, config.BaseSeed);
    }

    [Fact]
    public void ParameterSweepConfig_BaseSeed_CanBeOverriddenToNull()
    {
        var config = new ParameterSweepConfig { BaseSeed = null };
        Assert.Null(config.BaseSeed);
    }

    // ──────────────────────────────────────────────────────────────
    // GenerateCombinations: default config produces seeded profiles
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void GenerateCombinations_WithDefaultConfig_AllCombosHaveNonNullSeed()
    {
        var config = new ParameterSweepConfig
        {
            TemperatureValues = [0.5, 1.0],
            TopKValues = [40],
            TopPValues = [0.9],
            RepeatPenaltyValues = [1.1],
            MaxCombinations = 10
        };

        var combos = config.GenerateCombinations();

        Assert.NotEmpty(combos);
        Assert.All(combos, p => Assert.NotNull(p.Seed));
    }

    [Fact]
    public void GenerateCombinations_WithDefaultConfig_MultipleCombosHaveDistinctSeeds()
    {
        // Each combo must receive a unique seed block, not the same value
        var config = new ParameterSweepConfig
        {
            TemperatureValues = [0.3, 0.7],
            TopKValues = [40],
            TopPValues = [0.9],
            RepeatPenaltyValues = [1.1],
            MaxCombinations = 10
        };

        var combos = config.GenerateCombinations();
        var seeds = combos.Select(p => p.Seed).ToList();

        Assert.Equal(seeds.Count, seeds.Distinct().Count());
    }

    // ──────────────────────────────────────────────────────────────
    // Explicit opt-out: BaseSeed = null still produces null seeds
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void GenerateCombinations_WithExplicitBaseSeedNull_AllCombosHaveNullSeed()
    {
        var config = new ParameterSweepConfig
        {
            BaseSeed = null,
            TemperatureValues = [0.5],
            TopKValues = [40],
            TopPValues = [0.9],
            RepeatPenaltyValues = [1.1],
            MaxCombinations = 10
        };

        var combos = config.GenerateCombinations();

        Assert.NotEmpty(combos);
        Assert.All(combos, p => Assert.Null(p.Seed));
    }
}
