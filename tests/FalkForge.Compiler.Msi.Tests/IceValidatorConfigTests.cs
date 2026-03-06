using System.Runtime.Versioning;
using FalkForge.Compiler.Msi.Validation;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests;

[SupportedOSPlatform("windows")]
public sealed class IceValidatorConfigTests
{
    [Fact]
    public void Validate_DisabledConfig_ReturnsSuccessWithoutRunning()
    {
        var validator = new IceValidator();
        var config = new IceConfiguration { Enabled = false };

        var result = validator.Validate("nonexistent.msi", config);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsValid);
        Assert.Empty(result.Value.Messages);
    }

    [Fact]
    public void Validate_CustomCubPath_NonExistent_ReturnsFailure()
    {
        var validator = new IceValidator();
        var config = new IceConfiguration { CubFilePath = @"C:\nonexistent\darice.cub" };

        var tempMsi = Path.GetTempFileName();
        try
        {
            var result = validator.Validate(tempMsi, config);
            Assert.True(result.IsFailure);
            Assert.Equal(ErrorKind.FileNotFound, result.Error.Kind);
            Assert.Contains("CUB file not found", result.Error.Message);
        }
        finally
        {
            File.Delete(tempMsi);
        }
    }

    [Fact]
    public void Validate_NonExistentMsi_WithConfig_ReturnsFailure()
    {
        var validator = new IceValidator();
        var config = new IceConfiguration { CubFilePath = @"C:\some\darice.cub" };

        var result = validator.Validate("nonexistent.msi", config);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.FileNotFound, result.Error.Kind);
    }
}
