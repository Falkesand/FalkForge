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
}
