using System;
using System.Collections.Generic;
using FalkForge;
using FalkForge.Builders;
using FalkForge.Compiler.Msi;
using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Extensibility;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe;

/// <summary>
///     Tests for PackageCode (SummaryInformation PID 9 / RevisionNumber) derivation rules.
///
///     MSI spec: non-identical packages MUST have unique PackageCodes. Today
///     PackageCode always equals ProductCode, so two reproducible builds that
///     differ only in payload bytes share a PackageCode — breaking secure repair
///     (SECREPAIR, KB2918614) because Windows caches the source-package hash at
///     install time and re-validates it during repair. These tests encode the
///     intended behavior for issue #1.
/// </summary>
public sealed class PackageCodeDerivationTests
{
    // Fixed Unix epoch: 2020-01-01T00:00:00Z — makes reproducible ProductCode
    // deterministic across runs.
    private const long TestEpoch = 1577836800L;

    /// <summary>
    ///     Guard: identical inputs in reproducible mode must produce identical PackageCode.
    ///     If this fails after the GREEN implementation, we have broken reproducibility.
    /// </summary>
    [Fact]
    public void Reproducible_SameInputs_SamePackageCode()
    {
        // Two ResolvedPackage instances with the same identity, version, and
        // resolved files. The compiler must produce identical RevisionNumber
        // (PackageCode) for both — content-digest stability.
        var tempDir = CreateTempDir();
        try
        {
            // Payload bytes written to disk so the GREEN impl can SHA-256 them.
            var payloadPath = WriteTempFile(tempDir, "payload.dll", [0x4D, 0x5A, 0x01, 0x02]);

            var resolved1 = BuildResolvedPackage(
                productCode: new Guid("AAAAAAAA-0000-0000-0000-000000000001"),
                payloadPath: payloadPath,
                reproducible: true);

            var resolved2 = BuildResolvedPackage(
                productCode: new Guid("AAAAAAAA-0000-0000-0000-000000000001"),
                payloadPath: payloadPath,
                reproducible: true);

            var recipe1 = MsiRecipeBuilder.Build(resolved1, [], new MsiRecipeBuildOptions()).Value;
            var recipe2 = MsiRecipeBuilder.Build(resolved2, [], new MsiRecipeBuildOptions()).Value;

            Assert.Equal(recipe1.SummaryInfo.RevisionNumber, recipe2.SummaryInfo.RevisionNumber);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    /// <summary>
    ///     Core bug test: in reproducible mode, changing a payload file's bytes
    ///     must produce a different PackageCode even when identity and version are
    ///     unchanged. Today both builds return the same ProductCode-derived GUID,
    ///     making this assertion fail.
    ///
    ///     MSI rule: a package with a different byte sequence is not the same
    ///     package; it must carry a unique PackageCode so Windows Installer
    ///     distinguishes it from the cached copy at repair time.
    /// </summary>
    [Fact]
    public void Reproducible_DifferentPayloadBytes_DifferentPackageCode()
    {
        var tempDir = CreateTempDir();
        try
        {
            // Both builds share the same product identity but carry different payload bytes.
            // GREEN impl must hash these files as part of PackageCode derivation.
            var payloadV1 = WriteTempFile(tempDir, "payload_v1.dll", [0x4D, 0x5A, 0xAA, 0xBB]);
            var payloadV2 = WriteTempFile(tempDir, "payload_v2.dll", [0x4D, 0x5A, 0xCC, 0xDD]);

            var productCode = new Guid("BBBBBBBB-0000-0000-0000-000000000002");

            var resolvedV1 = BuildResolvedPackage(productCode, payloadPath: payloadV1, reproducible: true);
            var resolvedV2 = BuildResolvedPackage(productCode, payloadPath: payloadV2, reproducible: true);

            var recipeV1 = MsiRecipeBuilder.Build(resolvedV1, [], new MsiRecipeBuildOptions()).Value;
            var recipeV2 = MsiRecipeBuilder.Build(resolvedV2, [], new MsiRecipeBuildOptions()).Value;

            // RED today: both equal pkg.ProductCode.ToString("B").ToUpperInvariant()
            Assert.NotEqual(recipeV1.SummaryInfo.RevisionNumber, recipeV2.SummaryInfo.RevisionNumber);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    /// <summary>
    ///     In reproducible mode the PackageCode must be derived from content
    ///     (a digest GUID), NOT copied from ProductCode. ProductCode encodes
    ///     identity only; PackageCode must encode identity+content. Today
    ///     RevisionNumber == ProductCode, violating this invariant.
    /// </summary>
    [Fact]
    public void Reproducible_PackageCodeDiffersFromProductCode()
    {
        var tempDir = CreateTempDir();
        try
        {
            var payloadPath = WriteTempFile(tempDir, "payload.dll", [0x4D, 0x5A, 0x01, 0x02]);
            var productCode = new Guid("CCCCCCCC-0000-0000-0000-000000000003");

            var resolved = BuildResolvedPackage(productCode, payloadPath, reproducible: true);
            var recipe = MsiRecipeBuilder.Build(resolved, [], new MsiRecipeBuildOptions()).Value;

            var productCodeFormatted = productCode.ToString("B").ToUpperInvariant();

            // RED today: RevisionNumber == productCodeFormatted
            Assert.NotEqual(productCodeFormatted, recipe.SummaryInfo.RevisionNumber);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    /// <summary>
    ///     Secondary defect: in normal (non-reproducible) mode, an explicit
    ///     ProductCode must NOT be reused as PackageCode across independent
    ///     Build() calls. Each call to PackageBuilder.Build() represents a
    ///     distinct packaging event; MSI requires a fresh PackageCode each time
    ///     so that the two resulting bytes-different MSIs are distinguishable.
    ///
    ///     Today both calls produce RevisionNumber == ProductCode, making them
    ///     identical. This causes the same SECREPAIR hazard as the reproducible
    ///     mode defect when different build runs ship the same product.
    /// </summary>
    [Fact]
    public void NormalMode_ExplicitProductCode_FreshPackageCodePerBuild()
    {
        var tempDir = CreateTempDir();
        try
        {
            var payloadPath = WriteTempFile(tempDir, "payload.dll", [0x4D, 0x5A, 0x01, 0x02]);

            // Build two independent ResolvedPackages in normal mode with the same
            // explicit ProductCode — simulates two separate build invocations.
            var explicitProductCode = new Guid("DDDDDDDD-0000-0000-0000-000000000004");

            var resolved1 = BuildResolvedPackage(explicitProductCode, payloadPath, reproducible: false);
            var resolved2 = BuildResolvedPackage(explicitProductCode, payloadPath, reproducible: false);

            var recipe1 = MsiRecipeBuilder.Build(resolved1, [], new MsiRecipeBuildOptions()).Value;
            var recipe2 = MsiRecipeBuilder.Build(resolved2, [], new MsiRecipeBuildOptions()).Value;

            // RED today: both equal explicitProductCode.ToString("B").ToUpperInvariant()
            Assert.NotEqual(recipe1.SummaryInfo.RevisionNumber, recipe2.SummaryInfo.RevisionNumber);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    ///     Builds a minimal <see cref="ResolvedPackage" /> suitable for recipe
    ///     tests. Uses empty Components/Files lists to avoid FK validation
    ///     failures that are orthogonal to PackageCode derivation. The
    ///     <paramref name="payloadPath" /> file is written by callers for the
    ///     GREEN implementation to read (SHA-256 hashing); today MsiRecipeBuilder
    ///     does not read file bytes so the path is stored but unused at recipe level.
    /// </summary>
    private static ResolvedPackage BuildResolvedPackage(
        Guid productCode,
        string payloadPath,
        bool reproducible)
    {
        var installDir = KnownFolder.ProgramFiles / "PackageCodeTests" / "App";

        var resolvedFile = new ResolvedFile
        {
            SourcePath = payloadPath,
            TargetDirectory = installDir,
            FileName = System.IO.Path.GetFileName(payloadPath),
            FileSize = new System.IO.FileInfo(payloadPath).Length,
            ComponentId = "MainComponent",
            FileId = "file_payload",
        };

        ReproducibleBuildOptions? reproOptions = reproducible
            ? new ReproducibleBuildOptions { SourceDateEpoch = TestEpoch }
            : null;

        var package = new PackageModel
        {
            Name = "PackageCodeTest",
            Manufacturer = "FalkForge Tests",
            Version = new Version(1, 0, 0),
            ProductCode = productCode,
            ReproducibleOptions = reproOptions,
        };

        // Empty Components/Files: avoids FK validation cascades unrelated to
        // PackageCode. The resolvedFile is included in Files so the GREEN impl
        // can hash its SourcePath content; Components is empty to prevent
        // Feature→Directory FK chains that require a fully wired model.
        return new ResolvedPackage
        {
            Package = package,
            Components = [],
            Files = [resolvedFile],
        };
    }

    private static string CreateTempDir()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"falk-pkgcode-{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(dir);
        return dir;
    }

    private static string WriteTempFile(string dir, string fileName, byte[] content)
    {
        var path = System.IO.Path.Combine(dir, fileName);
        System.IO.File.WriteAllBytes(path, content);
        return path;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (System.IO.Directory.Exists(path))
                System.IO.Directory.Delete(path, recursive: true);
        }
        catch (System.IO.IOException)
        {
            // Best-effort cleanup — test isolation is not affected.
        }
    }
}
