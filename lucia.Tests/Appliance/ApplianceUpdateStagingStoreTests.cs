using lucia.AgentHost.Appliance;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests.Appliance;

public sealed class ApplianceUpdateStagingStoreTests
{
    [Fact]
    public void TryStart_SerializesAndPersistsStagingOperations()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"lucia-staging-{Guid.NewGuid():N}");
        try
        {
            var store = CreateStore(root);

            var accepted = store.TryStart("lucia", "v1.5.0");
            var rejected = store.TryStart("os", "v1.5.0");
            var recovered = CreateStore(root).GetStatus();

            Assert.NotNull(accepted);
            Assert.Null(rejected);
            Assert.Equal("failed", recovered.Status);
            Assert.Equal(
                "AgentHost restarted while staging the update.",
                recovered.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static ApplianceUpdateStagingStore CreateStore(string root) =>
        new(root, NullLogger<ApplianceUpdateStagingStore>.Instance);
}
