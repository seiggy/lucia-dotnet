using lucia.Agents.Abstractions;
using lucia.Agents.Models.HomeAssistant;

namespace lucia.Tests.Data.Providers;

/// <summary>
/// Behaviour every <see cref="IPresenceSensorRepository"/> provider must share.
/// </summary>
public abstract class PresenceSensorRepositoryContract
{
    protected abstract Task<StoreScope<IPresenceSensorRepository>> CreateAsync();

    [Fact]
    public async Task GetAllMappingsAsync_EmptyStore_ReturnsEmpty()
    {
        await using var scope = await CreateAsync();

        Assert.Empty(await scope.Store.GetAllMappingsAsync());
    }

    [Fact]
    public async Task UpsertMappingAsync_NewMapping_RoundTripsAllFields()
    {
        await using var scope = await CreateAsync();

        await scope.Store.UpsertMappingAsync(new PresenceSensorMapping
        {
            EntityId = "binary_sensor.office_presence",
            AreaId = "office",
            AreaName = "Office",
            Confidence = PresenceConfidence.High,
            IsUserOverride = true,
            IsDisabled = true,
        });

        var mapping = Assert.Single(await scope.Store.GetAllMappingsAsync());
        Assert.Equal("binary_sensor.office_presence", mapping.EntityId);
        Assert.Equal("office", mapping.AreaId);
        Assert.Equal("Office", mapping.AreaName);
        Assert.Equal(PresenceConfidence.High, mapping.Confidence);
        Assert.True(mapping.IsUserOverride);
        Assert.True(mapping.IsDisabled);
    }

    [Fact]
    public async Task UpsertMappingAsync_ExistingEntity_ReplacesInsteadOfDuplicating()
    {
        await using var scope = await CreateAsync();
        await scope.Store.UpsertMappingAsync(Mapping("binary_sensor.a", "kitchen"));

        await scope.Store.UpsertMappingAsync(Mapping("binary_sensor.a", "office"));

        var mapping = Assert.Single(await scope.Store.GetAllMappingsAsync());
        Assert.Equal("office", mapping.AreaId);
    }

    [Fact]
    public async Task DeleteMappingAsync_RemovesOnlyTheNamedEntity()
    {
        await using var scope = await CreateAsync();
        await scope.Store.UpsertMappingAsync(Mapping("binary_sensor.a", "kitchen"));
        await scope.Store.UpsertMappingAsync(Mapping("binary_sensor.b", "office"));

        await scope.Store.DeleteMappingAsync("binary_sensor.a");

        var mapping = Assert.Single(await scope.Store.GetAllMappingsAsync());
        Assert.Equal("binary_sensor.b", mapping.EntityId);
    }

    [Fact]
    public async Task ReplaceAutoDetectedMappingsAsync_KeepsUserOverridesAndReplacesAutoDetected()
    {
        await using var scope = await CreateAsync();
        await scope.Store.UpsertMappingAsync(Mapping("binary_sensor.user", "kitchen", isUserOverride: true));
        await scope.Store.UpsertMappingAsync(Mapping("binary_sensor.old_auto", "garage"));

        await scope.Store.ReplaceAutoDetectedMappingsAsync([Mapping("binary_sensor.new_auto", "office")]);

        var ids = (await scope.Store.GetAllMappingsAsync()).Select(m => m.EntityId).Order().ToArray();
        Assert.Equal(["binary_sensor.new_auto", "binary_sensor.user"], ids);
    }

    [Fact]
    public async Task ReplaceAutoDetectedMappingsAsync_EntityOwnedByUserOverride_IsNotOverwritten()
    {
        await using var scope = await CreateAsync();
        await scope.Store.UpsertMappingAsync(Mapping("binary_sensor.shared", "kitchen", isUserOverride: true));

        await scope.Store.ReplaceAutoDetectedMappingsAsync([Mapping("binary_sensor.shared", "office")]);

        var mapping = Assert.Single(await scope.Store.GetAllMappingsAsync());
        Assert.Equal("kitchen", mapping.AreaId);
        Assert.True(mapping.IsUserOverride);
    }

    [Fact]
    public async Task ReplaceAutoDetectedMappingsAsync_DuplicateEntityIds_KeepsHighestConfidence()
    {
        await using var scope = await CreateAsync();

        await scope.Store.ReplaceAutoDetectedMappingsAsync(
        [
            Mapping("binary_sensor.a", "kitchen", PresenceConfidence.Low),
            Mapping("binary_sensor.a", "office", PresenceConfidence.High),
        ]);

        var mapping = Assert.Single(await scope.Store.GetAllMappingsAsync());
        Assert.Equal("office", mapping.AreaId);
        Assert.Equal(PresenceConfidence.High, mapping.Confidence);
    }

    [Fact]
    public async Task ReplaceAutoDetectedMappingsAsync_EmptyList_ClearsAutoDetectedButKeepsUserOverrides()
    {
        await using var scope = await CreateAsync();
        await scope.Store.UpsertMappingAsync(Mapping("binary_sensor.user", "kitchen", isUserOverride: true));
        await scope.Store.UpsertMappingAsync(Mapping("binary_sensor.auto", "garage"));

        await scope.Store.ReplaceAutoDetectedMappingsAsync([]);

        var mapping = Assert.Single(await scope.Store.GetAllMappingsAsync());
        Assert.Equal("binary_sensor.user", mapping.EntityId);
    }
    [Fact]
    public async Task GetEnabledAsync_NeverSet_DefaultsToEnabled()
    {
        await using var scope = await CreateAsync();

        Assert.True(await scope.Store.GetEnabledAsync());
    }

    [Fact]
    public async Task SetEnabledAsync_PersistsLatestValue()
    {
        await using var scope = await CreateAsync();

        await scope.Store.SetEnabledAsync(false);
        Assert.False(await scope.Store.GetEnabledAsync());

        await scope.Store.SetEnabledAsync(true);
        Assert.True(await scope.Store.GetEnabledAsync());
    }

    private static PresenceSensorMapping Mapping(
        string entityId,
        string areaId,
        PresenceConfidence confidence = PresenceConfidence.Medium,
        bool isUserOverride = false) => new()
    {
        EntityId = entityId,
        AreaId = areaId,
        Confidence = confidence,
        IsUserOverride = isUserOverride,
    };
}
