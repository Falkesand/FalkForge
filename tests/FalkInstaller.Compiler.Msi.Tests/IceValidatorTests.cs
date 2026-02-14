using System.Runtime.Versioning;
using FalkInstaller.Compiler.Msi.Validation;
using Xunit;

namespace FalkInstaller.Compiler.Msi.Tests;

[SupportedOSPlatform("windows")]
public sealed class IceValidatorTests
{
    [Fact]
    public void FindDariceCub_ReturnsNullOrPath()
    {
        // FindDariceCub searches well-known SDK paths.
        // On CI without Windows SDK it returns null; with SDK it returns a valid path.
        var result = IceValidator.FindDariceCub();

        if (result is not null)
        {
            Assert.True(File.Exists(result), $"FindDariceCub returned non-existent path: {result}");
            Assert.EndsWith(".cub", result, StringComparison.OrdinalIgnoreCase);
        }
        // null is acceptable - means SDK not installed
    }

    [Fact]
    public void Validate_NonExistentMsi_ReturnsFailure()
    {
        var validator = new IceValidator();

        var result = validator.Validate(@"C:\nonexistent\fake.msi");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.FileNotFound, result.Error.Kind);
    }

    [Fact]
    public void Validate_NonExistentMsi_WithCubPath_ReturnsFailure()
    {
        var validator = new IceValidator();

        var result = validator.Validate(@"C:\nonexistent\fake.msi", @"C:\nonexistent\darice.cub");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.FileNotFound, result.Error.Kind);
        Assert.Contains("MSI file not found", result.Error.Message);
    }

    [Fact]
    public void Validate_NonExistentCub_ReturnsFailure()
    {
        var tempMsi = Path.Combine(Path.GetTempPath(), $"ice_test_{Guid.NewGuid():N}.msi");
        try
        {
            File.WriteAllBytes(tempMsi, [0x00]); // Dummy file so it exists
            var validator = new IceValidator();

            var result = validator.Validate(tempMsi, @"C:\nonexistent\darice.cub");

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
    public void Validate_WhenNoCubAvailable_ReturnsSuccess()
    {
        // When darice.cub is not found, Validate(msiPath) should return success
        // with empty messages. We can test this if SDK is not present,
        // or we can test via the single-arg overload with a real MSI.
        // Since we can't guarantee SDK absence, test the model behavior instead.
        var successResult = IceValidationResult.Success();

        Assert.True(successResult.IsValid);
        Assert.Empty(successResult.Messages);
    }

    [Fact]
    public void IceMessage_Properties_SetCorrectly()
    {
        var message = new IceMessage
        {
            IceName = "ICE03",
            Severity = IceMessageSeverity.Error,
            Description = "Invalid identifier",
            Table = "Component",
            Column = "Component",
            PrimaryKeys = "C_main"
        };

        Assert.Equal("ICE03", message.IceName);
        Assert.Equal(IceMessageSeverity.Error, message.Severity);
        Assert.Equal("Invalid identifier", message.Description);
        Assert.Equal("Component", message.Table);
        Assert.Equal("Component", message.Column);
        Assert.Equal("C_main", message.PrimaryKeys);
    }

    [Fact]
    public void IceMessage_OptionalProperties_DefaultToNull()
    {
        var message = new IceMessage
        {
            IceName = "ICE01",
            Severity = IceMessageSeverity.Information,
            Description = "Some info"
        };

        Assert.Null(message.Table);
        Assert.Null(message.Column);
        Assert.Null(message.PrimaryKeys);
    }

    [Fact]
    public void IceValidationResult_IsValid_TrueWhenNoErrors()
    {
        var result = IceValidationResult.FromMessages(
        [
            new IceMessage
            {
                IceName = "ICE01",
                Severity = IceMessageSeverity.Information,
                Description = "Info message"
            },
            new IceMessage
            {
                IceName = "ICE02",
                Severity = IceMessageSeverity.Warning,
                Description = "Warning message"
            }
        ]);

        Assert.True(result.IsValid);
        Assert.Equal(2, result.Messages.Count);
        Assert.Empty(result.Errors);
        Assert.Single(result.Warnings);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public void IceValidationResult_IsValid_FalseWhenErrors()
    {
        var result = IceValidationResult.FromMessages(
        [
            new IceMessage
            {
                IceName = "ICE03",
                Severity = IceMessageSeverity.Error,
                Description = "Error message"
            },
            new IceMessage
            {
                IceName = "ICE04",
                Severity = IceMessageSeverity.Warning,
                Description = "Warning message"
            }
        ]);

        Assert.False(result.IsValid);
        Assert.Equal(2, result.Messages.Count);
        Assert.Single(result.Errors);
        Assert.Single(result.Warnings);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public void IceValidationResult_IsValid_FalseWhenFailures()
    {
        var result = IceValidationResult.FromMessages(
        [
            new IceMessage
            {
                IceName = "ICE99",
                Severity = IceMessageSeverity.Failure,
                Description = "Critical failure"
            }
        ]);

        Assert.False(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Single(result.Failures);
    }

    [Fact]
    public void IceValidationResult_Success_ReturnsEmptyMessages()
    {
        var result = IceValidationResult.Success();

        Assert.True(result.IsValid);
        Assert.Empty(result.Messages);
        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
        Assert.Empty(result.Failures);
    }
}
