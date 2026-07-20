using System.Runtime.Versioning;
using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Models;
using FalkForge.Platform.Windows;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe;

/// <summary>
/// End-to-end proof that <see cref="MsiAuthoring.Compile"/> renders the license
/// agreement text configured through <c>PackageBuilder.LicenseFile</c>.
///
/// <para>
/// The MSI <c>ScrollableText</c> license control draws the content of its
/// <c>Control.Text</c> column (Windows Installer ignores <c>Control._Property</c>
/// for that control type), so the fix must inject the RTF literally into
/// <c>Text</c>. These tests compile a real MSI and read the <c>Control</c> table
/// back via msi.dll to confirm the license bytes land where the UI reads them —
/// the regression previously left <c>Text</c> null, so the license box rendered
/// blank for MSI output while the EXE bundle path rendered it correctly.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MsiAuthoringLicenseTests : IDisposable
{
    // Minimal, valid RTF document carrying a distinctive marker to assert on.
    private const string LicenseRtf =
        @"{\rtf1\ansi\deff0{\fonttbl{\f0 Times New Roman;}}\f0\fs24 " +
        @"FalkForge Demo License Agreement. You may use this software freely.\par}";

    private readonly string _tempDir;

    public MsiAuthoringLicenseTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"MsiAuthoringLicense_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); }
            catch (IOException) { }
        }
    }

    private string CreateSourceFile()
    {
        string sourceDir = Path.Combine(_tempDir, "src");
        Directory.CreateDirectory(sourceDir);
        string sourceFile = Path.Combine(sourceDir, "app.exe");
        File.WriteAllText(sourceFile, "fake exe content");
        return sourceFile;
    }

    [Fact]
    public void Compile_with_license_file_and_wizard_dialog_set_embeds_rtf_in_license_control_text()
    {
        string sourceFile = CreateSourceFile();
        string licensePath = Path.Combine(_tempDir, "license.rtf");
        File.WriteAllText(licensePath, LicenseRtf);

        string outputDir = Path.Combine(_tempDir, "out");
        Directory.CreateDirectory(outputDir);

        PackageModel package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "LicensedApp";
            p.Manufacturer = "FalkForge";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "FalkForge" / "LicensedApp"));
            p.UseDialogSet(MsiDialogSet.InstallDir);
            p.LicenseFile = licensePath;
        });

        Result<string> result = MsiAuthoring.Compile(package, outputDir);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);

        using MsiDatabase db = MsiDatabase.Open(result.Value, readOnly: true).Value;
        Result<List<string?[]>> query = db.QueryRows(
            "SELECT `Text` FROM `Control` " +
            "WHERE `Dialog_` = 'LicenseAgreementDlg' AND `Control` = 'LicenseText'",
            fieldCount: 1);
        Assert.True(query.IsSuccess);

        string?[] row = Assert.Single(query.Value);
        Assert.NotNull(row[0]);
        // The human-readable license body is present...
        Assert.Contains("FalkForge Demo License Agreement", row[0]);
        // ...and the RTF container is preserved intact (bytes not corrupted).
        Assert.Contains(@"\rtf1", row[0]);
    }

    [Fact]
    public void Compile_with_missing_license_file_and_wizard_dialog_set_fails_loud()
    {
        string sourceFile = CreateSourceFile();
        string missingLicense = Path.Combine(_tempDir, "does-not-exist.rtf");

        string outputDir = Path.Combine(_tempDir, "out");
        Directory.CreateDirectory(outputDir);

        PackageModel package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "LicensedApp";
            p.Manufacturer = "FalkForge";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "FalkForge" / "LicensedApp"));
            p.UseDialogSet(MsiDialogSet.InstallDir);
            p.LicenseFile = missingLicense;
        });

        Result<string> result = MsiAuthoring.Compile(package, outputDir);

        Assert.True(result.IsFailure, "a configured-but-unreadable license file must fail loud, not silently compile");
        Assert.Equal(ErrorKind.FileNotFound, result.Error.Kind);
        Assert.Contains("License file", result.Error.Message);
    }

    [Fact]
    public void Compile_with_license_file_but_minimal_dialog_set_without_license_page_does_not_fail()
    {
        // The Minimal dialog set has no license page. A configured LicenseFile has no
        // control to render into, so the compile must still succeed (the file is simply
        // not surfaced) rather than crash or fail loud on the unused path.
        string sourceFile = CreateSourceFile();
        string licensePath = Path.Combine(_tempDir, "license.rtf");
        File.WriteAllText(licensePath, LicenseRtf);

        string outputDir = Path.Combine(_tempDir, "out");
        Directory.CreateDirectory(outputDir);

        PackageModel package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "LicensedApp";
            p.Manufacturer = "FalkForge";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "FalkForge" / "LicensedApp"));
            p.UseDialogSet(MsiDialogSet.Minimal);
            p.LicenseFile = licensePath;
        });

        Result<string> result = MsiAuthoring.Compile(package, outputDir);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }
}
