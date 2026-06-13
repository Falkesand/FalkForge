using FalkForge.Engine.Protocol.Bundle;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

namespace FalkForge.Decompiler.Tests;

/// <summary>
/// Tests for MigrationProjectGenerator - bundle (.exe) path, FALKBUNDLE branch.
///
/// WHY these tests matter:
/// "forge migrate" on a native FalkForge bundle must emit a project the user can
/// compile and run. Unlike the illustrative `forge decompile` output (which calls a
/// non-existent Installer.BuildBundle(b => ...) overload), the migrated Program.cs
/// must use the REAL runnable entry point:
///   return Installer.BuildBundle(args, outputPath =>
///       { var bundle = new BundleBuilder()...Build(); return new BundleCompiler().Compile(bundle, outputPath); });
/// and the csproj must reference FalkForge.Compiler.Bundle so BundleBuilder/BundleCompiler resolve.
///
/// These tests use an injected IBundleAccess mock so they run cross-machine without a
/// real bundle EXE. Payload-byte extraction needs a real file on disk, so the
/// alignment-with-bytes invariant is covered separately by the on-disk test
/// (see <see cref="MigrationBundlePayloadAlignmentTests"/>).
/// </summary>
public sealed class MigrationBundleGeneratorTests
{
    private const string DummySourcePath = "../../src";
    private const string ProjectName = "MyBundleMigrated";

    private static InstallerManifest CreateManifest(
        PackageInfo[]? packages = null,
        ManifestChainItem[]? chain = null) => new()
    {
        Name = "Migrated Bundle",
        Manufacturer = "Acme",
        Version = "3.2.1",
        BundleId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        UpgradeCode = Guid.Parse("22222222-2222-2222-2222-222222222222"),
        Scope = InstallScope.PerMachine,
        Packages = packages ?? [],
        RelatedBundles = [],
        Chain = chain ?? []
    };

    private static PackageInfo Pkg(string id = "MyApp", string source = "MyApp.msi") => new()
    {
        Id = id,
        Type = PackageType.MsiPackage,
        DisplayName = id,
        SourcePath = source,
        Sha256Hash = "abc"
    };

    private static MigrationResult RunNativeMock(InstallerManifest manifest, TocEntry[]? toc = null)
    {
        var mock = new MockBundleAccess().WithManifest(manifest).WithToc(toc ?? []);
        var generator = new MigrationProjectGenerator(new BundleDecompiler(mock));
        var opts = new MigrationOptions(DummySourcePath, ProjectName);

        var result = generator.Generate("ignored.exe", opts);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
        return result.Value;
    }

    // --- result shape ---

    [Fact]
    public void Generate_NativeBundle_ReturnsThreeTextFiles()
    {
        var value = RunNativeMock(CreateManifest());

        Assert.Equal(3, value.TextFiles.Count);
        Assert.True(value.TextFiles.ContainsKey("Program.cs"));
        Assert.True(value.TextFiles.ContainsKey($"{ProjectName}.csproj"));
        Assert.True(value.TextFiles.ContainsKey("MIGRATION-REPORT.md"));
    }

    [Fact]
    public void Generate_NativeBundle_UnmappedIsEmpty()
    {
        // FALKBUNDLE has no WiX-specific unmapped features.
        var value = RunNativeMock(CreateManifest());

        Assert.Empty(value.Unmapped);
    }

    // --- Program.cs (runnable shape) ---

    [Fact]
    public void Generate_NativeProgramCs_UsesRunnableBuildBundleEntryPoint()
    {
        // WHY: the migrated program must actually compile a bundle at runtime. The
        // runnable overload is Installer.BuildBundle(args, Func<string,Result<string>>);
        // the decompile-style Installer.BuildBundle(b => ...) does not exist and would
        // not compile.
        var value = RunNativeMock(CreateManifest());
        var prog = value.TextFiles["Program.cs"];

        Assert.Contains("return Installer.BuildBundle(args,", prog);
        Assert.Contains("new BundleCompiler().Compile(", prog);
        Assert.Contains("var bundle = ", prog);
        Assert.DoesNotContain("Installer.BuildBundle(b =>", prog);
    }

