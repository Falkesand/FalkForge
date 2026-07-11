using System.Runtime.Versioning;
using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Models;
using FalkForge.Platform.Windows;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe;

/// <summary>
/// Compiled-MSI level tests for the Icon-table pipeline. These are the intent
/// guard for the correctness fix: icons, working directories, and product icons
/// authored through the fluent API used to be silently dropped because there was
/// no Icon-table producer and the Shortcut/ProgId producers hard-coded null.
/// An in-memory model assertion would miss this — the model always carried the
/// values; the compiler dropped them. So every assertion here reads the produced
/// MSI back through msi.dll.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class IconTablePipelineTests : IDisposable
{
    private readonly string _tempDir;
    private readonly byte[] _iconBytes = [0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x10, 0x10, 0xDE, 0xAD, 0xBE, 0xEF];

    public IconTablePipelineTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"IconPipeline_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup; msi.dll can briefly retain a handle.
            }
        }
    }

    private (string SourceFile, string IconFile, string OutputDir) Arrange()
    {
        string sourceDir = Path.Combine(_tempDir, "src");
        Directory.CreateDirectory(sourceDir);
        string sourceFile = Path.Combine(sourceDir, "app.exe");
        File.WriteAllText(sourceFile, "fake exe content");
        string iconFile = Path.Combine(sourceDir, "app.ico");
        File.WriteAllBytes(iconFile, _iconBytes);
        string outputDir = Path.Combine(_tempDir, "out");
        Directory.CreateDirectory(outputDir);
        return (sourceFile, iconFile, outputDir);
    }

    [Fact]
    public void Shortcut_with_icon_and_working_directory_reaches_compiled_msi()
    {
        (string sourceFile, string iconFile, string outputDir) = Arrange();

        PackageModel package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "IconShortcut";
            p.Manufacturer = "FalkForge";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "FalkForge" / "IconShortcut"));
            p.Shortcut("App", "[INSTALLDIR]app.exe")
                .WithIcon(iconFile, 3)
                .WithWorkingDirectory("DesktopFolder")
                .OnDesktop();
        });

        Result<string> compile = MsiAuthoring.Compile(package, outputDir);
        Assert.True(compile.IsSuccess, compile.IsFailure ? compile.Error.Message : null);

        using MsiDatabase db = MsiDatabase.Open(compile.Value, readOnly: true).Value;

        // Icon table must carry exactly one row whose Data stream is the icon bytes.
        Result<List<string?[]>> iconRows = db.QueryRows("SELECT `Name` FROM `Icon`", fieldCount: 1);
        Assert.True(iconRows.IsSuccess, iconRows.IsFailure ? iconRows.Error.Message : null);
        Assert.Single(iconRows.Value);
        string iconName = iconRows.Value[0][0]!;
        Assert.False(string.IsNullOrEmpty(iconName));

        Result<byte[]> data = db.ReadStream("SELECT `Data` FROM `Icon`", fieldCount: 1, streamField: 1);
        Assert.True(data.IsSuccess, data.IsFailure ? data.Error.Message : null);
        Assert.Equal(_iconBytes, data.Value);

        // Shortcut must reference that icon, carry IconIndex, and resolve WkDir to
        // the named directory id — NOT the hard-coded INSTALLDIR default.
        Result<List<string?[]>> scRows =
            db.QueryRows("SELECT `Shortcut`, `Icon_`, `IconIndex`, `WkDir` FROM `Shortcut`", fieldCount: 4);
        Assert.True(scRows.IsSuccess, scRows.IsFailure ? scRows.Error.Message : null);
        string?[] sc = Assert.Single(scRows.Value);
        Assert.Equal(iconName, sc[1]);
        Assert.Equal("3", sc[2]);
        Assert.Equal("DesktopFolder", sc[3]);
        Assert.NotEqual("INSTALLDIR", sc[3]);
    }

    [Fact]
    public void Shortcut_without_working_directory_keeps_installdir_default()
    {
        (string sourceFile, _, string outputDir) = Arrange();

        PackageModel package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "NoWkDir";
            p.Manufacturer = "FalkForge";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "FalkForge" / "NoWkDir"));
            p.Shortcut("App", "[INSTALLDIR]app.exe").OnStartMenu();
        });

        Result<string> compile = MsiAuthoring.Compile(package, outputDir);
        Assert.True(compile.IsSuccess, compile.IsFailure ? compile.Error.Message : null);

        using MsiDatabase db = MsiDatabase.Open(compile.Value, readOnly: true).Value;
        Result<List<string?[]>> scRows = db.QueryRows("SELECT `WkDir` FROM `Shortcut`", fieldCount: 1);
        Assert.True(scRows.IsSuccess, scRows.IsFailure ? scRows.Error.Message : null);
        Assert.Equal("INSTALLDIR", Assert.Single(scRows.Value)[0]);
    }

    [Fact]
    public void File_association_with_icon_sets_progid_icon()
    {
        (string sourceFile, string iconFile, string outputDir) = Arrange();

        PackageModel package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "AssocIcon";
            p.Manufacturer = "FalkForge";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "FalkForge" / "AssocIcon"));
            p.FileAssociation(".falk", a =>
            {
                a.ProgId("FalkForge.Doc");
                a.Description = "Falk Document";
                a.IconFile = iconFile;
                a.IconIndex = 5;
            });
        });

        Result<string> compile = MsiAuthoring.Compile(package, outputDir);
        Assert.True(compile.IsSuccess, compile.IsFailure ? compile.Error.Message : null);

        using MsiDatabase db = MsiDatabase.Open(compile.Value, readOnly: true).Value;

        Result<List<string?[]>> iconRows = db.QueryRows("SELECT `Name` FROM `Icon`", fieldCount: 1);
        Assert.True(iconRows.IsSuccess, iconRows.IsFailure ? iconRows.Error.Message : null);
        string iconName = Assert.Single(iconRows.Value)[0]!;

        Result<List<string?[]>> progIdRows =
            db.QueryRows("SELECT `ProgId`, `Icon_`, `IconIndex` FROM `ProgId`", fieldCount: 3);
        Assert.True(progIdRows.IsSuccess, progIdRows.IsFailure ? progIdRows.Error.Message : null);
        string?[] progId = Assert.Single(progIdRows.Value);
        Assert.Equal("FalkForge.Doc", progId[0]);
        Assert.Equal(iconName, progId[1]);
        Assert.Equal("5", progId[2]);
    }

    [Fact]
    public void Product_icon_writes_arpproducticon_property_and_icon_row()
    {
        (string sourceFile, string iconFile, string outputDir) = Arrange();

        PackageModel package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "ArpIcon";
            p.Manufacturer = "FalkForge";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "FalkForge" / "ArpIcon"));
            p.ProductIcon(iconFile);
        });

        Result<string> compile = MsiAuthoring.Compile(package, outputDir);
        Assert.True(compile.IsSuccess, compile.IsFailure ? compile.Error.Message : null);

        using MsiDatabase db = MsiDatabase.Open(compile.Value, readOnly: true).Value;

        Result<List<string?[]>> iconRows = db.QueryRows("SELECT `Name` FROM `Icon`", fieldCount: 1);
        Assert.True(iconRows.IsSuccess, iconRows.IsFailure ? iconRows.Error.Message : null);
        string iconName = Assert.Single(iconRows.Value)[0]!;

        Result<List<string?[]>> props = db.QueryRows("SELECT `Property`, `Value` FROM `Property`", fieldCount: 2);
        Assert.True(props.IsSuccess, props.IsFailure ? props.Error.Message : null);
        Assert.Contains(props.Value, r => r[0] == "ARPPRODUCTICON" && r[1] == iconName);
    }

    [Fact]
    public void Two_shortcuts_sharing_one_icon_file_dedup_to_a_single_icon_row()
    {
        (string sourceFile, string iconFile, string outputDir) = Arrange();

        PackageModel package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "IconDedup";
            p.Manufacturer = "FalkForge";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "FalkForge" / "IconDedup"));
            p.Shortcut("First", "[INSTALLDIR]app.exe").WithIcon(iconFile).OnDesktop();
            p.Shortcut("Second", "[INSTALLDIR]app.exe").WithIcon(iconFile).OnStartMenu();
        });

        Result<string> compile = MsiAuthoring.Compile(package, outputDir);
        Assert.True(compile.IsSuccess, compile.IsFailure ? compile.Error.Message : null);

        using MsiDatabase db = MsiDatabase.Open(compile.Value, readOnly: true).Value;
        Result<List<string?[]>> iconRows = db.QueryRows("SELECT `Name` FROM `Icon`", fieldCount: 1);
        Assert.True(iconRows.IsSuccess, iconRows.IsFailure ? iconRows.Error.Message : null);
        Assert.Single(iconRows.Value);

        Result<List<string?[]>> scRows = db.QueryRows("SELECT `Icon_` FROM `Shortcut`", fieldCount: 1);
        Assert.True(scRows.IsSuccess, scRows.IsFailure ? scRows.Error.Message : null);
        Assert.Equal(2, scRows.Value.Count);
        Assert.All(scRows.Value, r => Assert.Equal(iconRows.Value[0][0], r[0]));
    }
}
