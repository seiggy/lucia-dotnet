using lucia.Wyoming.Diarization;

namespace lucia.Tests.Data.Providers;

public abstract class SpeakerProfileStoreContract
{
    protected abstract Task<StoreScope<ISpeakerProfileStore>> CreateAsync();

    [Fact]
    public async Task GetAllAsync_EmptyStore_ReturnsEmpty()
    {
        await using var scope = await CreateAsync();

        Assert.Empty(await scope.Store.GetAllAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_AndGetAsync_RoundTripsAllMeaningfulFields()
    {
        await using var scope = await CreateAsync();

        var profile = CreateProfile("speaker-1", mergeTarget: "target-1");
        await scope.Store.CreateAsync(profile, CancellationToken.None);

        var stored = await scope.Store.GetAsync(profile.Id, CancellationToken.None);

        Assert.NotNull(stored);
        Assert.Equal(profile.Id, stored.Id);
        Assert.Equal(profile.Name, stored.Name);
        Assert.Equal(profile.IsProvisional, stored.IsProvisional);
        Assert.Equal(profile.IsAuthorized, stored.IsAuthorized);
        Assert.Equal(profile.AllowedSkills, stored.AllowedSkills);
        Assert.Equal(profile.Embeddings.Length, stored.Embeddings.Length);
        Assert.Equal(profile.Embeddings[0][0], stored.Embeddings[0][0]);
        Assert.Equal(profile.AverageEmbedding, stored.AverageEmbedding);
        Assert.Equal(profile.EnrollmentSessionId, stored.EnrollmentSessionId);
        Assert.Equal(profile.MergeTargetProfileId, stored.MergeTargetProfileId);
        Assert.Equal(profile.PendingMergeSourceIds, stored.PendingMergeSourceIds);
        Assert.Equal(profile.MergedProfileIds, stored.MergedProfileIds);
        Assert.Equal(profile.InteractionCount, stored.InteractionCount);
        Assert.Equal(profile.EnrolledAt, stored.EnrolledAt);
        Assert.Equal(profile.UpdatedAt, stored.UpdatedAt);
        Assert.Equal(profile.LastSeenAt, stored.LastSeenAt);
        Assert.Equal(profile.ExpiresAt, stored.ExpiresAt);
    }

    [Fact]
    public async Task UpdateAsync_ExistingProfile_ReplacesInsteadOfDuplicating()
    {
        await using var scope = await CreateAsync();

        var profile = CreateProfile("speaker-1");
        await scope.Store.CreateAsync(profile, CancellationToken.None);

        await scope.Store.UpdateAsync(
            profile with { Name = "Updated Speaker", InteractionCount = 77, IsProvisional = false },
            CancellationToken.None);

        var profiles = await scope.Store.GetAllAsync(CancellationToken.None);
        var stored = Assert.Single(profiles);
        Assert.Equal("Updated Speaker", stored.Name);
        Assert.False(stored.IsProvisional);
        Assert.Equal(77, stored.InteractionCount);
    }

    [Fact]
    public async Task UpdateAtomicAsync_MissingProfile_ReturnsNull()
    {
        await using var scope = await CreateAsync();

        var updated = await scope.Store.UpdateAtomicAsync(
            "missing-speaker",
            static profile => profile with { Name = "Updated" },
            CancellationToken.None);

        Assert.Null(updated);
    }

    [Fact]
    public async Task DeleteAsync_RemovesOnlyTargetProfile()
    {
        await using var scope = await CreateAsync();

        await scope.Store.CreateAsync(CreateProfile("speaker-1"), CancellationToken.None);
        await scope.Store.CreateAsync(CreateProfile("speaker-2"), CancellationToken.None);

        await scope.Store.DeleteAsync("speaker-1", CancellationToken.None);

        var profiles = await scope.Store.GetAllAsync(CancellationToken.None);
        var single = Assert.Single(profiles);
        Assert.Equal("speaker-2", single.Id);
    }

    [Fact]
    public async Task GetProvisionalProfilesAsync_AndGetEnrolledProfilesAsync_FilterCorrectly()
    {
        await using var scope = await CreateAsync();

        await scope.Store.CreateAsync(CreateProfile("speaker-1", isProvisional: true), CancellationToken.None);
        await scope.Store.CreateAsync(CreateProfile("speaker-2", isProvisional: false), CancellationToken.None);

        var provisional = await scope.Store.GetProvisionalProfilesAsync(CancellationToken.None);
        var enrolled = await scope.Store.GetEnrolledProfilesAsync(CancellationToken.None);

        Assert.Single(provisional);
        Assert.Equal("speaker-1", provisional[0].Id);
        Assert.Single(enrolled);
        Assert.Equal("speaker-2", enrolled[0].Id);
    }

    [Fact]
    public async Task GetExpiredProvisionalProfilesAsync_OnlyReturnsExpiredProfiles()
    {
        await using var scope = await CreateAsync();

        await scope.Store.CreateAsync(
            CreateProfile("speaker-1", isProvisional: true, lastSeenAt: DateTimeOffset.UtcNow.AddDays(-30)),
            CancellationToken.None);
        await scope.Store.CreateAsync(
            CreateProfile("speaker-2", isProvisional: true, lastSeenAt: DateTimeOffset.UtcNow.AddDays(-2)),
            CancellationToken.None);

        var expired = await scope.Store.GetExpiredProvisionalProfilesAsync(7, CancellationToken.None);

        var single = Assert.Single(expired);
        Assert.Equal("speaker-1", single.Id);
    }

    [Fact]
    public async Task UpdateAtomicAsync_ExistingProfile_AppliesTransformAndPersists()
    {
        await using var scope = await CreateAsync();
        await scope.Store.CreateAsync(CreateProfile("speaker-1"), CancellationToken.None);

        var updated = await scope.Store.UpdateAtomicAsync(
            "speaker-1",
            static profile => profile with { Name = "Renamed" },
            CancellationToken.None);

        Assert.NotNull(updated);
        Assert.Equal("Renamed", updated.Name);
        var stored = await scope.Store.GetAsync("speaker-1", CancellationToken.None);
        Assert.Equal("Renamed", stored?.Name);
    }

    [Fact]
    public async Task UpdateAtomicAsync_ConcurrentIncrements_LoseNoUpdates()
    {
        await using var scope = await CreateAsync();
        var profile = CreateProfile("speaker-1") with { InteractionCount = 0 };
        await scope.Store.CreateAsync(profile, CancellationToken.None);

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => scope.Store.UpdateAtomicAsync(
            "speaker-1",
            static current => current with { InteractionCount = current.InteractionCount + 1 },
            CancellationToken.None)));

        var stored = await scope.Store.GetAsync("speaker-1", CancellationToken.None);
        Assert.Equal(8, stored?.InteractionCount);
    }

