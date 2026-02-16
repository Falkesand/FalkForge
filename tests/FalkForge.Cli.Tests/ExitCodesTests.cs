using Xunit;

namespace FalkForge.Cli.Tests;

public sealed class ExitCodesTests
{
    [Fact]
    public void Success_IsZero()
    {
        Assert.Equal(0, ExitCodes.Success);
    }

    [Fact]
    public void ValidationFailure_IsOne()
    {
        Assert.Equal(1, ExitCodes.ValidationFailure);
    }

    [Fact]
    public void CompilationError_IsTwo()
    {
        Assert.Equal(2, ExitCodes.CompilationError);
    }

    [Fact]
    public void RuntimeError_IsThree()
    {
        Assert.Equal(3, ExitCodes.RuntimeError);
    }

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

    [Fact]
    public void FromResult_FailedResult_MapsErrorKind()
    {
        var result = Result<string>.Failure(ErrorKind.Validation, "bad input");

        Assert.Equal(1, ExitCodes.FromResult(result));
    }

    [Fact]
    public void FromResult_CompilationFailure_ReturnsTwo()
    {
        var result = Result<string>.Failure(ErrorKind.CompilationError, "compile failed");

        Assert.Equal(2, ExitCodes.FromResult(result));
    }

    [Fact]
    public void FromResult_RuntimeFailure_ReturnsThree()
    {
        var result = Result<string>.Failure(ErrorKind.FileNotFound, "not found");

        Assert.Equal(3, ExitCodes.FromResult(result));
    }
}
