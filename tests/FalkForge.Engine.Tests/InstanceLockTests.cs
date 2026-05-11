namespace FalkForge.Engine.Tests;

using FalkForge.Engine;
using Xunit;

/// <summary>
/// Verifies that InstanceLock prevents two concurrent engine instances from running
/// against the same bundle, ensuring they never race on the package cache or MSI.
/// </summary>
public sealed class InstanceLockTests
{
    private static readonly string TestBundleId = $"test-bundle-{Guid.NewGuid():N}";

    [Fact]
    public void TryAcquire_FirstCaller_Succeeds()
    {
        var acquired = InstanceLock.TryAcquire(TestBundleId, out var handle);

        try
        {
            Assert.True(acquired);
            Assert.NotNull(handle);
        }
        finally
        {
            handle?.Dispose();
        }
    }

    [Fact]
    public void TryAcquire_SecondCaller_FailsWhileFirstHeld()
    {
        var bundleId = $"test-bundle-{Guid.NewGuid():N}";

        var first = InstanceLock.TryAcquire(bundleId, out var firstHandle);
        Assert.True(first, "First acquisition must succeed");

        try
        {
            var second = InstanceLock.TryAcquire(bundleId, out var secondHandle);

            try
            {
                Assert.False(second, "Second acquisition must fail while first is held");
                Assert.Null(secondHandle);
            }
            finally
            {
                secondHandle?.Dispose();
            }
        }
        finally
        {
            firstHandle?.Dispose();
        }
    }

    [Fact]
    public void TryAcquire_AfterRelease_Succeeds()
    {
        var bundleId = $"test-bundle-{Guid.NewGuid():N}";

        var first = InstanceLock.TryAcquire(bundleId, out var firstHandle);
        Assert.True(first);
        using (firstHandle) { /* release the lock */ }

        var second = InstanceLock.TryAcquire(bundleId, out var secondHandle);
        try
        {
            Assert.True(second, "Acquisition after release must succeed");
            Assert.NotNull(secondHandle);
        }
        finally
        {
            secondHandle?.Dispose();
        }
    }

    [Fact]
    public void TryAcquire_DifferentBundleIds_BothSucceed()
    {
        var bundleA = $"bundle-a-{Guid.NewGuid():N}";
        var bundleB = $"bundle-b-{Guid.NewGuid():N}";

        var acquiredA = InstanceLock.TryAcquire(bundleA, out var handleA);
        var acquiredB = InstanceLock.TryAcquire(bundleB, out var handleB);

        try
        {
            Assert.True(acquiredA, "Bundle A lock must be acquired");
            Assert.True(acquiredB, "Bundle B lock must be acquired independently");
        }
        finally
        {
            handleA?.Dispose();
            handleB?.Dispose();
        }
    }
}
