using System.Text.Json;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Manifest;

public sealed class PreUIPackageInfoRoundTripTests
{
    [Fact]
    public void PreUIPackageInfo_Serializes_Roundtrip()
    {
        var original = new PreUIPackageInfo
        {
            Id = "DotNet10Desktop",
            DisplayName = ".NET 10 Desktop Runtime (x64)",
            SourcePath = "dotnet-runtime-10.0-win-x64.exe",
            Sha256Hash = "ABCDEF1234567890ABCDEF1234567890ABCDEF1234567890ABCDEF1234567890",
            Arguments = "/quiet /norestart",
            SearchConditions =
            [
                new SearchCondition
                {
                    Type = SearchConditionType.RegistryValue,
                    Path = @"HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\x64",
                    Value = "10.0.0",
                    Comparison = "="
                }
            ],
            DownloadUrl = "https://download.microsoft.com/dotnet-runtime-10.0.0-win-x64.exe",
            Size = 56_700_000,
            RebootBehavior = PreUIRebootBehavior.IgnoreAndContinue,
            PayloadMode = PreUIPayloadMode.Remote
        };

        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<PreUIPackageInfo>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(original.Id, deserialized.Id);
        Assert.Equal(original.DisplayName, deserialized.DisplayName);
        Assert.Equal(original.SourcePath, deserialized.SourcePath);
        Assert.Equal(original.Sha256Hash, deserialized.Sha256Hash);
        Assert.Equal(original.Arguments, deserialized.Arguments);
        Assert.Single(deserialized.SearchConditions);
        Assert.Equal(original.SearchConditions[0].Type, deserialized.SearchConditions[0].Type);
        Assert.Equal(original.SearchConditions[0].Path, deserialized.SearchConditions[0].Path);
        Assert.Equal(original.DownloadUrl, deserialized.DownloadUrl);
        Assert.Equal(original.Size, deserialized.Size);
        Assert.Equal(original.RebootBehavior, deserialized.RebootBehavior);
        Assert.Equal(original.PayloadMode, deserialized.PayloadMode);
    }

    [Fact]
    public void PreUIPackageInfo_Defaults_AreCorrect()
    {
        var info = new PreUIPackageInfo
        {
            Id = "TestPrereq",
            DisplayName = "Test Prerequisite",
            SourcePath = "prereq.exe",
            Sha256Hash = "ABCDEF1234567890ABCDEF1234567890ABCDEF1234567890ABCDEF1234567890",
            Arguments = "/quiet"
        };

        Assert.Equal(PreUIRebootBehavior.IgnoreAndContinue, info.RebootBehavior);
        Assert.Equal(PreUIPayloadMode.Embedded, info.PayloadMode);
        Assert.Empty(info.SearchConditions);
        Assert.Null(info.DownloadUrl);
        Assert.Equal(0L, info.Size);
    }
}
