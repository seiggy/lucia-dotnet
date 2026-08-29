using FakeItEasy;
using lucia.AgentHost.Conversation.Execution;
using lucia.AgentHost.Conversation.Models;
using lucia.Agents.Abstractions;
using lucia.Agents.Configuration;
using lucia.Agents.Configuration.UserConfiguration;
using lucia.Agents.Models;
using lucia.Agents.Models.HomeAssistant;
using lucia.Agents.Services;
using lucia.Agents.Skills;
using lucia.HomeAssistant.Models;
using lucia.HomeAssistant.Services;
using lucia.Wyoming.CommandRouting;
using Microsoft.Extensions.Configuration;
using Microsoft.FeatureManagement;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Tests.Conversation;

public sealed class DirectSkillExecutorTests
{
    private readonly IServiceProvider _serviceProvider = A.Fake<IServiceProvider>();
    private readonly IEntityLocationService _entityLocationService = A.Fake<IEntityLocationService>();
    private readonly IFeatureManager _featureManager = A.Fake<IFeatureManager>();
    private readonly ICascadingEntityResolver _cascadingEntityResolver = A.Fake<ICascadingEntityResolver>();
    private readonly DirectSkillExecutor _executor;

    public DirectSkillExecutorTests()
    {
        A.CallTo(() => _featureManager.IsEnabledAsync(A<string>._))
            .Returns(Task.FromResult(false));
        A.CallTo(() => _entityLocationService.IsCacheReady).Returns(true);
        _executor = new DirectSkillExecutor(
            _serviceProvider,
            _entityLocationService,
            _cascadingEntityResolver,
            _featureManager,
            A.Fake<ILogger<DirectSkillExecutor>>());
    }

