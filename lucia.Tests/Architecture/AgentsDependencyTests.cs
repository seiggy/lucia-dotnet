using lucia.Agents.Services;

namespace lucia.Tests.Architecture;

/// <summary>Keeps storage drivers out of the domain assembly. Providers live in lucia.Data.</summary>
public sealed class AgentsDependencyTests
{
    [Theory]
    [InlineData("StackExchange.Redis")]
    [InlineData("MongoDB.Driver")]
    [InlineData("MongoDB.Bson")]
    [InlineData("Npgsql")]
    [InlineData("Microsoft.Data.Sqlite")]
    public void LuciaAgents_DoesNotReferenceStorageDrivers(string assemblyName)
    {
        var referenced = typeof(EntityLocationService).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name);

        Assert.DoesNotContain(assemblyName, referenced);
    }
}