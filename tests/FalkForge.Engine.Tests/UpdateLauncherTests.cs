namespace FalkForge.Engine.Tests;

using Xunit;

public sealed class UpdateLauncherTests
{
    [Fact]
    public void Launch_PathOutsideCacheRoot_ReturnsSecurityError()
    {
        var cacheRoot = Path.GetTempPath();
        var launcher = new DefaultUpdateLauncher(cacheRoot);
        // Construct a path that traverses outside cache root
        var outsidePath = Path.GetFullPath(Path.Combine(cacheRoot, "..", "evil.exe"));

        var result = launcher.Launch(outsidePath);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("UPD005", result.Error.Message);
    }

    [Fact]
    public void Launch_NonExistentFile_ReturnsEngineError()
    {
        var cacheRoot = Path.GetTempPath();
        var launcher = new DefaultUpdateLauncher(cacheRoot);
        var missingPath = Path.Combine(cacheRoot, "does-not-exist.exe");

        var result = launcher.Launch(missingPath);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorKind.EngineError, result.Error.Kind);
        Assert.Contains("UPD005", result.Error.Message);
    }

    [Fact]
    public void BuildRelaunchArguments_AlwaysAssertsRequireSigned()
    {
        // C14 Stage 2 / B2: the already-trusted, currently-installed engine makes the trust decision
        // for the downloaded update by asserting --require-signed when it relaunches the update bundle.
        // A stripped/unsigned or untrusted-signed update is then rejected by the relaunched bundle's
        // integrity gate before it extracts or executes anything.
        var args = DefaultUpdateLauncher.BuildRelaunchArguments("C:\\cache\\old-bundle.exe");

        Assert.Contains("--require-signed", args);

        // The base bundle is still forwarded so a delta update can reconstruct against it.
        var baseIdx = args.IndexOf("--base-bundle");
        Assert.True(baseIdx >= 0, "the base bundle path must still be forwarded for delta reconstruction");
        Assert.Equal("C:\\cache\\old-bundle.exe", args[baseIdx + 1]);
    }

    [Fact]
    public void BuildRelaunchArguments_NoBasePath_StillAssertsRequireSigned()
    {
        // Even when the running bundle path is unavailable (no delta base to forward), the update path
        // must remain require-signed — the trust rule is independent of delta reconstruction.
        var args = DefaultUpdateLauncher.BuildRelaunchArguments(null);

        Assert.Contains("--require-signed", args);
        Assert.DoesNotContain("--base-bundle", args);
    }
}
