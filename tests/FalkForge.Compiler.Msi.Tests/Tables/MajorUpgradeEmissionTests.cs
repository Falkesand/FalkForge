using System.Globalization;
using System.Runtime.Versioning;
using FalkForge.Models;
using FalkForge.Platform.Windows;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Tables;

[SupportedOSPlatform("windows")]
public sealed class MajorUpgradeEmissionTests
{
    // MSI Upgrade table attribute bits (per Windows Installer SDK, Upgrade Table):
    //   0x100 (256) = msidbUpgradeAttributesVersionMinInclusive   → VersionMin comparison is >=
    //   0x200 (512) = msidbUpgradeAttributesVersionMaxInclusive   → VersionMax comparison is <=
    private const int VersionMinInclusive = 0x100;
    private const int VersionMaxInclusive = 0x200;

    [Fact]
    public void DefaultMajorUpgrade_EmitsVersionMinInclusiveOnlyOnOlderDetectRow()
    {
        var msiPath = CompileWithUpgrade("DefaultMUApp", allowSameVersion: false);
        try
        {
            var attributes = ReadOlderVersionFoundAttributes(msiPath);
            Assert.True((attributes & VersionMinInclusive) != 0,
                $"Expected VersionMinInclusive (0x{VersionMinInclusive:X3}) bit set so VersionMin='0.0.0' is treated as a >= 0.0.0 match; got Attributes=0x{attributes:X3}.");
            Assert.Equal(0, attributes & VersionMaxInclusive);
        }
        finally
        {
            Cleanup(msiPath);
        }
    }

    [Fact]
    public void AllowSameVersionUpgrade_EmitsBothInclusiveBitsOnOlderDetectRow()
    {
        var msiPath = CompileWithUpgrade("SameVerMUApp", allowSameVersion: true);
        try
        {
            var attributes = ReadOlderVersionFoundAttributes(msiPath);
            Assert.True((attributes & VersionMinInclusive) != 0,
                $"Expected VersionMinInclusive (0x{VersionMinInclusive:X3}) bit set; got Attributes=0x{attributes:X3}.");
            Assert.True((attributes & VersionMaxInclusive) != 0,
                $"Expected VersionMaxInclusive (0x{VersionMaxInclusive:X3}) bit set so the row matches the current version inclusively; got Attributes=0x{attributes:X3}.");
        }
        finally
        {
            Cleanup(msiPath);
        }
    }

    private static string CompileWithUpgrade(string appName, bool allowSameVersion)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"MUTest_{appName}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var sourceDir = Path.Combine(tempDir, "source");
        Directory.CreateDirectory(sourceDir);
        var sourceFile = Path.Combine(sourceDir, "app.exe");
        File.WriteAllText(sourceFile, $"fake content for {appName}");

        var outputDir = Path.Combine(tempDir, "output");
        Directory.CreateDirectory(outputDir);

        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = appName;
            p.Manufacturer = "TestCorp";
            p.Version = new Version(1, 2, 3);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / appName));
            p.MajorUpgrade(mu =>
            {
                if (allowSameVersion)
                    mu.AllowSameVersionUpgrades();
            });
        });

        var fileSystem = new WindowsFileSystem();
        var compiler = new MsiCompiler(fileSystem);
        var result = compiler.Compile(package, outputDir);
        Assert.True(result.IsSuccess, $"Compile failed: {(result.IsFailure ? result.Error.Message : "")}");
        return result.Value;
    }

    private static int ReadOlderVersionFoundAttributes(string msiPath)
    {
        var dbResult = MsiDatabase.Open(msiPath, readOnly: true);
        Assert.True(dbResult.IsSuccess, $"Failed to open MSI: {(dbResult.IsFailure ? dbResult.Error.Message : "")}");
        using var db = dbResult.Value;

        // EmitMajorUpgrade emits two Upgrade rows: one with ActionProperty='OLDERVERSIONFOUND'
        // (the detect-and-remove row whose Attributes carry the version inclusive flags) and
        // one with ActionProperty='NEWERVERSIONFOUND' (a static OnlyDetect=2 marker for the
        // downgrade-block launch condition). Filter by ActionProperty so the row order
        // returned by MSI doesn't matter.
        var rows = db.QueryRows("SELECT `ActionProperty`, `Attributes` FROM `Upgrade`", 2);
        Assert.True(rows.IsSuccess, $"Upgrade query failed: {(rows.IsFailure ? rows.Error.Message : "")}");
        var olderRow = rows.Value.FirstOrDefault(r =>
            string.Equals(r[0], "OLDERVERSIONFOUND", StringComparison.Ordinal));
        Assert.NotNull(olderRow);
        return int.Parse(olderRow![1]!, CultureInfo.InvariantCulture);
    }

    private static void Cleanup(string msiPath)
    {
        var dir = Path.GetDirectoryName(msiPath);
        if (dir is null)
            return;
        var parent = Path.GetDirectoryName(dir);
        if (parent is not null && Directory.Exists(parent))
            Directory.Delete(parent, recursive: true);
    }
}
