using FalkForge.Cli.Settings;
using Xunit;

namespace FalkForge.Cli.Tests;

public sealed class BundleDetachSettingsTests
{
    [Fact]
    public void Validate_AllPathsProvided_ReturnsSuccess()
    {
        var settings = new BundleDetachSettings
        {
            BundlePath = "bundle.exe",
            StubPath = "stub.exe",
            DataPath = "bundle.dat"
        };

        var result = settings.Validate();

        Assert.True(result.Successful);
    }

    [Fact]
    public void Validate_EmptyBundlePath_ReturnsError()
    {
        var settings = new BundleDetachSettings
        {
            BundlePath = "",
            StubPath = "stub.exe",
            DataPath = "bundle.dat"
        };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("Bundle path", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_WhitespaceOnlyBundlePath_ReturnsError()
    {
        var settings = new BundleDetachSettings
        {
            BundlePath = "   ",
            StubPath = "stub.exe",
            DataPath = "bundle.dat"
        };

        var result = settings.Validate();

        Assert.False(result.Successful);
    }

    [Fact]
    public void Validate_BundlePathWithInvalidCharAtStart_ReturnsError()
    {
        // Invalid char at index 0 — kills IndexOfAny >= 0 → > 0 mutation
        var settings = new BundleDetachSettings
        {
            BundlePath = "\0bundle.exe",
            StubPath = "stub.exe",
            DataPath = "bundle.dat"
        };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("invalid", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_BundlePathWithInvalidChars_ReturnsError()
    {
        var settings = new BundleDetachSettings
        {
            BundlePath = "bun\0dle.exe",
            StubPath = "stub.exe",
            DataPath = "bundle.dat"
        };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("invalid", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_EmptyStubPath_ReturnsError()
    {
        var settings = new BundleDetachSettings
        {
            BundlePath = "bundle.exe",
            StubPath = "",
            DataPath = "bundle.dat"
        };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("Stub", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_StubPathWithInvalidCharAtStart_ReturnsError()
    {
        // Invalid char at index 0 — kills IndexOfAny >= 0 → > 0 mutation
        var settings = new BundleDetachSettings
        {
            BundlePath = "bundle.exe",
            StubPath = "\0stub.exe",
            DataPath = "bundle.dat"
        };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("invalid", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_StubPathWithInvalidCharMidString_ReturnsError()
    {
        var settings = new BundleDetachSettings
        {
            BundlePath = "bundle.exe",
            StubPath = "stu\0b.exe",
            DataPath = "bundle.dat"
        };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("invalid", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_EmptyDataPath_ReturnsError()
    {
        var settings = new BundleDetachSettings
        {
            BundlePath = "bundle.exe",
            StubPath = "stub.exe",
            DataPath = ""
        };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("Data", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_DataPathWithInvalidCharAtStart_ReturnsError()
    {
        // Invalid char at index 0 — kills IndexOfAny >= 0 → > 0 mutation
        var settings = new BundleDetachSettings
        {
            BundlePath = "bundle.exe",
            StubPath = "stub.exe",
            DataPath = "\0bundle.dat"
        };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("invalid", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_DataPathWithInvalidCharMidString_ReturnsError()
    {
        var settings = new BundleDetachSettings
        {
            BundlePath = "bundle.exe",
            StubPath = "stub.exe",
            DataPath = "bund\0le.dat"
        };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("invalid", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Defaults_BundlePath_IsEmpty()
    {
        var settings = new BundleDetachSettings();

        Assert.Equal(string.Empty, settings.BundlePath);
    }

    [Fact]
    public void Defaults_StubPath_IsEmpty()
    {
        var settings = new BundleDetachSettings();

        Assert.Equal(string.Empty, settings.StubPath);
    }

    [Fact]
    public void Defaults_DataPath_IsEmpty()
    {
        var settings = new BundleDetachSettings();

        Assert.Equal(string.Empty, settings.DataPath);
    }
}