    [Fact]
    public void Generate_NativeProgramCs_ConstructsBundleBuilder()
    {
        // The runnable program builds a real BundleBuilder, not a config lambda.
        var value = RunNativeMock(CreateManifest());
        var prog = value.TextFiles["Program.cs"];

        Assert.Contains("new BundleBuilder()", prog);
        Assert.Contains("b.Name(\"Migrated Bundle\");", prog);
    }

    [Fact]
    public void Generate_NativeProgramCs_HasRequiredUsings()
    {
        var value = RunNativeMock(CreateManifest());
        var prog = value.TextFiles["Program.cs"];

        Assert.Contains("using FalkForge;", prog);
        Assert.Contains("using FalkForge.Compiler.Bundle.Builders;", prog);
        Assert.Contains("using FalkForge.Compiler.Bundle.Compilation;", prog);
    }

    [Fact]
    public void Generate_NativeProgramCs_BuildBeforeCompile()
    {
        // 'bundle' must be declared before it is compiled.
        var value = RunNativeMock(CreateManifest());
        var prog = value.TextFiles["Program.cs"];

        var buildIdx = prog.IndexOf("var bundle = ", StringComparison.Ordinal);
        var compileIdx = prog.IndexOf("new BundleCompiler().Compile(", StringComparison.Ordinal);

        Assert.True(buildIdx >= 0, "bundle build line missing");
        Assert.True(compileIdx > buildIdx, "Compile must come after bundle build");
    }

    // --- csproj ---

    [Fact]
    public void Generate_NativeCsproj_ReferencesCompilerBundle()
    {
        // Without Compiler.Bundle reference BundleBuilder/BundleCompiler won't resolve.
        var value = RunNativeMock(CreateManifest());
        var csproj = value.TextFiles[$"{ProjectName}.csproj"];

        Assert.Contains($"{DummySourcePath}/FalkForge.Core/FalkForge.Core.csproj", csproj);
        Assert.Contains($"{DummySourcePath}/FalkForge.Compiler.Bundle/FalkForge.Compiler.Bundle.csproj", csproj);
    }

    [Fact]
    public void Generate_NativeCsproj_TargetsNet10WindowsExeWithPayloadGlob()
    {
        var value = RunNativeMock(CreateManifest());
        var csproj = value.TextFiles[$"{ProjectName}.csproj"];

        Assert.Contains("Microsoft.NET.Sdk", csproj);
        Assert.Contains("net10.0-windows", csproj);
        Assert.Contains("<OutputType>Exe</OutputType>", csproj);
        Assert.Contains("payload/**", csproj);
        Assert.Contains("PreserveNewest", csproj);
    }

    // --- report ---

    [Fact]
    public void Generate_NativeReport_IdentifiesFalkForgeBundleType()
    {
        var value = RunNativeMock(CreateManifest());
        var report = value.TextFiles["MIGRATION-REPORT.md"];

        Assert.StartsWith("#", report.TrimStart());
        Assert.Contains("ignored.exe", report);
        Assert.Contains("FalkForge bundle", report);
    }

    // --- payload-path alignment (resolver-level, no real bytes) ---

    [Fact]
    public void Generate_NativeProgramCs_EmitsPayloadRelativePackagePaths()
    {
        // WHY: the migrated chain must reference payload/<name>, the same key the
        // migration writes the extracted bytes to. The package's original SourcePath
        // (e.g. "MyApp.msi") is rewritten to the payload-relative key so the generated
        // code and the written payload file agree by construction.
        var pkg = Pkg(id: "MyApp", source: "MyApp.msi");
        var manifest = CreateManifest(
            packages: [pkg],
            chain: [new PackageManifestChainItem(pkg)]);

        var value = RunNativeMock(manifest, toc:
        [
            new TocEntry
            {
                PackageId = "MyApp",
                Offset = 0,
                CompressedSize = 0,
                OriginalSize = 0,
                Sha256Hash = "abc"
            }
        ]);
        var prog = value.TextFiles["Program.cs"];

        Assert.Contains("c.MsiPackage(\"payload/MyApp.msi\"", prog);
        Assert.DoesNotContain("c.MsiPackage(\"MyApp.msi\"", prog);
    }
}
