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

    /// <summary>
    /// When darice.cub cannot be found and SkipWhenCubUnavailable is false (strict default),
    /// Validate must return a typed failure so the caller knows ICE was not checked.
    /// This ensures MSIs never ship with silently-skipped ICE validation.
    /// </summary>
    [Fact]
    public void Validate_CubNotFound_StrictDefault_ReturnsValidationFailure()
    {
        var validator = new IceValidator();
        // Only meaningful when darice.cub is absent. Skip if cub is present on this machine.
        if (IceValidator.FindDariceCub() is not null)
            return;

        var tempMsi = Path.GetTempFileName();
        try
        {
            var config = new IceConfiguration
            {
                // SkipWhenCubUnavailable = false is the default (strict)
                SkipWhenCubUnavailable = false
            };

            var result = validator.Validate(tempMsi, config);

            Assert.True(result.IsFailure, "Should fail loud when darice.cub is absent and strict mode is on");
            Assert.Equal(ErrorKind.Validation, result.Error.Kind);
            Assert.Contains("darice.cub", result.Error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(tempMsi);
        }
    }

    /// <summary>
    /// When SkipWhenCubUnavailable is explicitly set to true (opt-out / lenient),
    /// a missing cub returns silent success — preserving the old behavior for
    /// environments that genuinely lack the Windows SDK.
    /// </summary>
    [Fact]
    public void Validate_CubNotFound_SkipWhenCubUnavailable_ReturnsSuccess()
    {
        var validator = new IceValidator();
        // Only meaningful when darice.cub is absent; skip if present.
        if (IceValidator.FindDariceCub() is not null)
            return;

        var tempMsi = Path.GetTempFileName();
        try
        {
            var config = new IceConfiguration
            {
                SkipWhenCubUnavailable = true
            };

            var result = validator.Validate(tempMsi, config);

            Assert.True(result.IsSuccess, "Should silently succeed when cub is absent and lenient mode is on");
            Assert.True(result.Value.IsValid);
            Assert.Empty(result.Value.Messages);
        }
        finally
        {
            File.Delete(tempMsi);
        }
    }

    /// <summary>
    /// The parameterless Validate overload (legacy) retains lenient behavior —
    /// it always skips when cub is absent. Existing callers are unaffected.
    /// </summary>
    [Fact]
    public void Validate_ParameterlessOverload_CubNotFound_ReturnsSuccess()
    {
        var validator = new IceValidator();
        if (IceValidator.FindDariceCub() is not null)
            return;

        var tempMsi = Path.GetTempFileName();
        try
        {
            // The no-config overload must remain lenient for backward compatibility.
            var result = validator.Validate(tempMsi);

            Assert.True(result.IsSuccess, "Parameterless overload must remain lenient when cub absent");
        }
        finally
        {
            File.Delete(tempMsi);
        }
    }
}
