using System.ComponentModel;
using System.Reflection;

using lucia.AgentHost.Appliance;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests.Appliance;

public sealed class ApplianceUpdateStagingStoreTests
{
    [Fact]
    public void Progress_SurvivesFailureAndReloadWithTheSameOperationId()
    {
        var root = Directory.CreateTempSubdirectory("lucia-progress-").FullName;
        try
        {
            var store = CreateStore(root);
            var operation = store.TryStart("os", "v1.5.0");
            Assert.NotNull(operation);
            store.SetRunning("os", "v1.5.0");
            store.SetProgress("downloading", 30, 100);
            store.SetProgress("downloading", 20, 100);
            Assert.Equal(30, store.GetStatus().CompletedBytes);
            store.SetFailed("os", "v1.5.0", "connection failed");
            var reloaded = CreateStore(root).GetStatus();
            Assert.Equal(operation.OperationId, reloaded.OperationId);
            Assert.Equal("failed", reloaded.Status);
            Assert.Equal("downloading", reloaded.Phase);
            Assert.Equal(30, reloaded.CompletedBytes);
            Assert.Equal(100, reloaded.TotalBytes);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void SyncDirectory_UsesTheNativeDirectoryApi(bool regularFile)
    {
        Skip.IfNot(OperatingSystem.IsLinux(), "Directory fsync is Linux-specific.");
        var root = Directory.CreateTempSubdirectory("lucia-directory-sync-").FullName;
        try
        {
            var path = regularFile ? Path.Combine(root, "not-a-directory") : root;
            if (regularFile)
            {
                File.WriteAllText(path, "memory");
            }
            var sync = typeof(ApplianceUpdateStagingStore).GetMethod(
                "SyncDirectory", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(sync);

            var exception = Record.Exception(() => sync.Invoke(null, [path]));

            if (regularFile)
            {
                var invocation = Assert.IsType<TargetInvocationException>(exception);
                Assert.Equal(20, Assert.IsType<Win32Exception>(invocation.InnerException).NativeErrorCode);
            }
            else
            {
                Assert.Null(exception);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

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
            Assert.True(Guid.TryParseExact(
                accepted.OperationId,
                "D",
                out _));
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

    [Fact]
    public void TryStart_PersistenceFailureKeepsIdleState()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"lucia-staging-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "operation.json"));
        try
        {
            var store = CreateStore(root);

            var exception = Record.Exception(
                () => store.TryStart("lucia", "v1.5.0"));
            Assert.True(
                exception is IOException or UnauthorizedAccessException,
                exception?.ToString());
            Assert.Equal("idle", store.GetStatus().Status);

            Directory.Delete(Path.Combine(root, "operation.json"));
            Assert.NotNull(store.TryStart("lucia", "v1.5.0"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SetRunning_PersistenceFailureCanTransitionToFailed()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"lucia-staging-{Guid.NewGuid():N}");
        try
        {
            var store = CreateStore(root);
            Assert.NotNull(store.TryStart("lucia", "v1.5.0"));
            Directory.CreateDirectory(Path.Combine(root, "operation.json.tmp"));

            var exception = Record.Exception(
                () => store.SetRunning("lucia", "v1.5.0"));
            Assert.True(
                exception is IOException or UnauthorizedAccessException,
                exception?.ToString());
            Assert.Equal("queued", store.GetStatus().Status);

            store.SetFailedInMemory(
                "lucia",
                "v1.5.0",
                "persistence failed");
            Assert.Equal("failed", store.GetStatus().Status);
            Directory.Delete(Path.Combine(root, "operation.json.tmp"));
            Assert.NotNull(store.TryStart("os", "v1.6.0"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static ApplianceUpdateStagingStore CreateStore(string root) =>
        new(root, NullLogger<ApplianceUpdateStagingStore>.Instance);
}
