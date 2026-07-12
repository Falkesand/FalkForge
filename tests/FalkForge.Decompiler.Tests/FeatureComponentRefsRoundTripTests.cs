using System.Runtime.Versioning;
using FalkForge;
using FalkForge.Builders;
using FalkForge.Compiler.Msi;
using FalkForge.Decompiler;
using FalkForge.Models;
using FalkForge.Platform.Windows;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Decompiler.Tests;

/// <summary>
/// Proves that <see cref="FeatureModel.ComponentRefs"/> is a live feature-&gt;component wiring
/// axis in the MSI compiler, not just a decompiler-only field. The decompiler populates
/// ComponentRefs from an existing MSI's FeatureComponents table; before this was honored by the
/// compiler, a decompile-&gt;recompile round trip silently dropped the mapping (every component
/// collapsed onto the first feature) because the decompiled components carry no FeatureRef.
///
/// All assertions are against the compiled MSI's FeatureComponents table — the only place the
/// mapping actually lives.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class FeatureComponentRefsRoundTripTests
{
    // ── A9a: explicit ComponentRefs (no FeatureRef) drive FeatureComponents ──────────────

    [Fact]
    public void Compile_ExplicitComponentRefs_MapsComponentUnderThatFeatureNotTheDefault()
    {
        using var temp = new TempDir();
        var sourceFile = temp.WriteFile("app.exe");

        // Resolve first to discover the synthesized component id for the file.
        var installDir = KnownFolder.ProgramFiles / "TestCorp" / "A9a";
        var probe = ModelWithFiles("A9a",
            [FileEntry(sourceFile, installDir, "app.exe")],
            []);
        var componentId = ResolveSingleComponentId(probe);

        // Two features: F1 is first (the default-fallback target). The file has no FeatureRef;
        // only F2's ComponentRefs claims it. A working wiring must place it under F2, never F1.
        var model = ModelWithFiles("A9a",
            [FileEntry(sourceFile, installDir, "app.exe")],
            [
                new FeatureModel { Id = "F1", Title = "Feature One" },
                new FeatureModel { Id = "F2", Title = "Feature Two", ComponentRefs = [componentId] }
            ]);

        var rows = CompileAndReadFeatureComponents(model, temp.Path("out_a"));

        Assert.Contains(rows, r => r.Feature == "F2" && r.Component == componentId);
        Assert.DoesNotContain(rows, r => r.Feature == "F1" && r.Component == componentId);
    }

    // ── A9b: overlap (FeatureRef + ComponentRefs to the SAME feature) → exactly one row ──

    [Fact]
    public void Compile_ComponentRefAndFeatureRefToSameFeature_ProducesExactlyOneRow()
    {
        using var temp = new TempDir();
        var sourceFile = temp.WriteFile("app.exe");
        var installDir = KnownFolder.ProgramFiles / "TestCorp" / "A9b";

        var probe = ModelWithFiles("A9b",
            [FileEntry(sourceFile, installDir, "app.exe")],
            []);
        var componentId = ResolveSingleComponentId(probe);

        // The file is FeatureRef-stamped to F2 AND F2 lists it in ComponentRefs. The two paths
        // must collapse to a single (F2, component) row — no duplicate primary key.
        var model = ModelWithFiles("A9b",
            [FileEntry(sourceFile, installDir, "app.exe", featureRef: "F2")],
            [
                new FeatureModel { Id = "F1", Title = "Feature One" },
                new FeatureModel { Id = "F2", Title = "Feature Two", ComponentRefs = [componentId] }
            ]);

        var rows = CompileAndReadFeatureComponents(model, temp.Path("out_b"));

        var matches = rows.Where(r => r.Component == componentId).ToList();
        Assert.Single(matches);
        Assert.Equal("F2", matches[0].Feature);
    }

    // ── A9c: dangling ComponentRefs (unknown component id) → fail loud ───────────────────

    [Fact]
    public void Compile_DanglingComponentRef_FailsWithClearError()
    {
        using var temp = new TempDir();
        var sourceFile = temp.WriteFile("app.exe");
        var installDir = KnownFolder.ProgramFiles / "TestCorp" / "A9c";

        var model = ModelWithFiles("A9c",
            [FileEntry(sourceFile, installDir, "app.exe")],
            [new FeatureModel { Id = "F1", Title = "Feature One", ComponentRefs = ["GHOST_COMPONENT_ID"] }]);

        var compiler = new MsiCompiler(new WindowsFileSystem());
        var result = compiler.Compile(model, temp.Path("out_c"));

        Assert.True(result.IsFailure, "Expected compile to fail loud on a dangling ComponentRefs entry.");
        Assert.Contains("GHOST_COMPONENT_ID", result.Error.Message, StringComparison.Ordinal);
    }

    // ── A9d: decompile → recompile round trip preserves the feature→component mapping ────

    [Fact]
    public void Compile_Decompile_Recompile_PreservesFeatureComponentMapping()
    {
        using var temp = new TempDir();
        var fileA = temp.WriteFile("alpha.exe");
        var fileB = temp.WriteFile("beta.exe");

        // M1: two features, each owning a distinct file via FeatureRef stamping.
        var m1 = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "RoundTrip";
            p.Manufacturer = "TestCorp";
            p.Version = new Version(1, 0, 0);
            p.Feature("Alpha", f =>
                f.Files(fs => fs.Add(fileA).To(KnownFolder.ProgramFiles / "TestCorp" / "RoundTrip" / "Alpha")));
            p.Feature("Beta", f =>
                f.Files(fs => fs.Add(fileB).To(KnownFolder.ProgramFiles / "TestCorp" / "RoundTrip" / "Beta")));
        });

        var compiler = new MsiCompiler(new WindowsFileSystem());
        var msi1 = compiler.Compile(m1, temp.Path("out_m1"));
        Assert.True(msi1.IsSuccess, $"M1 compile failed: {(msi1.IsFailure ? msi1.Error.Message : "")}");

        var expected = ReadFeatureComponents(msi1.Value);
        var compAlpha = Assert.Single(expected, r => r.Feature == "Alpha").Component;
        var compBeta = Assert.Single(expected, r => r.Feature == "Beta").Component;
        Assert.NotEqual(compAlpha, compBeta);

        // Decompile the compiled MSI back to a PackageModel. Its features now carry ComponentRefs
        // (the decompiler's only channel for the mapping — the reconstructed files carry no FeatureRef).
        var m2 = new MsiDecompiler().Decompile(msi1.Value);
        Assert.True(m2.IsSuccess, $"Decompile failed: {(m2.IsFailure ? m2.Error.Message : "")}");

        // The decompiler cannot recover the original file bytes, so point each reconstructed file at
        // a real stub for the recompile's cabinet step. Preserve the decompiled ComponentId so the
        // recompiled component identity matches the ComponentRefs entries. Neither touches the mapping.
        var stubDir = temp.Path("stubs");
        Directory.CreateDirectory(stubDir);
        var recompileFiles = m2.Value.Files.Select(f =>
        {
            var stub = Path.Combine(stubDir, f.FileName);
            File.WriteAllText(stub, "stub bytes");
            return new FileEntryModel
            {
                SourcePath = stub,
                TargetDirectory = f.TargetDirectory,
                FileName = f.FileName,
                IsKeyPath = f.IsKeyPath,
                ComponentId = f.ComponentId,
                FeatureRef = f.FeatureRef,
                Vital = f.Vital
            };
        }).ToList();

        var recompileModel = new PackageModel
        {
            Name = m2.Value.Name,
            Manufacturer = m2.Value.Manufacturer,
            Version = m2.Value.Version,
            UpgradeCode = m2.Value.UpgradeCode,
            ProductCode = m2.Value.ProductCode,
            DefaultInstallDirectory = m2.Value.DefaultInstallDirectory
                ?? (KnownFolder.ProgramFiles / "TestCorp" / "RoundTrip"),
            Files = recompileFiles,
            Features = m2.Value.Features
        };

        var msi2 = compiler.Compile(recompileModel, temp.Path("out_m2"));
        Assert.True(msi2.IsSuccess, $"Recompile failed: {(msi2.IsFailure ? msi2.Error.Message : "")}");

        var actual = ReadFeatureComponents(msi2.Value);

        // The mapping must survive: each component stays under its own feature, not collapsed.
        Assert.Contains(actual, r => r.Feature == "Alpha" && r.Component == compAlpha);
        Assert.Contains(actual, r => r.Feature == "Beta" && r.Component == compBeta);
        Assert.DoesNotContain(actual, r => r.Feature == "Alpha" && r.Component == compBeta);
        Assert.DoesNotContain(actual, r => r.Feature == "Beta" && r.Component == compAlpha);
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────

    private static FileEntryModel FileEntry(string sourcePath, InstallPath targetDir, string fileName,
        string? featureRef = null)
        => new()
        {
            SourcePath = sourcePath,
            TargetDirectory = targetDir,
            FileName = fileName,
            IsKeyPath = true,
            Vital = true,
            FeatureRef = featureRef
        };

    private static PackageModel ModelWithFiles(string name, IReadOnlyList<FileEntryModel> files,
        IReadOnlyList<FeatureModel> features)
        => new()
        {
            Name = name,
            Manufacturer = "TestCorp",
            Version = new Version(1, 0, 0),
            UpgradeCode = new Guid("11111111-1111-1111-1111-111111111111"),
            ProductCode = new Guid("22222222-2222-2222-2222-222222222222"),
            DefaultInstallDirectory = KnownFolder.ProgramFiles / "TestCorp" / name,
            Files = files,
            Features = features
        };

    private static string ResolveSingleComponentId(PackageModel model)
    {
        var resolved = new ComponentResolver(new WindowsFileSystem()).Resolve(model);
        Assert.True(resolved.IsSuccess, $"Resolve failed: {(resolved.IsFailure ? resolved.Error.Message : "")}");
        return Assert.Single(resolved.Value.Components).Id;
    }

    private static List<(string Feature, string Component)> CompileAndReadFeatureComponents(
        PackageModel model, string outputDir)
    {
        var compiler = new MsiCompiler(new WindowsFileSystem());
        var result = compiler.Compile(model, outputDir);
        Assert.True(result.IsSuccess, $"Compile failed: {(result.IsFailure ? result.Error.Message : "")}");
        return ReadFeatureComponents(result.Value);
    }

    private static List<(string Feature, string Component)> ReadFeatureComponents(string msiPath)
    {
        using var db = MsiDatabase.Open(msiPath, readOnly: true).Value;
        var rows = db.QueryRows("SELECT `Feature_`, `Component_` FROM `FeatureComponents`", 2).Value;
        return rows.Select(r => (r[0]!, r[1]!)).ToList();
    }

    private sealed class TempDir : IDisposable
    {
        private readonly string _root =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"A9_{Guid.NewGuid():N}");

        public TempDir() => Directory.CreateDirectory(_root);

        public string Path(string sub) => System.IO.Path.Combine(_root, sub);

        public string WriteFile(string name)
        {
            var p = System.IO.Path.Combine(_root, name);
            File.WriteAllText(p, "fake payload for feature-component test");
            return p;
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, true);
        }
    }
}
