using Xunit;

namespace FalkForge.Cli.Tests;

public sealed class ExitCodesTests
{
    [Theory]
    [InlineData(ErrorKind.Validation, 1)]
    [InlineData(ErrorKind.InvalidConfiguration, 1)]
    [InlineData(ErrorKind.CompilationError, 2)]
    [InlineData(ErrorKind.FileNotFound, 3)]
    [InlineData(ErrorKind.DirectoryNotFound, 3)]
    [InlineData(ErrorKind.IoError, 3)]
    [InlineData(ErrorKind.SecurityError, 3)]
    [InlineData(ErrorKind.PlatformError, 3)]
    [InlineData(ErrorKind.InvalidOperation, 3)]
    [InlineData(ErrorKind.NotSupported, 3)]
    [InlineData(ErrorKind.ExecutionError, 3)]
    [InlineData(ErrorKind.BundleError, 3)]
    public void FromErrorKind_MapsCorrectly(ErrorKind kind, int expectedExitCode)
    {
        Assert.Equal(expectedExitCode, ExitCodes.FromErrorKind(kind));
    }

    [Fact]
    public void FromResult_SuccessfulResult_ReturnsZero()
    {
        var result = Result<string>.Success("test");

        Assert.Equal(0, ExitCodes.FromResult(result));
    }
}