    [Fact]
    public async Task ExecuteAsync_WithNoMatch_ReturnsFailed()
    {
        // Arrange
        var route = CommandRouteResult.NoMatch(TimeSpan.FromMilliseconds(1));
        var context = CreateContext();

        // Act
        var result = await _executor.ExecuteAsync(route, context);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("No matched pattern", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_LightToggle_CallsLightControlSkill()
    {
        // Arrange — create a real LightControlSkill with mocked HA dependencies
        var haClient = A.Fake<IHomeAssistantClient>();
        var locationService = A.Fake<IEntityLocationService>();
        var options = A.Fake<IOptionsMonitor<LightControlSkillOptions>>();
        A.CallTo(() => options.CurrentValue).Returns(new LightControlSkillOptions());

        // SearchHierarchyAsync returns a result with one light entity
        A.CallTo(() => locationService.SearchHierarchyAsync(
                A<string>._, A<HybridMatchOptions?>._, A<IReadOnlyList<string>?>._, A<CancellationToken>._))
            .Returns(new HierarchicalSearchResult
            {
                FloorMatches = Array.Empty<EntityMatchResult<FloorInfo>>(),
                AreaMatches = Array.Empty<EntityMatchResult<AreaInfo>>(),
                EntityMatches = Array.Empty<EntityMatchResult<HomeAssistantEntity>>(),
                ResolvedEntities = new List<HomeAssistantEntity>
                {
                    new()
                    {
                        EntityId = "light.living_room",
                        FriendlyName = "Living Room Light"
                    }
                },
                ResolutionStrategy = ResolutionStrategy.Entity,
                ResolutionReason = "Direct entity match"
            });

        A.CallTo(() => haClient.CallServiceAsync(
                A<string>._, A<string>._, A<string?>._, A<ServiceCallRequest?>._, A<CancellationToken>._))
            .Returns(Array.Empty<object>());

        // ExactMatchEntities used by non-cascading entity resolution path
        A.CallTo(() => _entityLocationService.ExactMatchEntities(
                "living room", A<IReadOnlyList<string>>._))
            .Returns(new List<HomeAssistantEntity>
            {
                new() { EntityId = "light.living_room", FriendlyName = "Living Room Light" }
            });

        var skill = new LightControlSkill(
            haClient,
            A.Fake<ILogger<LightControlSkill>>(),
            locationService,
            options);

        A.CallTo(() => _serviceProvider.GetService(typeof(LightControlSkill)))
            .Returns(skill);

        var route = new CommandRouteResult
        {
            IsMatch = true,
            Confidence = 0.95f,
            MatchedPattern = new CommandPattern
            {
                Id = "light-toggle",
                SkillId = "LightControlSkill",
                Action = "toggle",
                Templates = ["turn {action} {entity}"]
            },
            CapturedValues = new Dictionary<string, string>
            {
                ["action"] = "on",
                ["entity"] = "living room"
            }
        };

        // Act
        var result = await _executor.ExecuteAsync(route, CreateContext());

        // Assert
        Assert.True(result.Success);
        Assert.Equal("LightControlSkill", result.SkillId);
        Assert.Equal("toggle", result.Action);
        A.CallTo(() => haClient.CallServiceAsync(
                "light", "turn_on", A<string?>._, A<ServiceCallRequest?>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ExecuteAsync_CascadeMiss_UsesEmbeddingEntityMatch()
    {
        var haClient = A.Fake<IHomeAssistantClient>();
        var locationService = A.Fake<IEntityLocationService>();
        var cascadingResolver = A.Fake<ICascadingEntityResolver>();
        var featureManager = A.Fake<IFeatureManager>();
        var options = A.Fake<IOptionsMonitor<LightControlSkillOptions>>();
        var entity = new HomeAssistantEntity
        {
            EntityId = "light.zacks_light",
            FriendlyName = "Zack's Light"
        };

        A.CallTo(() => featureManager.IsEnabledAsync(A<string>._)).Returns(true);
        A.CallTo(() => options.CurrentValue).Returns(new LightControlSkillOptions
        {
            HybridSimilarityThreshold = 0.77
        });
        A.CallTo(() => cascadingResolver.Resolve(
                A<string>._,
                A<string?>._,
                A<string?>._,
                A<IReadOnlyList<string>>._,
                A<string?>._,
                A<CancellationToken>._))
            .Returns(new CascadeResult
            {
                IsResolved = false,
                BailReason = BailReason.NoMatch,
                Explanation = "No deterministic match"
            });
        A.CallTo(() => locationService.SearchHierarchyAsync(
                "Zach's light",
                A<HybridMatchOptions?>._,
                A<IReadOnlyList<string>?>._,
                "light-agent",
                A<CancellationToken>._))
            .Returns(new HierarchicalSearchResult
            {
                FloorMatches = [],
                AreaMatches = [],
                EntityMatches = [],
                ResolvedEntities = [entity],
                ResolutionStrategy = ResolutionStrategy.Entity,
                ResolutionReason = "Embedding match"
            });
        A.CallTo(() => locationService.ExactMatchEntities(
                "light.zacks_light",
                A<IReadOnlyList<string>?>._))
            .Returns([entity]);
        A.CallTo(() => haClient.CallServiceAsync(
                A<string>._,
                A<string>._,
                A<string?>._,
                A<ServiceCallRequest?>._,
                A<CancellationToken>._))
            .Returns([]);

        var skill = new LightControlSkill(
            haClient,
            A.Fake<ILogger<LightControlSkill>>(),
            locationService,
            options);
        A.CallTo(() => _serviceProvider.GetService(typeof(LightControlSkill))).Returns(skill);

        var executor = new DirectSkillExecutor(
            _serviceProvider,
            locationService,
            cascadingResolver,
            featureManager,
            A.Fake<ILogger<DirectSkillExecutor>>());
        var route = new CommandRouteResult
        {
            IsMatch = true,
            Confidence = 0.95f,
            NormalizedTranscript = "turn off zach's light",
            MatchedPattern = new CommandPattern
            {
                Id = "light-toggle",
                SkillId = "LightControlSkill",
                Action = "toggle",
                Templates = ["turn {action} {entity}"]
            },
            CapturedValues = new Dictionary<string, string>
            {
                ["action"] = "off",
                ["entity"] = "Zach's light"
            }
        };

        var result = await executor.ExecuteAsync(route, CreateContext());

        Assert.True(result.Success, result.Error);
        A.CallTo(() => locationService.SearchHierarchyAsync(
                "Zach's light",
                A<HybridMatchOptions?>.That.Matches(matchOptions => HasExpectedThreshold(matchOptions)),
                A<IReadOnlyList<string>?>._,
                "light-agent",
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => haClient.CallServiceAsync(
                "light",
                "turn_off",
                A<string?>._,
                A<ServiceCallRequest?>._,
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public async Task ExecuteAsync_ClimateNonFiniteTemperature_ReturnsFailed(string value)
    {
        var haClient = A.Fake<IHomeAssistantClient>();
        var options = A.Fake<IOptionsMonitor<ClimateControlSkillOptions>>();
        A.CallTo(() => options.CurrentValue).Returns(new ClimateControlSkillOptions());
        var skill = new ClimateControlSkill(
            haClient,
            A.Fake<IEmbeddingProviderResolver>(),
            A.Fake<ILogger<ClimateControlSkill>>(),
            A.Fake<IDeviceCacheService>(),
            _entityLocationService,
            A.Fake<IHybridEntityMatcher>(),
            options,
            new ConfigurationBuilder().Build());
        A.CallTo(() => _serviceProvider.GetService(typeof(ClimateControlSkill))).Returns(skill);
        var route = new CommandRouteResult
        {
            IsMatch = true,
            Confidence = 0.9f,
            MatchedPattern = new CommandPattern
            {
                Id = "climate-set",
                SkillId = "ClimateControlSkill",
                Action = "set_temperature",
                Templates = ["set {entity} to {value}"]
            },
            CapturedValues = new Dictionary<string, string>
            {
                ["entity"] = "office",
                ["value"] = value
            }
        };

        var result = await _executor.ExecuteAsync(route, CreateContext());

        Assert.False(result.Success);
        Assert.Contains("finite", result.Error, StringComparison.OrdinalIgnoreCase);
        A.CallTo(() => haClient.CallServiceAsync(
                A<string>._,
                A<string>._,
                A<string?>._,
                A<ServiceCallRequest?>._,
                A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Theory]
    [InlineData("-5")]
    [InlineData("101")]
    public async Task ExecuteAsync_LightBrightnessOutsideRange_ReturnsFailed(string value)
    {
        var haClient = A.Fake<IHomeAssistantClient>();
        var options = A.Fake<IOptionsMonitor<LightControlSkillOptions>>();
        A.CallTo(() => options.CurrentValue).Returns(new LightControlSkillOptions());
        var skill = new LightControlSkill(
            haClient,
            A.Fake<ILogger<LightControlSkill>>(),
            _entityLocationService,
            options);
        A.CallTo(() => _serviceProvider.GetService(typeof(LightControlSkill))).Returns(skill);
        var route = new CommandRouteResult
        {
            IsMatch = true,
            Confidence = 0.9f,
            MatchedPattern = new CommandPattern
            {
                Id = "light-brightness",
                SkillId = "LightControlSkill",
                Action = "brightness",
                Templates = ["dim {entity} to {value}"]
            },
            CapturedValues = new Dictionary<string, string>
            {
                ["entity"] = "kitchen light",
                ["value"] = value
            }
        };

        var result = await _executor.ExecuteAsync(route, CreateContext());

        Assert.False(result.Success);
        Assert.Contains("0 and 100", result.Error, StringComparison.OrdinalIgnoreCase);
        A.CallTo(() => haClient.CallServiceAsync(
                A<string>._,
                A<string>._,
                A<string?>._,
                A<ServiceCallRequest?>._,
                A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task ExecuteAsync_UnsupportedSkillAction_ReturnsFailed()
    {
        // Arrange — route matches an unknown skill/action combo
        var route = new CommandRouteResult
        {
            IsMatch = true,
            Confidence = 0.9f,
            MatchedPattern = new CommandPattern
            {
                Id = "unknown-action",
                SkillId = "NonExistentSkill",
                Action = "fly",
                Templates = ["{action} the {entity}"]
            },
            CapturedValues = new Dictionary<string, string> { ["entity"] = "drone" }
        };

        // Act
        var result = await _executor.ExecuteAsync(route, CreateContext());

        // Assert
        Assert.False(result.Success);
        Assert.Equal("NonExistentSkill", result.SkillId);
        Assert.Contains("No executor registered", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_SkillThrows_ReturnsFailed()
    {
        // Arrange — service provider returns null → GetRequiredService throws
        A.CallTo(() => _serviceProvider.GetService(typeof(LightControlSkill)))
            .Returns(null);

        var route = new CommandRouteResult
        {
            IsMatch = true,
            Confidence = 0.95f,
            MatchedPattern = new CommandPattern
            {
                Id = "light-toggle",
                SkillId = "LightControlSkill",
                Action = "toggle",
                Templates = ["turn {action} {entity}"]
            },
            CapturedValues = new Dictionary<string, string>
            {
                ["action"] = "on",
                ["entity"] = "bedroom"
            }
        };

        // Act
        var result = await _executor.ExecuteAsync(route, CreateContext());

        // Assert
        Assert.False(result.Success);
        Assert.Equal("LightControlSkill", result.SkillId);
        Assert.NotNull(result.Error);
    }

    private static bool HasExpectedThreshold(HybridMatchOptions? matchOptions) =>
        matchOptions is { Threshold: 0.77 };

    private static ConversationContext CreateContext() => new()
    {
        Timestamp = DateTimeOffset.UtcNow,
        ConversationId = "test-conv",
        DeviceArea = "Living Room"
    };
}
