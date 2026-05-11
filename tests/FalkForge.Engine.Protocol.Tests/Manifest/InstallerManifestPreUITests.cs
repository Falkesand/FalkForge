using System.Text.Json;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Manifest;

public sealed class InstallerManifestPreUITests
{
    [Fact]
    public void InstallerManifest_PreUIPackages_DefaultsToEmpty()
    {
        var manifest = CreateMinimalManifest();

        Assert.Empty(manifest.PreUIPackages);
    }

    [Fact]
    public void InstallerManifest_PreUIPackages_DeserializesToEmpty_WhenFieldMissing()
    {
        // Simulate old bundle that has no PreUIPackages field — back-compat
        var json = """
            {
              "Name": "TestApp",
              "Manufacturer": "Test",
              "Version": "1.0.0",
              "BundleId": "11111111-1111-1111-1111-111111111111",
              "UpgradeCode": "22222222-2222-2222-2222-222222222222",
              "Packages": [],
              "Scope": 0
            }
            """;

        var manifest = JsonSerializer.Deserialize<InstallerManifest>(json);

        Assert.NotNull(manifest);
        Assert.Empty(manifest.PreUIPackages);
    }

    [Fact]
    public void InstallerManifest_PreUIPackages_RoundTrips_WhenPresent()
    {
        var manifest = new InstallerManifest
        {
            Name = "TestApp",
            Manufacturer = "Test",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Packages = [],
            Scope = InstallScope.PerMachine,
            PreUIPackages =
            [
                new PreUIPackageInfo
                {
                    Id = "DotNet10Desktop",
                    DisplayName = ".NET 10 Desktop Runtime",
                    SourcePath = "dotnet.exe",
                    Sha256Hash = "ABCDEF1234567890ABCDEF1234567890ABCDEF1234567890ABCDEF1234567890",
                    Arguments = "/quiet"
                }
            ]
        };

        var json = JsonSerializer.Serialize(manifest);
        var deserialized = JsonSerializer.Deserialize<InstallerManifest>(json);

        Assert.NotNull(deserialized);
        Assert.Single(deserialized.PreUIPackages);
        Assert.Equal("DotNet10Desktop", deserialized.PreUIPackages[0].Id);
    }

    private static InstallerManifest CreateMinimalManifest() =>
        new InstallerManifest
        {
            Name = "TestApp",
            Manufacturer = "Test",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Packages = [],
            Scope = InstallScope.PerUser
        };
}
