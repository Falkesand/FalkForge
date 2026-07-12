using FalkForge.Compiler.Bundle.Builders;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Builders;

public sealed class PreUIPackageBuilderTests
{
    [Fact]
    public void PreUIPackageBuilder_Build_PopulatesAllFields()
    {
        var builder = new PreUIPackageBuilder("dotnet-runtime-10.0-win-x64.exe")
            .Id("DotNet10Desktop")
            .DisplayName(".NET 10 Desktop Runtime (x64)")
            .Arguments("/quiet /norestart")
            .SearchCondition(sc => sc.RegistryValue(
                RegistryRoot.LocalMachine,
                @"SOFTWARE\dotnet\Setup\InstalledVersions\x64",
                "10.0.0",
                "=",
                "10.0.0"))
            .RebootBehavior(PreUIRebootBehavior.IgnoreAndContinue);

        var model = builder.Build();

        Assert.Equal("DotNet10Desktop", model.Id);
        Assert.Equal(".NET 10 Desktop Runtime (x64)", model.DisplayName);
        Assert.Equal("dotnet-runtime-10.0-win-x64.exe", model.SourcePath);
        Assert.Equal("/quiet /norestart", model.Arguments);
        Assert.Single(model.SearchConditions);
        Assert.Equal(SearchConditionType.RegistryValue, model.SearchConditions[0].Type);
        Assert.Equal(@"HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\x64", model.SearchConditions[0].Path);
        Assert.Equal(PreUIRebootBehavior.IgnoreAndContinue, model.RebootBehavior);
        Assert.Equal(PreUIPayloadMode.Embedded, model.PayloadMode);
        Assert.Null(model.RemotePayload);
    }

    [Fact]
    public void PreUIPackageBuilder_RemotePayload_SetsPayloadMode()
    {
        var builder = new PreUIPackageBuilder("dotnet-runtime-10.0-win-x64.exe")
            .Id("DotNet10Desktop")
            .DisplayName(".NET 10 Desktop Runtime")
            .Arguments("/quiet")
            .RemotePayload(
                "https://download.microsoft.com/dotnet-runtime-10.0.0-win-x64.exe",
                "ABCDEF1234567890ABCDEF1234567890ABCDEF1234567890ABCDEF1234567890",
                56_700_000L);

        var model = builder.Build();

        Assert.Equal(PreUIPayloadMode.Remote, model.PayloadMode);
        Assert.NotNull(model.RemotePayload);
        Assert.Equal("https://download.microsoft.com/dotnet-runtime-10.0.0-win-x64.exe", model.RemotePayload.DownloadUrl);
        Assert.Equal("ABCDEF1234567890ABCDEF1234567890ABCDEF1234567890ABCDEF1234567890", model.RemotePayload.Sha256Hash);
        Assert.Equal(56_700_000L, model.RemotePayload.Size);
    }

    [Fact]
    public void PreUIPackageBuilder_DefaultId_UsesFilenameWithoutExtension()
    {
        var builder = new PreUIPackageBuilder("dotnet-runtime-10.0-win-x64.exe")
            .DisplayName("Test")
            .Arguments("/quiet");

        var model = builder.Build();

        // Default Id derived from filename without extension
        Assert.Equal("dotnet-runtime-10.0-win-x64", model.Id);
    }

    [Fact]
    public void PreUIPackageBuilder_MultipleSearchConditions_AllStored()
    {
        var builder = new PreUIPackageBuilder("prereq.exe")
            .Id("TestPrereq")
            .DisplayName("Test")
            .Arguments("/q")
            .SearchCondition(sc => sc.RegistryValue(RegistryRoot.LocalMachine, @"SOFTWARE\Key1", "v1", "=", "v1"))
            .SearchCondition(sc => sc.FileExists(@"C:\Windows\System32\mfc42.dll"));

        var model = builder.Build();

        Assert.Equal(2, model.SearchConditions.Count);
        Assert.Equal(SearchConditionType.RegistryValue, model.SearchConditions[0].Type);
        Assert.Equal(SearchConditionType.FileExists, model.SearchConditions[1].Type);
    }

    [Fact]
    public void PreUIPackageBuilder_NoExitCode_ModelHasNullExitCodes()
    {
        var builder = new PreUIPackageBuilder("prereq.exe")
            .Id("TestPrereq")
            .DisplayName("Test")
            .Arguments("/q");

        var model = builder.Build();

        Assert.Null(model.ExitCodes);
    }

    [Fact]
    public void PreUIPackageBuilder_ExitCode_PopulatesExitCodesMap()
    {
        var builder = new PreUIPackageBuilder("prereq.exe")
            .Id("TestPrereq")
            .DisplayName("Test")
            .Arguments("/q")
            .ExitCode(3010, ExitCodeBehavior.ScheduleReboot)
            .ExitCode(1641, ExitCodeBehavior.RebootRequired);

        var model = builder.Build();

        Assert.NotNull(model.ExitCodes);
        Assert.Equal(2, model.ExitCodes.Count);
        Assert.Equal(ExitCodeBehavior.ScheduleReboot, model.ExitCodes[3010]);
        Assert.Equal(ExitCodeBehavior.RebootRequired, model.ExitCodes[1641]);
    }
}
