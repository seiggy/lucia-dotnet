namespace lucia.Tests.Orchestration;

/// <summary>
/// xUnit collection definition that shares a single <see cref="EvalTestFixture"/>
/// across all evaluation test classes, avoiding redundant Azure client creation.
/// </summary>
[CollectionDefinition(Name)]
public sealed class EvalTestCollection : ICollectionFixture<EvalTestFixture>
{
    public const string Name = "EvalTests";
}
