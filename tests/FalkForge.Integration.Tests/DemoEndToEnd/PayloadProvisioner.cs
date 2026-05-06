using System.Runtime.Versioning;
using System.Security.Cryptography;
using FalkForge.Builders;
using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Models;
using FalkForge.Platform.Windows;

namespace FalkForge.Integration.Tests.DemoEndToEnd;

/// <summary>
/// Creates stub payload artifacts in demo project directories at test-run time.
/// All created files are tracked for cleanup after the test suite.
/// </summary>
/// <remarks>
/// Stubs for bundle packages (MSI, EXE, MSU) are random-bytes blobs — the
/// <see cref="FalkForge.Compiler.Bundle.Compilation.BundleCompiler"/> only
/// needs the file to exist so it can stream SHA-256 and read the size; it does
/// not parse the format.
///
/// Stubs for <c>PatchCompiler</c> and <c>TransformCompiler</c> payloads must be
/// real MSI databases because msi.dll <c>MsiOpenDatabase</c> is called on them.
/// Those are compiled on-the-fly via <see cref="MsiAuthoring.Compile"/>.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class PayloadProvisioner
{
    // Per-demo stub manifests: relative path inside the project dir → stub kind.
    // Paths use forward slashes; provisioner normalises to platform separators.
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<PayloadStubEntry>> StubManifest =
        new Dictionary<string, IReadOnlyList<PayloadStubEntry>>(StringComparer.OrdinalIgnoreCase)
        {
            ["35-bundle-simple"] = [
                new("MyApp.msi", PayloadStubKind.AnyBlob),
            ],
            ["36-bundle-exe-package"] = [
                new("vcredist_x64.exe", PayloadStubKind.AnyBlob),
                new("MyApp.msi",        PayloadStubKind.AnyBlob),
            ],
            ["37-bundle-msu-package"] = [
                new("windows-hotfix-kb123456.msu", PayloadStubKind.AnyBlob),
                new("MyApp.msi",                   PayloadStubKind.AnyBlob),
            ],
            ["38-bundle-nested"] = [
                new("ChildSetup.exe", PayloadStubKind.AnyBlob),
                new("ParentApp.msi",  PayloadStubKind.AnyBlob),
            ],
            ["40-bundle-variables"] = [
                new("CoreApp.msi",      PayloadStubKind.AnyBlob),
                new("OptionalTools.msi", PayloadStubKind.AnyBlob),
            ],
            ["41-bundle-rollback"] = [
                new("Runtime.msi", PayloadStubKind.AnyBlob),
                new("MyApp.msi",   PayloadStubKind.AnyBlob),
            ],
            ["42-bundle-update-feed"] = [
                new("MyApp.msi", PayloadStubKind.AnyBlob),
            ],
            ["43-bundle-layout"] = [
                new("Core.msi",   PayloadStubKind.AnyBlob),
                new("Extras.msi", PayloadStubKind.AnyBlob),
            ],
            ["45-patch"] = [
                new("payload/app-v1.msi", PayloadStubKind.RealMsiV1),
                new("payload/app-v2.msi", PayloadStubKind.RealMsiV2),
            ],
            ["53-delta-updates"] = [
                new("MyApp.msi", PayloadStubKind.AnyBlob),
            ],
        };

    /// <summary>
    /// Materialises stub payloads for <paramref name="demo"/> into its project directory.
    /// Returns the absolute paths of files that were newly created (existing files are skipped).
    /// Returns an empty list if the demo has no stub manifest entry.
    /// </summary>
    public IReadOnlyList<string> Provision(DemoExpectation demo)
    {
        var demoKey = demo.Name; // e.g. "35-bundle-simple"
        if (!StubManifest.TryGetValue(demoKey, out var entries))
            return [];

        var created = new List<string>();

        var projectDir = Path.GetDirectoryName(demo.ProjectPath)
            ?? throw new InvalidOperationException($"Cannot determine project dir for {demo.Name}");

        foreach (var entry in entries)
        {
            var normalised = entry.RelativePath.Replace('/', Path.DirectorySeparatorChar);

            // Defense-in-depth: no path traversal
            if (normalised.Split(Path.DirectorySeparatorChar)
                .Any(seg => seg == ".." || seg == "."))
                throw new InvalidOperationException(
                    $"Stub path '{entry.RelativePath}' contains traversal segments.");

            var fullPath = Path.GetFullPath(Path.Combine(projectDir, normalised));

            // Ensure the path stays inside the project dir
            if (!fullPath.StartsWith(projectDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(fullPath, projectDir, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Stub path '{entry.RelativePath}' resolves outside project dir.");

            // Skip if already present (don't overwrite repo-tracked files)
            if (File.Exists(fullPath))
                continue;

            var dir = Path.GetDirectoryName(fullPath);
            if (dir is not null)
                Directory.CreateDirectory(dir);

            switch (entry.Kind)
            {
                case PayloadStubKind.AnyBlob:
                    CreateAnyBlob(fullPath);
                    break;

                case PayloadStubKind.RealMsiV1:
                    CompileStubMsi(fullPath, version: new Version(1, 0, 0),
                        productCode: new Guid("AAAAAAAA-AAAA-4AAA-AAAA-AAAAAAAAAAAA"),
                        upgradeCode: new Guid("BBBBBBBB-BBBB-4BBB-BBBB-BBBBBBBBBBBB"));
                    break;

                case PayloadStubKind.RealMsiV2:
                    CompileStubMsi(fullPath, version: new Version(1, 1, 0),
                        productCode: new Guid("CCCCCCCC-CCCC-4CCC-CCCC-CCCCCCCCCCCC"),
                        upgradeCode: new Guid("BBBBBBBB-BBBB-4BBB-BBBB-BBBBBBBBBBBB"));
                    break;

                default:
                    throw new InvalidOperationException($"Unknown PayloadStubKind: {entry.Kind}");
            }

            created.Add(fullPath);
        }

        return created;
    }

    private static void CreateAnyBlob(string path)
    {
        // 256 random bytes — enough for SHA-256 streaming; not parsed by BundleCompiler
        var bytes = RandomNumberGenerator.GetBytes(256);
        File.WriteAllBytes(path, bytes);
    }

    private static void CompileStubMsi(string outputPath, Version version, Guid productCode, Guid upgradeCode)
    {
        // Compile a minimal single-file MSI via the recipe pipeline.
        // MsiAuthoring.Compile writes "Name-Version.msi" into the directory;
        // we then rename/move it to the caller-requested outputPath.
        var tempDir = Path.Combine(Path.GetTempPath(), $"falk-stub-{Guid.NewGuid():N}");
        var tempContent = Path.Combine(tempDir, "stub.txt");
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(tempContent, "stub");

            var builder = new PackageBuilder
            {
                Name = "Stub",
                Manufacturer = "Test",
                Version = version,
                ProductCode = productCode,
                UpgradeCode = upgradeCode,
            };
            builder.Files(fs => fs
                .Add(tempContent)
                .To(KnownFolder.ProgramFiles / "Stub"));
            var package = builder.Build();

            var result = MsiAuthoring.Compile(package, tempDir);
            if (result.IsFailure)
                throw new InvalidOperationException(
                    $"Stub MSI compilation failed: {result.Error.Message}");

            File.Move(result.Value, outputPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempContent))
                try { File.Delete(tempContent); } catch { /* best effort */ }
            if (Directory.Exists(tempDir))
                try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}

internal enum PayloadStubKind
{
    /// <summary>Any non-empty byte blob. Sufficient for bundle package embedding.</summary>
    AnyBlob,

    /// <summary>Real MSI database v1.0.0 — required where msi.dll opens the file.</summary>
    RealMsiV1,

    /// <summary>Real MSI database v1.1.0 with same UpgradeCode — required for patch creation.</summary>
    RealMsiV2,
}

internal sealed record PayloadStubEntry(string RelativePath, PayloadStubKind Kind);
