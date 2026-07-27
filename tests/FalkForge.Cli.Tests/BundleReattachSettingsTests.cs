using FalkForge.Cli.Settings;
using Xunit;

namespace FalkForge.Cli.Tests;

public sealed class BundleReattachSettingsTests
{
    [Fact]
    public void Validate_AllPathsProvided_ReturnsSuccess()
    {
        var settings = new BundleReattachSettings
        {
            StubPath = "stub.exe",
            DataPath = "bundle.dat",
            OutputPath = "bundle.exe"
        };

        var result = settings.Validate();

        Assert.True(result.Successful);
    }

    [Fact]
    public void Validate_EmptyStubPath_ReturnsError()
    {
        var settings = new BundleReattachSettings
        {
            StubPath = "",
            DataPath = "bundle.dat",
            OutputPath = "bundle.exe"
        };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("Stub", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_WhitespaceOnlyStubPath_ReturnsError()
    {
        var settings = new BundleReattachSettings
        {
            StubPath = "   ",
            DataPath = "bundle.dat",
            OutputPath = "bundle.exe"
        };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("required", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_StubPathWithInvalidCharAtStart_ReturnsError()
    {
        // Invalid char at index 0 — kills IndexOfAny >= 0 → > 0 mutation.
        var settings = new BundleReattachSettings
        {
            StubPath = "\0stub.exe",
            DataPath = "bundle.dat",
            OutputPath = "bundle.exe"
        };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("invalid", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_StubPathWithInvalidCharMidString_ReturnsError()
    {
        var settings = new BundleReattachSettings
        {
            StubPath = "stu\0b.exe",
            DataPath = "bundle.dat",
            OutputPath = "bundle.exe"
        };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("invalid", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_EmptyDataPath_ReturnsError()
    {
        var settings = new BundleReattachSettings
        {
            StubPath = "stub.exe",
            DataPath = "",
            OutputPath = "bundle.exe"
        };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("Data", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_WhitespaceOnlyDataPath_ReturnsError()
    {
        var settings = new BundleReattachSettings
        {
            StubPath = "stub.exe",
            DataPath = "   ",
            OutputPath = "bundle.exe"
        };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("required", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_DataPathWithInvalidCharAtStart_ReturnsError()
    {
        // Invalid char at index 0 — kills IndexOfAny >= 0 → > 0 mutation.
        var settings = new BundleReattachSettings
        {
            StubPath = "stub.exe",
            DataPath = "\0bundle.dat",
            OutputPath = "bundle.exe"
        };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("invalid", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_DataPathWithInvalidCharMidString_ReturnsError()
    {
        var settings = new BundleReattachSettings
        {
            StubPath = "stub.exe",
            DataPath = "bund\0le.dat",
            OutputPath = "bundle.exe"
        };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("invalid", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_EmptyOutputPath_ReturnsError()
    {
        var settings = new BundleReattachSettings
        {
            StubPath = "stub.exe",
            DataPath = "bundle.dat",
            OutputPath = ""
        };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("Output", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_WhitespaceOnlyOutputPath_ReturnsError()
    {
        var settings = new BundleReattachSettings
        {
            StubPath = "stub.exe",
            DataPath = "bundle.dat",
            OutputPath = "   "
        };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("required", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_OutputPathWithInvalidCharAtStart_ReturnsError()
    {
        // Invalid char at index 0 — kills IndexOfAny >= 0 → > 0 mutation.
        var settings = new BundleReattachSettings
        {
            StubPath = "stub.exe",
            DataPath = "bundle.dat",
            OutputPath = "\0bundle.exe"
        };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("invalid", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_OutputPathWithInvalidCharMidString_ReturnsError()
    {
        var settings = new BundleReattachSettings
        {
            StubPath = "stub.exe",
            DataPath = "bundle.dat",
            OutputPath = "bund\0le.exe"
        };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("invalid", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Defaults_StubPath_IsEmpty()
    {
        var settings = new BundleReattachSettings();

        Assert.Equal(string.Empty, settings.StubPath);
    }

    [Fact]
    public void Defaults_DataPath_IsEmpty()
    {
        var settings = new BundleReattachSettings();

        Assert.Equal(string.Empty, settings.DataPath);
    }

    [Fact]
    public void Defaults_OutputPath_IsEmpty()
    {
        var settings = new BundleReattachSettings();

        Assert.Equal(string.Empty, settings.OutputPath);
    }
}
