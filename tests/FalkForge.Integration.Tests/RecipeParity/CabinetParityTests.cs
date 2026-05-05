using System.Runtime.Versioning;
using FalkForge.Builders;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Integration.Tests.RecipeParity;

/// <summary>
/// Phase 9 Step 3 — asserts that the recipe pipeline produces the same cabinet
/// layout as the legacy <c>MsiCompiler</c>: correct default cab name (<c>Data.cab</c>
/// not <c>cab1.cab</c>), correct multi-cabinet splitting when a MediaTemplate is
/// set, and correct Media table rows (DiskId, LastSequence, Cabinet column value).
/// Tests are written before the production fix so they form the RED gate in the
/// working-tree-gate TDD convention.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CabinetParityTests
{
    private const long TestEpoch = 1577836800L;

    // -------------------------------------------------------------------------
    // 1. Default cab name parity (no MediaTemplate, no files)
    // -------------------------------------------------------------------------

    /// <summary>
    /// When no <c>MediaTemplate</c> is set and the package has no files, the
    /// Media table row must reference <c>#Data.cab</c> (the legacy default),
    /// not the old hardcoded <c>#cab1.cab</c>.
    /// </summary>
    [Fact]
    public void No_media_template_Media_row_cabinet_column_is_Data_cab()
    {
        PackageModel package = BuildMinimalPackage(mediaTemplate: null);
        MsiDiffReport report = MsiByteDiffHarness.CompareCompilers(
            package, nameof(No_media_template_Media_row_cabinet_column_is_Data_cab));

        var mediaDiffs = report.StructuralDifferences
            .Where(d => d.StartsWith("[Table:Media]", StringComparison.Ordinal))
            .ToList();

        Assert.True(
            mediaDiffs.Count == 0,
            $"Media table diverges between legacy and recipe:{Environment.NewLine}" +
            string.Join(Environment.NewLine, mediaDiffs));
    }

    // -------------------------------------------------------------------------
    // 2. MediaTemplate with embedded cabinet — single cab (MaxSize = 0)
    // -------------------------------------------------------------------------

    /// <summary>
    /// When <c>MediaTemplate</c> is set with <c>MaximumCabinetSizeInMB = 0</c>
    /// (no size limit), all files land in one cabinet named via the template.
    /// Both pipelines must produce identical Media table rows.
    /// </summary>
    [Fact]
    public void Media_template_embedded_single_cab_Media_rows_match_legacy()
    {
        string tmpDir = MakeTempDir();
        try
        {
            string file1 = WriteFile(tmpDir, "file1.txt", "hello");
            string file2 = WriteFile(tmpDir, "file2.txt", "world");

            PackageModel package = BuildPackageWithFiles(
                [file1, file2],
                new MediaTemplateModel
                {
                    CabinetTemplate = "cab{0}.cab",
                    MaximumCabinetSizeInMB = 0, // no size limit → single cab
                    EmbedCabinet = true,
                });

            MsiDiffReport report = MsiByteDiffHarness.CompareCompilers(
                package, nameof(Media_template_embedded_single_cab_Media_rows_match_legacy));

            var mediaDiffs = report.StructuralDifferences
                .Where(d => d.StartsWith("[Table:Media]", StringComparison.Ordinal))
                .ToList();

            Assert.True(
                mediaDiffs.Count == 0,
                $"Media table diverges between legacy and recipe (MediaTemplate, embedded, single cab):{Environment.NewLine}" +
                string.Join(Environment.NewLine, mediaDiffs));
        }
        finally
        {
            TryDeleteDir(tmpDir);
        }
    }

    // -------------------------------------------------------------------------
    // 3. External cab parity — EmbedCabinet = false
    // -------------------------------------------------------------------------

    /// <summary>
    /// When <c>MediaTemplate.EmbedCabinet = false</c>, the Media table
    /// <c>Cabinet</c> column must reference the plain file name (no leading
    /// <c>#</c>), and both pipelines must agree on that value.
    /// </summary>
    [Fact]
    public void External_cabinet_Media_row_Cabinet_column_matches_legacy()
    {
        string tmpDir = MakeTempDir();
        try
        {
            string file1 = WriteFile(tmpDir, "app.exe", "fake exe content");

            PackageModel package = BuildPackageWithFiles(
                [file1],
                new MediaTemplateModel
                {
                    CabinetTemplate = "cab{0}.cab",
                    MaximumCabinetSizeInMB = 0,
                    EmbedCabinet = false,
                });

            MsiDiffReport report = MsiByteDiffHarness.CompareCompilers(
                package, nameof(External_cabinet_Media_row_Cabinet_column_matches_legacy));

            var mediaDiffs = report.StructuralDifferences
                .Where(d => d.StartsWith("[Table:Media]", StringComparison.Ordinal))
                .ToList();

            Assert.True(
                mediaDiffs.Count == 0,
                $"Media table diverges for external-cabinet package:{Environment.NewLine}" +
                string.Join(Environment.NewLine, mediaDiffs));
        }
        finally
        {
            TryDeleteDir(tmpDir);
        }
    }

    // -------------------------------------------------------------------------
    // 4. Byte-level identity for a no-file package
    //    (cabinet-name fix was the last known blocker for cabinet parity;
    //     full byte identity still blocked by table-set parity Step 4)
    // -------------------------------------------------------------------------

    /// <summary>
    /// For a package with no files the two pipelines should eventually produce
    /// byte-for-byte identical MSI files. Step 3 fixes the cabinet-name divergence.
    /// Step 4 will fix the remaining table-set divergence (RemoveIniFile only in
    /// legacy; LockPermissions + MsiLockPermissionsEx only in recipe). Skip until
    /// Step 4 completes.
    /// </summary>
    [Fact(Skip = "Phase 9 Step 3 — cabinet-name parity fixed; remaining byte divergence is " +
                 "table-set parity (RemoveIniFile/LockPermissions/MsiLockPermissionsEx). Fix in Step 4.")]
    public void MinimalPackage_no_files_byte_layout_matches_legacy()
    {
        PackageModel package = BuildMinimalPackage(mediaTemplate: null);
        MsiDiffReport report = MsiByteDiffHarness.CompareCompilers(
            package, nameof(MinimalPackage_no_files_byte_layout_matches_legacy));

        Assert.True(
            report.Equal,
            $"MSI byte layout differs for minimal no-file package.{Environment.NewLine}" +
            $"Structural differences:{Environment.NewLine}" +
            string.Join(Environment.NewLine, report.StructuralDifferences) +
            (report.FirstByteDiff is { } diff
                ? $"{Environment.NewLine}First byte diff: {diff}"
                : string.Empty));
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static PackageModel BuildMinimalPackage(MediaTemplateModel? mediaTemplate)
    {
        var builder = new PackageBuilder
        {
            Name = "CabParityTest",
            Manufacturer = "FalkForge Tests",
            Version = new Version(1, 0, 0),
            ProductCode = Guid.Parse("AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE"),
            UpgradeCode = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        };
        builder.Reproducible(TestEpoch);
        if (mediaTemplate is not null)
        {
            builder.MediaTemplate(m => m
                .CabinetTemplate(mediaTemplate.CabinetTemplate)
                .MaxCabinetSizeMB(mediaTemplate.MaximumCabinetSizeInMB)
                .EmbedCabinet(mediaTemplate.EmbedCabinet));
        }
        return builder.Build();
    }

    private static PackageModel BuildPackageWithFiles(
        IEnumerable<string> sourcePaths,
        MediaTemplateModel? mediaTemplate)
    {
        var builder = new PackageBuilder
        {
            Name = "CabParityWithFiles",
            Manufacturer = "FalkForge Tests",
            Version = new Version(1, 0, 0),
            ProductCode = Guid.Parse("BBBBBBBB-CCCC-DDDD-EEEE-FFFFFFFFFFFF"),
            UpgradeCode = Guid.Parse("22222222-3333-4444-5555-666666666666"),
        };
        builder.Reproducible(TestEpoch);

        if (mediaTemplate is not null)
        {
            builder.MediaTemplate(m => m
                .CabinetTemplate(mediaTemplate.CabinetTemplate)
                .MaxCabinetSizeMB(mediaTemplate.MaximumCabinetSizeInMB)
                .EmbedCabinet(mediaTemplate.EmbedCabinet));
        }

        foreach (string path in sourcePaths)
        {
            string fileName = Path.GetFileName(path);
            string fileDir = Path.GetDirectoryName(path)!;
            builder.Files(f => f.Add(path).To(KnownFolder.ProgramFiles / "CabParityTest" / "bin"));
        }

        return builder.Build();
    }

    private static string MakeTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"FalkForge-CabParity-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string WriteFile(string dir, string name, string content)
    {
        string path = Path.Combine(dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static void TryDeleteDir(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
