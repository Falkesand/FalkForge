using System.Runtime.Versioning;
using FalkForge.Builders;
using FalkForge.Models;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests;

/// <summary>
/// End-to-end tests for <see cref="PatchCompiler"/> (MSP output, assessment priority #1):
/// compiles a real "target" (baseline) and "updated" MSI (Windows-only, requires msi.dll),
/// generates a patch package via <see cref="PatchCompiler.Compile"/>, then reads the result
/// back through the real MSI-storage read path (<see cref="MsiDatabase.Open"/>) rather than
/// asserting the output bytes are merely non-empty. As documented on <see cref="PatchCompiler"/>,
/// this produces a real embedded transform wrapped in an MsiPatchMetadata-bearing database --
/// NOT a Windows-Installer-signed .msp created via MsiCreatePatchFileEx/PatchWiz (that API is
/// not available to this project; see the class doc comment). These tests verify everything that
/// IS honestly verifiable in-process: the metadata table content, the embedded transform stream,
/// and that the embedded transform is itself a real, applicable diff between the two packages.
/// Before this file, PatchCompiler.cs sat at 0% line coverage with no test file.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class PatchCompilerTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"MspCompilerTest_{Guid.NewGuid():N}");

    public PatchCompilerTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { }
        }
    }

    private string CompileVersioned(string label, Version version)
    {
        var sourceDir = Path.Combine(_tempDir, $"{label}_{version}_source");
        Directory.CreateDirectory(sourceDir);
        var sourceFile = Path.Combine(sourceDir, "app.exe");
        File.WriteAllText(sourceFile, $"payload for {label} {version}");

        var outputDir = Path.Combine(_tempDir, $"{label}_{version}_output");
        Directory.CreateDirectory(outputDir);

        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = label;
            p.Manufacturer = "TestCorp";
            p.Version = version;
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / label));
        });

        var result = new MsiCompiler().Compile(package, outputDir);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        return result.Value;
    }

    private string OutputDir([System.Runtime.CompilerServices.CallerMemberName] string label = "")
    {
        var dir = Path.Combine(_tempDir, $"{label}_msp_output");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Compile_ValidTargetAndUpdatedMsi_ProducesMspWithExpectedMetadataAndApplicableEmbeddedTransform()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        var targetMsi = CompileVersioned("PatchApp", new Version(1, 0, 0));
        var updatedMsi = CompileVersioned("PatchApp", new Version(2, 0, 0));

        var patchId = Guid.NewGuid();
        var patchResult = new PatchBuilder()
            .Id(patchId)
            .Classification(PatchClassification.SecurityUpdate)
            .Description("Fixes a security issue")
            .Manufacturer("TestCorp")
            .TargetVersion("1.0.0")
            .UpdatedVersion("2.0.0")
            .TargetMsi(targetMsi)
            .UpdatedMsi(updatedMsi)
            .AllowRemoval(true)
            .Build();
        Assert.True(patchResult.IsSuccess, patchResult.IsFailure ? patchResult.Error.Message : null);

        var compileResult = new PatchCompiler().Compile(patchResult.Value, OutputDir());
        Assert.True(compileResult.IsSuccess, compileResult.IsFailure ? compileResult.Error.Message : null);
        var mspPath = compileResult.Value;

        Assert.True(File.Exists(mspPath), $"MSP not found at: {mspPath}");
        Assert.Equal($"Patch_{patchId:N}.msp", Path.GetFileName(mspPath));

        // Read the metadata back through the REAL MSI-storage read path (this is only possible
        // because PatchCompiler builds the .msp as a genuine OLE-compound MSI database via
        // MsiDatabase.Create -- a corrupted/truncated output would fail to even open here).
        using var db = MsiDatabase.Open(mspPath, readOnly: true).Value;

        var metadataRows = db.QueryRows("SELECT `Property`, `Value` FROM `MsiPatchMetadata`", 2);
        Assert.True(metadataRows.IsSuccess, metadataRows.IsFailure ? metadataRows.Error.Message : null);
        var metadata = metadataRows.Value.ToDictionary(r => r[0]!, r => r[1], StringComparer.Ordinal);

        Assert.Equal("Security Update", metadata["Classification"]);
        Assert.Equal("1", metadata["AllowRemoval"]);
        Assert.Equal("TestCorp", metadata["ManufacturerName"]);
        Assert.Equal("Fixes a security issue", metadata["Description"]);
        Assert.Equal("1.0.0", metadata["TargetProductVersion"]);
        Assert.Equal("2.0.0", metadata["UpdatedProductVersion"]);

        // Extract the embedded transform stream and prove it is a REAL, applicable transform
        // that carries the actual target->updated diff, not an opaque or empty blob.
        var streamResult = db.ReadStream("SELECT `Data` FROM `_Streams` WHERE `Name` = 'PatchTransform'", 1, 1);
        Assert.True(streamResult.IsSuccess, streamResult.IsFailure ? streamResult.Error.Message : null);
        Assert.True(streamResult.Value.Length > 0, "Embedded PatchTransform stream is empty");

        var extractedMstPath = Path.Combine(_tempDir, "extracted.mst");
        File.WriteAllBytes(extractedMstPath, streamResult.Value);

        var appliedCopy = Path.Combine(_tempDir, "applied.msi");
        File.Copy(targetMsi, appliedCopy);
        using var appliedDb = MsiDatabase.Open(appliedCopy, readOnly: false).Value;
        var applyResult = appliedDb.ApplyTransform(extractedMstPath);
        Assert.True(applyResult.IsSuccess, applyResult.IsFailure ? applyResult.Error.Message : null);

        var versionRows = appliedDb.QueryRows("SELECT `Value` FROM `Property` WHERE `Property` = 'ProductVersion'", 1);
        Assert.True(versionRows.IsSuccess, versionRows.IsFailure ? versionRows.Error.Message : null);
        Assert.Equal("2.0.0", Assert.Single(versionRows.Value)[0]);
    }

    [Theory]
    [InlineData(PatchClassification.Hotfix, "Hotfix")]
    [InlineData(PatchClassification.SecurityUpdate, "Security Update")]
    [InlineData(PatchClassification.Update, "Update")]
    public void Compile_ClassificationVariants_EmitExpectedMetadataString(
        PatchClassification classification, string expected)
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        var targetMsi = CompileVersioned("ClassApp", new Version(1, 0, 0));
        var updatedMsi = CompileVersioned("ClassApp", new Version(1, 0, 1));

        var patchResult = new PatchBuilder()
            .Id(Guid.NewGuid())
            .Classification(classification)
            .TargetMsi(targetMsi)
            .UpdatedMsi(updatedMsi)
            .Build();
        Assert.True(patchResult.IsSuccess, patchResult.IsFailure ? patchResult.Error.Message : null);

        var compileResult = new PatchCompiler().Compile(patchResult.Value, OutputDir());
        Assert.True(compileResult.IsSuccess, compileResult.IsFailure ? compileResult.Error.Message : null);

        using var db = MsiDatabase.Open(compileResult.Value, readOnly: true).Value;
        var row = db.QueryRows("SELECT `Value` FROM `MsiPatchMetadata` WHERE `Property` = 'Classification'", 1).Value;
        Assert.Equal(expected, Assert.Single(row)[0]);
    }

    [Theory]
    [InlineData(true, "1")]
    [InlineData(false, "0")]
    public void Compile_AllowRemovalVariants_EncodeAsOneOrZero(bool allowRemoval, string expected)
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        var targetMsi = CompileVersioned("AllowRemovalApp", new Version(1, 0, 0));
        var updatedMsi = CompileVersioned("AllowRemovalApp", new Version(1, 0, 1));

        var patchResult = new PatchBuilder()
            .Id(Guid.NewGuid())
            .Classification(PatchClassification.Update)
            .TargetMsi(targetMsi)
            .UpdatedMsi(updatedMsi)
            .AllowRemoval(allowRemoval)
            .Build();
        Assert.True(patchResult.IsSuccess, patchResult.IsFailure ? patchResult.Error.Message : null);

        var compileResult = new PatchCompiler().Compile(patchResult.Value, OutputDir());
        Assert.True(compileResult.IsSuccess, compileResult.IsFailure ? compileResult.Error.Message : null);

        using var db = MsiDatabase.Open(compileResult.Value, readOnly: true).Value;
        var row = db.QueryRows("SELECT `Value` FROM `MsiPatchMetadata` WHERE `Property` = 'AllowRemoval'", 1).Value;
        Assert.Equal(expected, Assert.Single(row)[0]);
    }

    [Fact]
    public void Compile_OptionalFieldsOmitted_EmitsEmptyDefaultsAndSkipsVersionRows()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        // Description/Manufacturer are null -> the compiler must fall back to empty string
        // rather than throwing (a null CHAR column value would fail MsiViewModify). Note the
        // round-trip assertion below expects null, not "": the Windows Installer record API
        // has no way to distinguish a stored empty string from a NULL field -- MsiRecordGetString
        // reports zero length for both, and MsiDatabase.GetRecordString maps that zero-length
        // case to null (see MsiDatabase.cs). That collapse happens at the native API layer, not
        // in FalkForge's code, so an empty-string round-trip reading back as null is the correct,
        // honest expectation here, not a compiler bug. TargetVersion/UpdatedVersion are null ->
        // those two metadata KEYS must be entirely absent (PatchCompiler only adds them "if not
        // null"), not present with an empty/placeholder value.
        var targetMsi = CompileVersioned("MinimalApp", new Version(1, 0, 0));
        var updatedMsi = CompileVersioned("MinimalApp", new Version(1, 0, 1));

        var patchResult = new PatchBuilder()
            .Id(Guid.NewGuid())
            .Classification(PatchClassification.Update)
            .TargetMsi(targetMsi)
            .UpdatedMsi(updatedMsi)
            .Build();
        Assert.True(patchResult.IsSuccess, patchResult.IsFailure ? patchResult.Error.Message : null);

        var compileResult = new PatchCompiler().Compile(patchResult.Value, OutputDir());
        Assert.True(compileResult.IsSuccess, compileResult.IsFailure ? compileResult.Error.Message : null);

        using var db = MsiDatabase.Open(compileResult.Value, readOnly: true).Value;
        var metadata = db.QueryRows("SELECT `Property`, `Value` FROM `MsiPatchMetadata`", 2).Value
            .ToDictionary(r => r[0]!, r => r[1], StringComparer.Ordinal);

        Assert.Null(metadata["ManufacturerName"]);
        Assert.Null(metadata["Description"]);
        Assert.False(metadata.ContainsKey("TargetProductVersion"), "TargetProductVersion must be absent when TargetVersion is null.");
        Assert.False(metadata.ContainsKey("UpdatedProductVersion"), "UpdatedProductVersion must be absent when UpdatedVersion is null.");
    }

    [Fact]
    public void Compile_MissingTargetMsi_ReturnsFileNotFound()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        var updatedMsi = CompileVersioned("ExistsUpdated", new Version(1, 0, 0));
        var missingTarget = Path.Combine(_tempDir, "does-not-exist-target.msi");

        var patchResult = new PatchBuilder()
            .Id(Guid.NewGuid())
            .Classification(PatchClassification.Update)
            .TargetMsi(missingTarget)
            .UpdatedMsi(updatedMsi)
            .Build();
        Assert.True(patchResult.IsSuccess, patchResult.IsFailure ? patchResult.Error.Message : null);

        var compileResult = new PatchCompiler().Compile(patchResult.Value, OutputDir());

        Assert.True(compileResult.IsFailure);
        Assert.Equal(ErrorKind.FileNotFound, compileResult.Error.Kind);
        Assert.Contains(missingTarget, compileResult.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Compile_MissingUpdatedMsi_ReturnsFileNotFound()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        var targetMsi = CompileVersioned("ExistsTarget", new Version(1, 0, 0));
        var missingUpdated = Path.Combine(_tempDir, "does-not-exist-updated.msi");

        var patchResult = new PatchBuilder()
            .Id(Guid.NewGuid())
            .Classification(PatchClassification.Update)
            .TargetMsi(targetMsi)
            .UpdatedMsi(missingUpdated)
            .Build();
        Assert.True(patchResult.IsSuccess, patchResult.IsFailure ? patchResult.Error.Message : null);

        var compileResult = new PatchCompiler().Compile(patchResult.Value, OutputDir());

        Assert.True(compileResult.IsFailure);
        Assert.Equal(ErrorKind.FileNotFound, compileResult.Error.Kind);
        Assert.Contains(missingUpdated, compileResult.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Compile_InvalidModel_ReturnsValidationFailure()
    {
        // PatchBuilder.Build() already validates (covered by PatchBuilderTests /
        // PatchValidatorTests). This proves PatchCompiler.Compile ALSO re-checks an
        // already-constructed invalid model rather than trusting every caller went
        // through the builder -- the model type only requires non-null, not non-empty/non-default.
        var invalidModel = new PatchModel
        {
            Id = Guid.Empty,
            Classification = PatchClassification.Update,
            TargetMsiPath = "",
            UpdatedMsiPath = ""
        };

        var compileResult = new PatchCompiler().Compile(invalidModel, OutputDir());

        Assert.True(compileResult.IsFailure);
        Assert.Equal(ErrorKind.Validation, compileResult.Error.Kind);
    }

    [Fact]
    public void Compile_ExistingMspFileAtOutputPath_IsOverwrittenWithFreshPatch()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        var targetMsi = CompileVersioned("OverwritePatchApp", new Version(1, 0, 0));
        var updatedMsi = CompileVersioned("OverwritePatchApp", new Version(1, 5, 0));
        var patchId = Guid.NewGuid();

        var patchResult = new PatchBuilder()
            .Id(patchId)
            .Classification(PatchClassification.Update)
            .TargetMsi(targetMsi)
            .UpdatedMsi(updatedMsi)
            .Build();
        Assert.True(patchResult.IsSuccess, patchResult.IsFailure ? patchResult.Error.Message : null);

        var outputDir = OutputDir();
        var stalePath = Path.Combine(outputDir, $"Patch_{patchId:N}.msp");
        File.WriteAllText(stalePath, "not a real patch");

        var compileResult = new PatchCompiler().Compile(patchResult.Value, outputDir);
        Assert.True(compileResult.IsSuccess, compileResult.IsFailure ? compileResult.Error.Message : null);
        Assert.Equal(stalePath, compileResult.Value);

        // The stale placeholder text is gone -- a real, openable MSI-storage database
        // (with the MsiPatchMetadata table) was written in its place.
        var dbResult = MsiDatabase.Open(compileResult.Value, readOnly: true);
        Assert.True(dbResult.IsSuccess, dbResult.IsFailure ? dbResult.Error.Message : null);
        using var db = dbResult.Value;
        var rows = db.QueryRows("SELECT `Property` FROM `MsiPatchMetadata` WHERE `Property` = 'Classification'", 1);
        Assert.True(rows.IsSuccess, rows.IsFailure ? rows.Error.Message : null);
        Assert.Single(rows.Value);
    }
}
