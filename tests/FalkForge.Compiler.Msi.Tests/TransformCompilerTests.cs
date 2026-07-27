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
    public void Compile_WithPropertyChangesSet_NewPropertyLandsInAppliedTransform()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        // Regression test for a bug found during a coverage sweep: TransformBuilder.SetProperty(...)
        // fully populates TransformModel.PropertyChanges, but TransformCompiler.Compile(...) used
        // to never read that dictionary at all -- it only diffed whatever already differed between
        // the two already-compiled database files. A caller doing
        // `new TransformBuilder().SetProperty("MYCUSTOMPROP", "hello")` must now get a transform
        // that actually carries MYCUSTOMPROP, not merely whatever incidentally differed already.
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

        // The real diff (ProductVersion) still transfers -- the transform mechanism itself works.
        var versionRows = db.QueryRows("SELECT `Value` FROM `Property` WHERE `Property` = 'ProductVersion'", 1);
        Assert.True(versionRows.IsSuccess, versionRows.IsFailure ? versionRows.Error.Message : null);
        Assert.Equal("2.0.0", Assert.Single(versionRows.Value)[0]);

        // And the explicitly requested MYCUSTOMPROP change now makes it into the transform too.
        var customRows = db.QueryRows("SELECT `Value` FROM `Property` WHERE `Property` = 'MYCUSTOMPROP'", 1);
        Assert.True(customRows.IsSuccess, customRows.IsFailure ? customRows.Error.Message : null);
        Assert.Equal("hello", Assert.Single(customRows.Value)[0]);
    }

    [Fact]
    public void Compile_WithPropertyChangeOverridingExistingProperty_UpdatesRatherThanDuplicates()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        // ALLUSERS already exists as a row in a PerMachine target's Property table (PropertyTableProducer
        // always seeds it as "1" for InstallScope.PerMachine, the package default). Overriding it via
        // SetProperty must UPDATE that row, not attempt a duplicate INSERT (which the Property table's
        // primary key on `Property` would reject). ALLUSERS is also a legal ALL-UPPERCASE public MSI
        // property identifier, unlike the standard mixed-case reserved properties (e.g. ProductVersion).
        var baseMsi = CompileVersioned("PropUpdateApp", new Version(1, 0, 0));
        var targetMsi = CompileVersioned("PropUpdateApp", new Version(2, 0, 0));

        var modelResult = new TransformBuilder()
            .BaseMsi(baseMsi)
            .TargetMsi(targetMsi)
            .SetProperty("ALLUSERS", "2")
            .Build();
        Assert.True(modelResult.IsSuccess, modelResult.IsFailure ? modelResult.Error.Message : null);

        var compileResult = new TransformCompiler().Compile(modelResult.Value, OutputDir());
        Assert.True(compileResult.IsSuccess, compileResult.IsFailure ? compileResult.Error.Message : null);

        var appliedCopy = Path.Combine(_tempDir, "applied-propupdate.msi");
        File.Copy(baseMsi, appliedCopy);
        using var db = MsiDatabase.Open(appliedCopy, readOnly: false).Value;
        var applyResult = db.ApplyTransform(compileResult.Value);
        Assert.True(applyResult.IsSuccess, applyResult.IsFailure ? applyResult.Error.Message : null);

        var allUsersRows = db.QueryRows("SELECT `Value` FROM `Property` WHERE `Property` = 'ALLUSERS'", 1);
        Assert.True(allUsersRows.IsSuccess, allUsersRows.IsFailure ? allUsersRows.Error.Message : null);
        Assert.Equal("2", Assert.Single(allUsersRows.Value)[0]);
    }

    [Fact]
    public void Compile_WithMultiplePropertyChanges_AllLandInAppliedTransform()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        var baseMsi = CompileVersioned("PropMultiApp", new Version(1, 0, 0));
        var targetMsi = CompileVersioned("PropMultiApp", new Version(2, 0, 0));

        var modelResult = new TransformBuilder()
            .BaseMsi(baseMsi)
            .TargetMsi(targetMsi)
            .SetProperty("FIRSTPROP", "one")
            .SetProperty("SECONDPROP", "two")
            .SetProperty("ALLUSERS", "2")
            .Build();
        Assert.True(modelResult.IsSuccess, modelResult.IsFailure ? modelResult.Error.Message : null);

        var compileResult = new TransformCompiler().Compile(modelResult.Value, OutputDir());
        Assert.True(compileResult.IsSuccess, compileResult.IsFailure ? compileResult.Error.Message : null);

        var appliedCopy = Path.Combine(_tempDir, "applied-propmulti.msi");
        File.Copy(baseMsi, appliedCopy);
        using var db = MsiDatabase.Open(appliedCopy, readOnly: false).Value;
        var applyResult = db.ApplyTransform(compileResult.Value);
        Assert.True(applyResult.IsSuccess, applyResult.IsFailure ? applyResult.Error.Message : null);

        var first = db.QueryRows("SELECT `Value` FROM `Property` WHERE `Property` = 'FIRSTPROP'", 1);
        Assert.True(first.IsSuccess, first.IsFailure ? first.Error.Message : null);
        Assert.Equal("one", Assert.Single(first.Value)[0]);

        var second = db.QueryRows("SELECT `Value` FROM `Property` WHERE `Property` = 'SECONDPROP'", 1);
        Assert.True(second.IsSuccess, second.IsFailure ? second.Error.Message : null);
        Assert.Equal("two", Assert.Single(second.Value)[0]);

        var allUsers = db.QueryRows("SELECT `Value` FROM `Property` WHERE `Property` = 'ALLUSERS'", 1);
        Assert.True(allUsers.IsSuccess, allUsers.IsFailure ? allUsers.Error.Message : null);
        Assert.Equal("2", Assert.Single(allUsers.Value)[0]);
    }

    [Fact]
    public void Compile_WithIllegalPropertyName_ReturnsTypedValidationFailureNotNativeError()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        var baseMsi = CompileVersioned("PropIllegalApp", new Version(1, 0, 0));
        var targetMsi = CompileVersioned("PropIllegalApp", new Version(2, 0, 0));

        // Lowercase + space is not a legal PUBLIC MSI property identifier.
        var modelResult = new TransformBuilder()
            .BaseMsi(baseMsi)
            .TargetMsi(targetMsi)
            .SetProperty("not a valid prop", "x")
            .Build();

        // TransformBuilder.Build() already re-validates, so the illegal name is rejected here --
        // covered by TransformValidatorTests for the rule itself. Compile must also reject it if
        // ever handed an already-constructed model that bypassed the builder.
        var model = modelResult.IsSuccess
            ? modelResult.Value
            : new TransformModel
            {
                BaseMsiPath = baseMsi,
                TargetMsiPath = targetMsi,
                PropertyChanges = new Dictionary<string, string> { ["not a valid prop"] = "x" }
            };

        var compileResult = new TransformCompiler().Compile(model, OutputDir());

        Assert.True(compileResult.IsFailure);
        Assert.Equal(ErrorKind.Validation, compileResult.Error.Kind);
    }

    [Fact]
    public void Compile_WithPropertyChanges_NeverMutatesCallersTargetMsiFile()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        var baseMsi = CompileVersioned("PropNoMutateApp", new Version(1, 0, 0));
        var targetMsi = CompileVersioned("PropNoMutateApp", new Version(2, 0, 0));

        var beforeBytes = File.ReadAllBytes(targetMsi);
        var beforeWriteTimeUtc = File.GetLastWriteTimeUtc(targetMsi);

        var modelResult = new TransformBuilder()
            .BaseMsi(baseMsi)
            .TargetMsi(targetMsi)
            .SetProperty("MUTATIONCHECKPROP", "value")
            .Build();
        Assert.True(modelResult.IsSuccess, modelResult.IsFailure ? modelResult.Error.Message : null);

        var compileResult = new TransformCompiler().Compile(modelResult.Value, OutputDir());
        Assert.True(compileResult.IsSuccess, compileResult.IsFailure ? compileResult.Error.Message : null);

        var afterBytes = File.ReadAllBytes(targetMsi);
        var afterWriteTimeUtc = File.GetLastWriteTimeUtc(targetMsi);

        Assert.Equal(beforeWriteTimeUtc, afterWriteTimeUtc);
        Assert.Equal(beforeBytes, afterBytes);
    }

    [Fact]
    public void Compile_WithPropertyChanges_LeavesNoTempWorkingCopyBehindOnSuccess()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        var baseMsi = CompileVersioned("PropNoLeakApp", new Version(1, 0, 0));
        var targetMsi = CompileVersioned("PropNoLeakApp", new Version(2, 0, 0));

        var modelResult = new TransformBuilder()
            .BaseMsi(baseMsi)
            .TargetMsi(targetMsi)
            .SetProperty("LEAKCHECKPROP", "value")
            .Build();
        Assert.True(modelResult.IsSuccess, modelResult.IsFailure ? modelResult.Error.Message : null);

        var outputDir = OutputDir();
        var compileResult = new TransformCompiler().Compile(modelResult.Value, outputDir);
        Assert.True(compileResult.IsSuccess, compileResult.IsFailure ? compileResult.Error.Message : null);

        // Only the .mst artifact should remain in the output directory -- no leftover working copy.
        var remaining = Directory.GetFiles(outputDir);
        Assert.Single(remaining);
        Assert.Equal(".mst", Path.GetExtension(remaining[0]), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Compile_WithPropertyChanges_LeavesNoTempWorkingCopyBehindOnFailure()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        var targetMsi = CompileVersioned("PropNoLeakFailApp", new Version(1, 0, 0));

        // A corrupt/non-MSI "base" file exists on disk (passes the File.Exists check) but fails
        // native MsiDatabase.Open -- this fires AFTER the property-change working copy has
        // already been created, exercising the finally-block cleanup on a genuine failure path.
        var corruptBase = Path.Combine(_tempDir, "corrupt-base.msi");
        File.WriteAllText(corruptBase, "this is not a real MSI database");

        var modelResult = new TransformBuilder()
            .BaseMsi(corruptBase)
            .TargetMsi(targetMsi)
            .SetProperty("LEAKCHECKPROP", "value")
            .Build();
        Assert.True(modelResult.IsSuccess, modelResult.IsFailure ? modelResult.Error.Message : null);

        var outputDir = OutputDir();
        var compileResult = new TransformCompiler().Compile(modelResult.Value, outputDir);
        Assert.True(compileResult.IsFailure);

        Assert.Empty(Directory.GetFiles(outputDir));
    }

    [Fact]
    public void Compile_WithPropertyChanges_TargetMsiLockedByAnotherProcess_ReturnsIoErrorInsteadOfThrowing()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        // Regression test for a CodeRabbit finding: the property-changes path's File.Copy of the
        // target MSI into a private working copy was the only I/O on that path NOT translated
        // into a Result<string>.Failure -- a locked file, full disk, or permissions error would
        // throw straight through Compile and break the Result<T> contract the rest of the method
        // (and the codebase convention) relies on. An exclusive FileShare.None handle on the
        // target MSI makes File.Copy's read-open genuinely fail with an IOException; this proves
        // that failure now comes back as a typed Result rather than an unhandled exception.
        var baseMsi = CompileVersioned("PropLockedApp", new Version(1, 0, 0));
        var targetMsi = CompileVersioned("PropLockedApp", new Version(2, 0, 0));

        var modelResult = new TransformBuilder()
            .BaseMsi(baseMsi)
            .TargetMsi(targetMsi)
            .SetProperty("LOCKEDCOPYPROP", "value")
            .Build();
        Assert.True(modelResult.IsSuccess, modelResult.IsFailure ? modelResult.Error.Message : null);

        using (new FileStream(targetMsi, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var compileResult = new TransformCompiler().Compile(modelResult.Value, OutputDir());

            Assert.True(compileResult.IsFailure);
            Assert.Equal(ErrorKind.IoError, compileResult.Error.Kind);
        }
    }
}
