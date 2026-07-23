using Xunit;

namespace FalkForge.Extensions.DotNet.Tests;

public sealed class DotNetSearchPlannerTests
{
    [Fact]
    public void Plan_Runtime_X64_ResolvesNetCoreAppPathAndCoreclrSentinel()
    {
        var model = CreateModel(DotNetRuntimeType.Runtime, DotNetPlatform.X64, "8.0.0", "DOTNET8_FOUND");

        var plans = DotNetSearchPlanner.Plan([model]);

        var plan = Assert.Single(plans);
        Assert.Equal("DOTNET8_FOUND", plan.PropertyName);
        Assert.Equal(@"[ProgramFiles64Folder]dotnet\shared\Microsoft.NETCore.App", plan.Path);
        Assert.Equal("coreclr.dll", plan.FileName);
        Assert.Equal("8.0.0.0", plan.MinVersion);
    }

    [Fact]
    public void Plan_AspNetCore_ResolvesAspNetCoreAppPathAndSentinel()
    {
        var model = CreateModel(DotNetRuntimeType.AspNetCore, DotNetPlatform.X64, "8.0.0", "ASPNET8_FOUND");

        var plan = Assert.Single(DotNetSearchPlanner.Plan([model]));

        Assert.Equal(@"[ProgramFiles64Folder]dotnet\shared\Microsoft.AspNetCore.App", plan.Path);
        Assert.Equal("Microsoft.AspNetCore.dll", plan.FileName);
    }

    [Fact]
    public void Plan_WindowsDesktop_ResolvesWindowsDesktopAppPathAndSentinel()
    {
        var model = CreateModel(DotNetRuntimeType.WindowsDesktop, DotNetPlatform.X64, "8.0.0", "WPF8_FOUND");

        var plan = Assert.Single(DotNetSearchPlanner.Plan([model]));

        Assert.Equal(@"[ProgramFiles64Folder]dotnet\shared\Microsoft.WindowsDesktop.App", plan.Path);
        Assert.Equal("PresentationCore.dll", plan.FileName);
    }

    [Fact]
    public void Plan_X86_UsesProgramFilesFolder()
    {
        var model = CreateModel(DotNetRuntimeType.Runtime, DotNetPlatform.X86, "8.0.0", "DOTNET8_X86_FOUND");

        var plan = Assert.Single(DotNetSearchPlanner.Plan([model]));

        Assert.StartsWith("[ProgramFilesFolder]", plan.Path, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_Arm64_UsesProgramFiles64Folder()
    {
        var model = CreateModel(DotNetRuntimeType.Runtime, DotNetPlatform.Arm64, "8.0.0", "DOTNET8_ARM64_FOUND");

        var plan = Assert.Single(DotNetSearchPlanner.Plan([model]));

        Assert.StartsWith("[ProgramFiles64Folder]", plan.Path, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_MinVersion_NormalizesToFourParts()
    {
        var model = CreateModel(DotNetRuntimeType.Runtime, DotNetPlatform.X64, "8.0", "DOTNET8_FOUND");

        var plan = Assert.Single(DotNetSearchPlanner.Plan([model]));

        Assert.Equal("8.0.0.0", plan.MinVersion);
    }

    [Fact]
    public void Plan_TwoSearches_ProduceDistinctSignatureNames()
    {
        var first = CreateModel(DotNetRuntimeType.Runtime, DotNetPlatform.X64, "8.0.0", "DOTNET8_FOUND");
        var second = CreateModel(DotNetRuntimeType.AspNetCore, DotNetPlatform.X64, "8.0.0", "ASPNET8_FOUND");

        var plans = DotNetSearchPlanner.Plan([first, second]);

        Assert.Equal(2, plans.Count);
        Assert.NotEqual(plans[0].SignatureName, plans[1].SignatureName);
    }

    private static DotNetCoreSearchModel CreateModel(
        DotNetRuntimeType runtimeType, DotNetPlatform platform, string minVersion, string variableName) => new()
    {
        RuntimeType = runtimeType,
        Platform = platform,
        MinimumVersion = Version.Parse(minVersion),
        VariableName = variableName,
    };
}
