using System.Runtime.InteropServices;
using FalkForge.Compiler.Msix.Registry;
using Xunit;

namespace FalkForge.Compiler.Msix.Tests.Registry;

public sealed class RegistryHiveBuilderTests
{
    private static readonly bool OffregAvailable = ProbeOffreg();

    private static bool ProbeOffreg()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        if (!NativeLibrary.TryLoad("offreg.dll", out var handle))
            return false;

        NativeLibrary.Free(handle);
        return true;
    }

    [Fact]
    public void Build_EmptyEntries_ReturnsFailure()
    {
        var result = RegistryHiveBuilder.Build([]);

        Assert.True(result.IsFailure);
        Assert.Contains("No registry entries", result.Error.Message);
    }

    [Fact]
    public void Build_SingleStringEntry_CreatesHive()
    {
        if (!OffregAvailable)
            Assert.Skip("offreg.dll not available on this platform");

        var entries = new List<MsixRegistryEntry>
        {
            new()
            {
                Root = "HKCU",
                Key = "Software\\TestApp",
                ValueName = "DisplayName",
                Value = "Test Application",
                Type = MsixRegistryValueType.String
            }
        };

        var result = RegistryHiveBuilder.Build(entries);

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Value);
    }

    [Fact]
    public void Build_DWordEntry_CreatesHive()
    {
        if (!OffregAvailable)
            Assert.Skip("offreg.dll not available on this platform");

        var entries = new List<MsixRegistryEntry>
        {
            new()
            {
                Root = "HKCU",
                Key = "Software\\TestApp",
                ValueName = "Version",
                Value = "42",
                Type = MsixRegistryValueType.DWord
            }
        };

        var result = RegistryHiveBuilder.Build(entries);

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Value);
    }

    [Fact]
    public void Build_MultipleEntries_CreatesHive()
    {
        if (!OffregAvailable)
            Assert.Skip("offreg.dll not available on this platform");

        var entries = new List<MsixRegistryEntry>
        {
            new()
            {
                Root = "HKCU",
                Key = "Software\\TestApp",
                ValueName = "Name",
                Value = "Test",
                Type = MsixRegistryValueType.String
            },
            new()
            {
                Root = "HKCU",
                Key = "Software\\TestApp",
                ValueName = "Count",
                Value = "10",
                Type = MsixRegistryValueType.DWord
            },
            new()
            {
                Root = "HKCU",
                Key = "Software\\TestApp\\Sub",
                ValueName = "Path",
                Value = "%ProgramFiles%\\TestApp",
                Type = MsixRegistryValueType.ExpandString
            }
        };

        var result = RegistryHiveBuilder.Build(entries);

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Value);
    }

    [Fact]
    public void Build_HklmAndHkcuEntries_BothInHive()
    {
        if (!OffregAvailable)
            Assert.Skip("offreg.dll not available on this platform");

        var entries = new List<MsixRegistryEntry>
        {
            new()
            {
                Root = "HKCU",
                Key = "Software\\TestApp",
                ValueName = "UserSetting",
                Value = "user-value",
                Type = MsixRegistryValueType.String
            },
            new()
            {
                Root = "HKLM",
                Key = "Software\\TestApp",
                ValueName = "MachineSetting",
                Value = "machine-value",
                Type = MsixRegistryValueType.String
            }
        };

        var result = RegistryHiveBuilder.Build(entries);

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Value);
    }
}
