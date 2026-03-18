using FakeItEasy;
using lucia.AgentHost.Conversation.Execution;
using lucia.AgentHost.Conversation.Models;
using lucia.Agents.Abstractions;
using lucia.Agents.Configuration;
using lucia.Agents.Models;
using lucia.Agents.Models.HomeAssistant;
using lucia.Agents.Skills;
using lucia.HomeAssistant.Models;
using lucia.HomeAssistant.Services;
using lucia.Wyoming.CommandRouting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Tests.Conversation;

public sealed class DirectSkillExecutorTests
{
    private readonly IServiceProvider _serviceProvider = A.Fake<IServiceProvider>();
    private readonly DirectSkillExecutor _executor;

    public DirectSkillExecutorTests()
    {
        _executor = new DirectSkillExecutor(
            _serviceProvider, A.Fake<ILogger<DirectSkillExecutor>>());
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

    private static ConversationContext CreateContext() => new()
    {
        Timestamp = DateTimeOffset.UtcNow,
        ConversationId = "test-conv",
        DeviceArea = "Living Room"
    };
}
