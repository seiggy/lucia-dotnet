namespace lucia.PlaywrightTests.Infrastructure;

/// <summary>
/// xUnit collection definition that shares a single Playwright browser across all tests.
/// All test classes using [Collection(TestCollections.Playwright)] share the same fixture.
/// </summary>
[CollectionDefinition(TestCollections.Playwright)]
public sealed class PlaywrightCollection : ICollectionFixture<PlaywrightFixture>;

public static class TestCollections
{
    public const string Playwright = "Playwright";
}
