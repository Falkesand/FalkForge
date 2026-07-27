using System.Runtime.Versioning;
using FalkForge.Builders;
using FalkForge.Models;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests;

/// <summary>
/// End-to-end tests for <see cref="TransformCompiler"/> (MST output, assessment priority #1):
/// compiles two real MSIs (Windows-only, requires msi.dll) that genuinely differ, generates a
/// transform between them via <see cref="TransformCompiler.Compile"/>, then proves the transform
/// is real by applying it (via <see cref="MsiDatabase.ApplyTransform"/>) to a fresh copy of the
/// base MSI and reading back the changed property -- not merely asserting the .mst file is
/// non-empty. Before this file, TransformCompiler.cs sat at 0% line coverage with no test file.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TransformCompilerTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"MstCompilerTest_{Guid.NewGuid():N}");

    public TransformCompilerTests()
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
        var dir = Path.Combine(_tempDir, $"{label}_mst_output");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Compile_BaseAndTargetDifferInProductVersion_ProducesMstThatAppliesAndChangesVersion()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        var baseMsi = CompileVersioned("TransformApp", new Version(1, 0, 0));
        var targetMsi = CompileVersioned("TransformApp", new Version(2, 0, 0));

        var modelResult = new TransformBuilder().BaseMsi(baseMsi).TargetMsi(targetMsi).Build();
        Assert.True(modelResult.IsSuccess, modelResult.IsFailure ? modelResult.Error.Message : null);

        var compileResult = new TransformCompiler().Compile(modelResult.Value, OutputDir());
        Assert.True(compileResult.IsSuccess, compileResult.IsFailure ? compileResult.Error.Message : null);
        var mstPath = compileResult.Value;
        Assert.True(File.Exists(mstPath), $"MST not found at: {mstPath}");
        Assert.True(new FileInfo(mstPath).Length > 0, "MST file is empty");

        // Prove the transform carries the REAL diff: apply it to a fresh copy of the base
        // MSI and confirm ProductVersion actually flips to the target's value. A bug that
        // generated an empty/placeholder transform would still leave a non-empty .mst file
        // (OLE storage overhead alone is non-zero), so file-existence alone cannot catch it.
        var appliedCopy = Path.Combine(_tempDir, "applied-version-diff.msi");
        File.Copy(baseMsi, appliedCopy);
        using var db = MsiDatabase.Open(appliedCopy, readOnly: false).Value;
        var applyResult = db.ApplyTransform(mstPath);
        Assert.True(applyResult.IsSuccess, applyResult.IsFailure ? applyResult.Error.Message : null);

        var rows = db.QueryRows("SELECT `Value` FROM `Property` WHERE `Property` = 'ProductVersion'", 1);
        Assert.True(rows.IsSuccess, rows.IsFailure ? rows.Error.Message : null);
        Assert.Equal("2.0.0", Assert.Single(rows.Value)[0]);
    }

    [Fact]
    public void Compile_MissingBaseMsi_ReturnsFileNotFoundWithoutTouchingNativeApi()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        var realMsi = CompileVersioned("ExistsApp", new Version(1, 0, 0));
        var missingBase = Path.Combine(_tempDir, "does-not-exist-base.msi");

        var modelResult = new TransformBuilder().BaseMsi(missingBase).TargetMsi(realMsi).Build();
        Assert.True(modelResult.IsSuccess, modelResult.IsFailure ? modelResult.Error.Message : null);

        var compileResult = new TransformCompiler().Compile(modelResult.Value, OutputDir());

        Assert.True(compileResult.IsFailure);
        Assert.Equal(ErrorKind.FileNotFound, compileResult.Error.Kind);
        Assert.Contains(missingBase, compileResult.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Compile_MissingTargetMsi_ReturnsFileNotFound()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        var realMsi = CompileVersioned("ExistsApp2", new Version(1, 0, 0));
        var missingTarget = Path.Combine(_tempDir, "does-not-exist-target.msi");

        var modelResult = new TransformBuilder().BaseMsi(realMsi).TargetMsi(missingTarget).Build();
        Assert.True(modelResult.IsSuccess, modelResult.IsFailure ? modelResult.Error.Message : null);

        var compileResult = new TransformCompiler().Compile(modelResult.Value, OutputDir());

        Assert.True(compileResult.IsFailure);
        Assert.Equal(ErrorKind.FileNotFound, compileResult.Error.Kind);
        Assert.Contains(missingTarget, compileResult.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Compile_InvalidModel_ReturnsValidationFailure()
    {
        // TransformBuilder.Build() already validates (covered by TransformBuilderTests /
        // TransformValidatorTests). This proves TransformCompiler.Compile ALSO re-checks
        // an already-constructed invalid model rather than trusting every caller to have
        // gone through the builder -- the model type only requires non-null, not non-empty.
        var invalidModel = new TransformModel { BaseMsiPath = "", TargetMsiPath = "" };

        var compileResult = new TransformCompiler().Compile(invalidModel, OutputDir());

        Assert.True(compileResult.IsFailure);
        Assert.Equal(ErrorKind.Validation, compileResult.Error.Kind);
    }

    [Fact]
    public void Compile_WithExplicitId_UsesSanitizedIdAsFileName()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        var baseMsi = CompileVersioned("IdApp", new Version(1, 0, 0));
        var targetMsi = CompileVersioned("IdApp", new Version(1, 1, 0));

        // Contains characters ('/' and ':') invalid in a file name -- must come back sanitized.
        var modelResult = new TransformBuilder()
            .Id("My/Transform:Id")
            .BaseMsi(baseMsi)
            .TargetMsi(targetMsi)
            .Build();
        Assert.True(modelResult.IsSuccess, modelResult.IsFailure ? modelResult.Error.Message : null);

        var compileResult = new TransformCompiler().Compile(modelResult.Value, OutputDir());
        Assert.True(compileResult.IsSuccess, compileResult.IsFailure ? compileResult.Error.Message : null);

        Assert.Equal($"{FileNameSanitizer.Sanitize("My/Transform:Id")}.mst", Path.GetFileName(compileResult.Value));
    }

    [Fact]
    public void Compile_WithoutId_DerivesFileNameFromBaseMsiFileName()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        var baseMsi = CompileVersioned("NoIdApp", new Version(1, 0, 0));
        var targetMsi = CompileVersioned("NoIdApp", new Version(1, 1, 0));

        var modelResult = new TransformBuilder().BaseMsi(baseMsi).TargetMsi(targetMsi).Build();
        Assert.True(modelResult.IsSuccess, modelResult.IsFailure ? modelResult.Error.Message : null);

        var compileResult = new TransformCompiler().Compile(modelResult.Value, OutputDir());
        Assert.True(compileResult.IsSuccess, compileResult.IsFailure ? compileResult.Error.Message : null);

        var expectedName = $"Transform_{FileNameSanitizer.Sanitize(Path.GetFileNameWithoutExtension(baseMsi))}.mst";
        Assert.Equal(expectedName, Path.GetFileName(compileResult.Value));
    }

    [Fact]
    public void Compile_ExistingMstFileAtOutputPath_IsOverwrittenWithFreshTransform()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        var baseMsi = CompileVersioned("OverwriteApp", new Version(1, 0, 0));
        var targetMsi = CompileVersioned("OverwriteApp", new Version(3, 0, 0));

        var modelResult = new TransformBuilder().Id("StaleId").BaseMsi(baseMsi).TargetMsi(targetMsi).Build();
        Assert.True(modelResult.IsSuccess, modelResult.IsFailure ? modelResult.Error.Message : null);

        var outputDir = OutputDir();
        var stalePath = Path.Combine(outputDir, "StaleId.mst");
        File.WriteAllText(stalePath, "not a real transform");

        var compileResult = new TransformCompiler().Compile(modelResult.Value, outputDir);
        Assert.True(compileResult.IsSuccess, compileResult.IsFailure ? compileResult.Error.Message : null);
        Assert.Equal(stalePath, compileResult.Value);

        // The stale placeholder text must be gone -- a real transform was written in its place.
        var appliedCopy = Path.Combine(_tempDir, "applied-overwrite.msi");
        File.Copy(baseMsi, appliedCopy);
        using var db = MsiDatabase.Open(appliedCopy, readOnly: false).Value;
        var applyResult = db.ApplyTransform(compileResult.Value);
        Assert.True(applyResult.IsSuccess, applyResult.IsFailure ? applyResult.Error.Message : null);
    }

    [Fact]
    public void Compile_WithPropertyChangesSet_KnownBug_ChangesAreSilentlyIgnored()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        // KNOWN BUG found during this coverage sweep (not fixed here -- out of scope for a
        // test-coverage task; flagged for the maintainer): TransformBuilder.SetProperty(...)
        // fully populates TransformModel.PropertyChanges (proven by TransformBuilderTests),
        // but TransformCompiler.Compile(...) never reads that dictionary at all -- it only
        // calls MsiDatabaseGenerateTransform(target, base, ...), which diffs whatever already
        // differs between the two ALREADY-COMPILED database files. A caller doing
        // `new TransformBuilder().SetProperty("REINSTALLMODE", "amus")` expecting a small,
        // targeted property-only transform gets nothing for that property unless it also
        // happens to already differ between BaseMsiPath and TargetMsiPath's own Property
        // tables. This test pins the CURRENT (buggy) behavior so a future intentional fix
        // shows up as a deliberate, reviewed test change instead of a silent regression.
        var baseMsi = CompileVersioned("PropBugApp", new Version(1, 0, 0));
        var targetMsi = CompileVersioned("PropBugApp", new Version(2, 0, 0));

        var modelResult = new TransformBuilder()
            .BaseMsi(baseMsi)
            .TargetMsi(targetMsi)
            .SetProperty("MYCUSTOMPROP", "hello")
            .Build();
        Assert.True(modelResult.IsSuccess, modelResult.IsFailure ? modelResult.Error.Message : null);
        Assert.Equal("hello", modelResult.Value.PropertyChanges["MYCUSTOMPROP"]); // the model DOES carry it

        var compileResult = new TransformCompiler().Compile(modelResult.Value, OutputDir());
        Assert.True(compileResult.IsSuccess, compileResult.IsFailure ? compileResult.Error.Message : null);

        var appliedCopy = Path.Combine(_tempDir, "applied-propbug.msi");
        File.Copy(baseMsi, appliedCopy);
        using var db = MsiDatabase.Open(appliedCopy, readOnly: false).Value;
        var applyResult = db.ApplyTransform(compileResult.Value);
        Assert.True(applyResult.IsSuccess, applyResult.IsFailure ? applyResult.Error.Message : null);

        // The real diff (ProductVersion) DID transfer -- the transform mechanism itself works.
        var versionRows = db.QueryRows("SELECT `Value` FROM `Property` WHERE `Property` = 'ProductVersion'", 1);
        Assert.True(versionRows.IsSuccess, versionRows.IsFailure ? versionRows.Error.Message : null);
        Assert.Equal("2.0.0", Assert.Single(versionRows.Value)[0]);

        // But the explicitly requested MYCUSTOMPROP change never made it into the transform.
        var customRows = db.QueryRows("SELECT `Value` FROM `Property` WHERE `Property` = 'MYCUSTOMPROP'", 1);
        Assert.True(customRows.IsSuccess, customRows.IsFailure ? customRows.Error.Message : null);
        Assert.Empty(customRows.Value);
    }
}
