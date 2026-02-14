namespace FalkInstaller.Engine.Tests.Mocks;

using FalkInstaller.Engine.Protocol.Manifest;

internal static class TestManifestFactory
{
    public static InstallerManifest CreateSimple(
        string name = "TestApp",
        string version = "1.0.0",
        InstallScope scope = InstallScope.PerUser,
        params PackageInfo[] packages)
    {
        return new InstallerManifest
        {
            Name = name,
            Manufacturer = "Test Manufacturer",
            Version = version,
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = scope,
            Packages = packages.Length > 0 ? packages : [CreateMsiPackage()]
        };
    }

    public static PackageInfo CreateMsiPackage(
        string id = "TestMsi",
        string? productCode = null,
        string? version = null)
    {
        var props = new Dictionary<string, string>();
        if (productCode is not null)
        {
            props["ProductCode"] = productCode;
        }

        return new PackageInfo
        {
            Id = id,
            Type = PackageType.MsiPackage,
            DisplayName = $"Test MSI Package ({id})",
            Version = version,
            SourcePath = $@"C:\test\{id}.msi",
            Sha256Hash = "AABBCCDD",
            Properties = props
        };
    }

    public static PackageInfo CreateExePackage(string id = "TestExe")
    {
        return new PackageInfo
        {
            Id = id,
            Type = PackageType.ExePackage,
            DisplayName = $"Test EXE Package ({id})",
            SourcePath = $@"C:\test\{id}.exe",
            Sha256Hash = "EEFF0011"
        };
    }
}
