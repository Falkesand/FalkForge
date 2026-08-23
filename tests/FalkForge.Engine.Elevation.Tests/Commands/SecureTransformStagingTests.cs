namespace FalkForge.Engine.Elevation.Tests.Commands;

using System.Runtime.Versioning;
using FalkForge.Engine.Elevation.Commands;
using FalkForge.TestSupport;
using Xunit;

/// <summary>
/// The elevated staging directory must resist a same-user attacker who owns <c>%ProgramData%\FalkForge</c>
/// (it is user-writable by design) and plants or swaps a junction on the staging path to redirect the
/// SYSTEM-generated transform into attacker-writable storage — the property-injection attack this whole
/// design exists to prevent. These tests plant a real junction (unelevated, via <c>mklink /J</c>) on the staging
/// path and prove the staging request fails closed and nothing is created in the redirected location. The
/// allowed root is injected so the walk can be exercised without writing under the real ProgramData.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SecureTransformStagingTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), $"SecureStaging_{Guid.NewGuid():N}");

    public SecureTransformStagingTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        // Remove any junction reparse points first so a recursive delete does not follow them.
        foreach (var junction in new[]
                 {
                     Path.Combine(_tempDir, "root", "FalkForge", "SecureTransforms"),
                     Path.Combine(_tempDir, "root", "FalkForge")
                 })
        {
            if (Directory.Exists(junction) &&
                new DirectoryInfo(junction).Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                try { Directory.Delete(junction); }
                catch (IOException)
                {
                    // Best-effort test cleanup.
                }
            }
        }

        TestTemp.TryDelete(_tempDir);
    }

    [Fact]
    public void CreateStagingDirectory_LeafIsAJunction_FailsClosed_AndNothingLandsInTheRedirectedTarget()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        var allowedRoot = Path.Combine(_tempDir, "root");
        var falkforge = Path.Combine(allowedRoot, "FalkForge");
        Directory.CreateDirectory(falkforge);
        var attackerTarget = Path.Combine(_tempDir, "attacker");
        Directory.CreateDirectory(attackerTarget);

        var staging = Path.Combine(falkforge, "SecureTransforms");
        if (!TestJunction.TryCreate(staging, attackerTarget))
            Assert.Skip("Could not create an NTFS directory junction in this environment.");

        var result = SecureTransformStaging.CreateStagingDirectory(staging, [allowedRoot]);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        // The attacker's directory received no staging subdirectory: the transform was NOT generated there.
        Assert.Empty(Directory.GetDirectories(attackerTarget));
        Assert.Empty(Directory.GetFiles(attackerTarget));
    }

    [Fact]
    public void CreateStagingDirectory_AncestorIsAJunction_FailsClosed_AndNothingLandsInTheRedirectedTarget()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        var allowedRoot = Path.Combine(_tempDir, "root");
        Directory.CreateDirectory(allowedRoot);
        var attackerTarget = Path.Combine(_tempDir, "attacker");
        Directory.CreateDirectory(attackerTarget);

        // The FalkForge ancestor itself is a junction to attacker-writable storage.
        var falkforge = Path.Combine(allowedRoot, "FalkForge");
        if (!TestJunction.TryCreate(falkforge, attackerTarget))
            Assert.Skip("Could not create an NTFS directory junction in this environment.");

        var staging = Path.Combine(falkforge, "SecureTransforms");

        var result = SecureTransformStaging.CreateStagingDirectory(staging, [allowedRoot]);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        // Nothing was created through the ancestor junction into the attacker's directory.
        Assert.Empty(Directory.GetDirectories(attackerTarget));
        Assert.Empty(Directory.GetFiles(attackerTarget));
    }

    [Fact]
    public void CreateStagingDirectory_OutsideAllowedRoot_FailsClosed()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        var staging = Path.Combine(_tempDir, "root", "FalkForge", "SecureTransforms");

        // Allowed root that does not contain the staging path.
        var result = SecureTransformStaging.CreateStagingDirectory(staging, [Path.Combine(_tempDir, "other")]);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
    }
}
