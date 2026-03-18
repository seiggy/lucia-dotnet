using FakeItEasy;
using lucia.AgentHost.Conversation.Templates;
using Microsoft.Extensions.Logging;

namespace lucia.Tests.Conversation;

public sealed class ResponseTemplateRendererTests
{
    private readonly IResponseTemplateRepository _repository = A.Fake<IResponseTemplateRepository>();
    private readonly ResponseTemplateRenderer _renderer;

    public ResponseTemplateRendererTests()
    {
        _renderer = new ResponseTemplateRenderer(
            _repository, A.Fake<ILogger<ResponseTemplateRenderer>>());
    }

    [Fact]
    public async Task RenderAsync_WithMatchingTemplate_ReplacesPlaceholders()
    {
        // Arrange
        var template = new ResponseTemplate
        {
            SkillId = "LightControlSkill",
            Action = "toggle",
            Templates = ["Turned {action} the {entity}."]
        };

        A.CallTo(() => _repository.GetBySkillAndActionAsync(
                "LightControlSkill", "toggle", A<CancellationToken>._))
            .Returns(template);

        var captures = new Dictionary<string, string>
        {
            ["action"] = "on",
            ["entity"] = "living room lights"
        };

        // Act
        var result = await _renderer.RenderAsync(
            "LightControlSkill", "toggle", captures);

        // Assert
        Assert.Equal("Turned on the living room lights.", result);
    }

    [Fact]
    public async Task RenderAsync_WithMissingPlaceholder_LeavesPlaceholderOrUsesEmpty()
    {
        // Arrange
        var template = new ResponseTemplate
        {
            SkillId = "LightControlSkill",
            Action = "toggle",
            Templates = ["Turned {action} the {entity}."]
        };

        A.CallTo(() => _repository.GetBySkillAndActionAsync(
                "LightControlSkill", "toggle", A<CancellationToken>._))
            .Returns(template);

        var captures = new Dictionary<string, string> { ["action"] = "on" };

        // Act
        var result = await _renderer.RenderAsync(
            "LightControlSkill", "toggle", captures);

        // Assert — missing placeholder replaced with empty string
        Assert.Equal("Turned on the .", result);
    }

    [Fact]
    public async Task RenderAsync_WithNoTemplate_ReturnsFallback()
    {
        // Arrange
        A.CallTo(() => _repository.GetBySkillAndActionAsync(
                A<string>._, A<string>._, A<CancellationToken>._))
            .Returns((ResponseTemplate?)null);

        // Act
        var result = await _renderer.RenderAsync(
            "UnknownSkill", "unknown", new Dictionary<string, string>());

        // Assert
        Assert.Equal("Done.", result);
    }

    [Fact]
    public async Task RenderAsync_SelectsRandomTemplate()
    {
        // Arrange
        var template = new ResponseTemplate
        {
            SkillId = "LightControlSkill",
            Action = "toggle",
            Templates =
            [
                "Turned {action} the {entity}.",
                "The {entity} is now {action}.",
                "{entity} switched {action}."
            ]
        };

        A.CallTo(() => _repository.GetBySkillAndActionAsync(
                "LightControlSkill", "toggle", A<CancellationToken>._))
            .Returns(template);

        var captures = new Dictionary<string, string>
        {
            ["action"] = "on",
            ["entity"] = "lights"
        };

        var expectedTemplates = new HashSet<string>
        {
            "Turned on the lights.",
            "The lights is now on.",
            "lights switched on."
        };

        // Act — run multiple times to observe randomness
        var results = new HashSet<string>();
        for (var i = 0; i < 100; i++)
        {
            results.Add(await _renderer.RenderAsync(
                "LightControlSkill", "toggle", captures));
        }

        // Assert — all results come from the template set
        Assert.Subset(expectedTemplates, results);
        Assert.True(results.Count > 1,
            "Expected multiple different templates to be selected across 100 iterations");
    }
}
