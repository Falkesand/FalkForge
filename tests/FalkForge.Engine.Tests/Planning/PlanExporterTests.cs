namespace FalkForge.Engine.Tests.Planning;

using FalkForge.Engine.Planning;
using FalkForge.Engine.Protocol.Manifest;
using System.Text.Json;
using Xunit;

public sealed class PlanExporterTests
{
    [Fact]
    public void ToJson_EmptyPlan_ProducesValidJson()
    {
        var plan = new InstallPlan { Actions = [] };
        var json = PlanExporter.ToJson(plan);

        Assert.False(string.IsNullOrEmpty(json));
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
    }

    [Fact]
    public void ToJson_WithActions_IncludesPackageInfo()
    {
        var plan = CreatePlanWithOneAction();

        var json = PlanExporter.ToJson(plan);

        Assert.Contains("packages", json);
    }

    [Fact]
    public void ToJson_WithActions_IncludesPackageIdAndActionType()
    {
        var plan = CreatePlanWithOneAction();

        var json = PlanExporter.ToJson(plan);

        Assert.Contains("TestPkg", json);
        Assert.Contains("Install", json);
    }

    [Fact]
    public void WriteToFile_WritesJsonFile()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".json");
        try
        {
            var plan = new InstallPlan { Actions = [] };
            var result = PlanExporter.WriteToFile(plan, tempPath);

            Assert.True(result.IsSuccess);
            Assert.True(File.Exists(tempPath));
            var content = File.ReadAllText(tempPath);
            Assert.False(string.IsNullOrEmpty(content));
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    private static InstallPlan CreatePlanWithOneAction()
    {
        var package = new PackageInfo
        {
            Id = "TestPkg",
            Type = PackageType.MsiPackage,
            DisplayName = "Test Package",
            SourcePath = @"C:\test\TestPkg.msi",
            Sha256Hash = "AABBCCDD"
        };

        var action = new PlanAction
        {
            PackageId = "TestPkg",
            ActionType = PlanActionType.Install,
            Package = package
        };

        return new InstallPlan { Actions = [action] };
    }
}