    [Fact]
    public async Task UpdateAtomicAsync_ProfileBeingMerged_Throws()
    {
        await using var scope = await CreateAsync();
        await scope.Store.CreateAsync(CreateProfile("speaker-1", mergeTarget: "target-1"), CancellationToken.None);

        await Assert.ThrowsAsync<ProfileMergeConflictException>(() => scope.Store.UpdateAtomicAsync(
            "speaker-1",
            static profile => profile with { Name = "Blocked" },
            CancellationToken.None));
    }

    private static readonly DateTimeOffset Cutoff = new(2025, 3, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task DeleteExpiredProvisionalAsync_ExpiredProvisionalProfile_IsDeleted()
    {
        await using var scope = await CreateAsync();
        await scope.Store.CreateAsync(CreateProfile("speaker-1", lastSeenAt: Cutoff.AddDays(-1)), CancellationToken.None);

        var deleted = await Conditional(scope).DeleteExpiredProvisionalAsync("speaker-1", Cutoff, CancellationToken.None);

        Assert.True(deleted);
        Assert.Null(await scope.Store.GetAsync("speaker-1", CancellationToken.None));
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(false, -1)]
    public async Task DeleteExpiredProvisionalAsync_NotExpiredOrNotProvisional_KeepsTheProfile(bool isProvisional, int daysAfterCutoff)
    {
        await using var scope = await CreateAsync();
        await scope.Store.CreateAsync(
            CreateProfile("speaker-1", isProvisional, Cutoff.AddDays(daysAfterCutoff)),
            CancellationToken.None);

        var deleted = await Conditional(scope).DeleteExpiredProvisionalAsync("speaker-1", Cutoff, CancellationToken.None);

        Assert.False(deleted);
        Assert.NotNull(await scope.Store.GetAsync("speaker-1", CancellationToken.None));
    }

    [Fact]
    public async Task DeleteExpiredProvisionalAsync_MissingProfile_ReturnsFalse()
    {
        await using var scope = await CreateAsync();

        Assert.False(await Conditional(scope).DeleteExpiredProvisionalAsync("missing", Cutoff, CancellationToken.None));
    }

    private static IConditionalSpeakerProfileStore Conditional(StoreScope<ISpeakerProfileStore> scope)
        => Assert.IsAssignableFrom<IConditionalSpeakerProfileStore>(scope.Store);

    private static SpeakerProfile CreateProfile(
        string id,
        bool isProvisional = true,
        DateTimeOffset? lastSeenAt = null,
        string? mergeTarget = null)
    {
        var seenAt = lastSeenAt ?? new DateTimeOffset(2025, 2, 10, 12, 15, 0, TimeSpan.Zero);

        return new SpeakerProfile
        {
            Id = id,
            Name = id == "speaker-2" ? "Speaker Two" : "Speaker One",
            IsProvisional = isProvisional,
            IsAuthorized = true,
            AllowedSkills = ["skill-alpha", "skill-beta"],
            Embeddings = [[1.0f, 2.0f], [3.0f, 4.0f]],
            AverageEmbedding = [1.5f, 2.5f],
            EnrollmentSessionId = "session-abc",
            MergeTargetProfileId = mergeTarget,
            PendingMergeSourceIds = ["source-1", "source-2"],
            MergedProfileIds = ["merged-1"],
            InteractionCount = 42,
            EnrolledAt = new DateTimeOffset(2025, 1, 1, 8, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2025, 1, 10, 8, 0, 0, TimeSpan.Zero),
            LastSeenAt = seenAt,
            ExpiresAt = new DateTimeOffset(2025, 6, 1, 8, 0, 0, TimeSpan.Zero),
        };
    }
}
