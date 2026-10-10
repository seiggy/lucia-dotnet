using lucia.AgentHost.Conversation.Templates;

namespace lucia.Tests.Data.Providers;

/// <summary>
/// Behaviour every <see cref="IResponseTemplateRepository"/> provider must share.
/// </summary>
public abstract class ResponseTemplateRepositoryContract
{
    protected abstract Task<StoreScope<IResponseTemplateRepository>> CreateAsync();

    [Fact]
    public async Task GetAllAsync_EmptyStore_ReturnsEmpty()
    {
        await using var scope = await CreateAsync();

        Assert.Empty(await scope.Store.GetAllAsync());
    }

    [Fact]
    public async Task CreateAsync_NewTemplate_RoundTripsAllFields()
    {
        await using var scope = await CreateAsync();

        var template = new ResponseTemplate
        {
            SkillId = "LightControlSkill",
            Action = "toggle",
            Templates = ["OK, I turned it on.", "Done!"],
            IsDefault = false,
        };

        var created = await scope.Store.CreateAsync(template);

        Assert.False(string.IsNullOrWhiteSpace(created.Id));

        var loaded = await scope.Store.GetByIdAsync(created.Id!);
        Assert.NotNull(loaded);
        Assert.Equal(created.SkillId, loaded!.SkillId);
        Assert.Equal(created.Action, loaded.Action);
        Assert.Equal(created.Templates, loaded.Templates);
        Assert.Equal(created.IsDefault, loaded.IsDefault);
    }

    [Fact]
    public async Task GetBySkillAndActionAsync_FindsMatchingTemplate()
    {
        await using var scope = await CreateAsync();

        await scope.Store.CreateAsync(new ResponseTemplate
        {
            SkillId = "SceneControlSkill",
            Action = "activate",
            Templates = ["Scene activated."],
            IsDefault = false,
        });

        var template = await scope.Store.GetBySkillAndActionAsync("SceneControlSkill", "activate");
        Assert.NotNull(template);
        Assert.Equal("activate", template!.Action);
    }

    [Fact]
    public async Task UpdateAsync_ReplacesTemplateAndMovesUpdatedAt()
    {
        await using var scope = await CreateAsync();

        var created = await scope.Store.CreateAsync(new ResponseTemplate
        {
            SkillId = "ClimateControlSkill",
            Action = "adjust",
            Templates = ["Old template."],
            IsDefault = false,
        });

        var before = created.UpdatedAt;
        await Task.Delay(10);

        var update = new ResponseTemplate
        {
            SkillId = "ClimateControlSkill",
            Action = "adjust",
            Templates = ["New template."],
            IsDefault = true,
        };

        var result = await scope.Store.UpdateAsync(created.Id!, update);

        Assert.True(result.UpdatedAt > before);
        Assert.Equal("ClimateControlSkill", result.SkillId);
        Assert.Equal("adjust", result.Action);
        Assert.Equal(["New template."], result.Templates);
    }

    [Fact]
    public async Task DeleteAsync_RemovesOnlyTheTargetTemplate()
    {
        await using var scope = await CreateAsync();

        var first = await scope.Store.CreateAsync(new ResponseTemplate
        {
            SkillId = "LightControlSkill",
            Action = "toggle",
            Templates = ["One"],
            IsDefault = false,
        });
        var second = await scope.Store.CreateAsync(new ResponseTemplate
        {
            SkillId = "LightControlSkill",
            Action = "brightness",
            Templates = ["Two"],
            IsDefault = false,
        });

        var deleted = await scope.Store.DeleteAsync(first.Id!);

        Assert.True(deleted);
        Assert.Null(await scope.Store.GetByIdAsync(first.Id!));
        Assert.NotNull(await scope.Store.GetByIdAsync(second.Id!));
    }

    [Fact]
    public async Task CreateAsync_DuplicateSkillAndAction_ThrowsDuplicateException()
    {
        await using var scope = await CreateAsync();

        await scope.Store.CreateAsync(new ResponseTemplate
        {
            SkillId = "LightControlSkill",
            Action = "toggle",
            Templates = ["First"],
            IsDefault = false,
        });

        await Assert.ThrowsAsync<DuplicateResponseTemplateException>(() => scope.Store.CreateAsync(new ResponseTemplate
        {
            SkillId = "LightControlSkill",
            Action = "toggle",
            Templates = ["Second"],
            IsDefault = false,
        }));
    }

    [Fact]
    public async Task ResetToDefaultsAsync_ReplacesExistingTemplatesWithTheSeedSet()
    {
        await using var scope = await CreateAsync();

        await scope.Store.CreateAsync(new ResponseTemplate
        {
            SkillId = "CustomSkill",
            Action = "custom_action",
            Templates = ["Custom"],
            IsDefault = false,
        });

        await scope.Store.ResetToDefaultsAsync();

        var defaults = DefaultResponseTemplates.GetDefaults();
        var all = await scope.Store.GetAllAsync();

        Assert.Equal(defaults.Count, all.Count);
        Assert.All(all, template => Assert.False(string.IsNullOrWhiteSpace(template.Id)));
        Assert.Contains(all, template => template.SkillId == "LightControlSkill" && template.Action == "toggle");
        Assert.DoesNotContain(all, template => template.SkillId == "CustomSkill");
    }
}
