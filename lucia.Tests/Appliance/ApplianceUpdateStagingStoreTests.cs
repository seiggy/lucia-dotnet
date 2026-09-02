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
            var orphan = Path.Combine(root, ".v1.4.0.deadbeef.partial");
            Directory.CreateDirectory(orphan);
            var store = CreateStore(root);

            var accepted = store.TryStart("lucia", "v1.5.0");
            var rejected = store.TryStart("os", "v1.5.0");
            var abandonedFinal = Path.Combine(root, "v1.5.0");
            Directory.CreateDirectory(abandonedFinal);
            var recovered = CreateStore(root).GetStatus();

            Assert.NotNull(accepted);
            Assert.Null(rejected);
            Assert.False(Directory.Exists(orphan));
            Assert.False(Directory.Exists(abandonedFinal));
            Assert.Equal("failed", recovered.Status);
            Assert.Equal(
                "AgentHost restarted while staging the update.",
                recovered.Message);

            store.SetHandedOff("lucia", "v1.5.0");
            Assert.Null(store.TryStart("os", "v1.5.0"));
            Assert.Equal("running", CreateStore(root).GetStatus().Status);

            store.SetHandingOff("os", "v1.6.0");
            Assert.True(store.IsHandoffRequestActive);
            Assert.Null(store.TryStart("lucia", "v1.6.0"));
            store.CompleteHandoffAttempt();
            Assert.False(store.IsHandoffRequestActive);
            Assert.Equal("handoff", CreateStore(root).GetStatus().Action);

            var finalized = Path.Combine(root, "v1.6.0");
            Directory.CreateDirectory(finalized);
            store.Clear();
            Assert.False(Directory.Exists(finalized));

            store.SetFailed("lucia", "v1.7.0", "rejected");
            var rejectedFinal = Path.Combine(root, "v1.7.0");
            Directory.CreateDirectory(rejectedFinal);
            Assert.NotNull(store.TryStart("os", "v1.8.0"));
            Assert.False(Directory.Exists(rejectedFinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static ApplianceUpdateStagingStore CreateStore(string root) =>
        new(root, NullLogger<ApplianceUpdateStagingStore>.Instance);
}
