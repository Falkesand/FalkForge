using System.Text;
using System.Text.Json;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests;

public sealed class DryRunSidecarWriterTests
{
    [Fact]
    public void WriteSidecar_ValidInputs_WritesFileAlongsideMsiOutputPath()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"DryRunSidecarTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var msiOutputPath = Path.Combine(tempDir, "Setup.msi");
            var actions = new (string Kind, string Description)[]
            {
                ("filewrite", "Writes app.exe to Program Files"),
                ("network", "Downloads .NET runtime")
            };
            var unsupported = new[] { "Firewall", "Iis" };

            var result = DryRunSidecarWriter.WriteSidecar(actions, unsupported, msiOutputPath);

            Assert.True(result.IsSuccess);
            var sidecarPath = msiOutputPath + ".dryrun.json";
            Assert.True(File.Exists(sidecarPath));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void WriteSidecar_EmittedDocument_MatchesActionsAndUnsupportedExtensions()
    {
        // WHY: the sidecar is the only durable record of what "forge plan" predicted --
        // pin that every action (kind + description) and unsupported-extension entry the
        // caller supplied actually lands in the emitted JSON, not just that a file exists.
        var tempDir = Path.Combine(Path.GetTempPath(), $"DryRunSidecarTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var msiOutputPath = Path.Combine(tempDir, "Setup.msi");
            var actions = new (string Kind, string Description)[]
            {
                ("filewrite", "MARKER_DESC_1"),
                ("registry", "MARKER_DESC_2")
            };
            var unsupported = new[] { "MARKER_EXT_A", "MARKER_EXT_B" };

            var result = DryRunSidecarWriter.WriteSidecar(actions, unsupported, msiOutputPath);

            Assert.True(result.IsSuccess);
            var json = File.ReadAllText(msiOutputPath + ".dryrun.json");
            var parsed = JsonSerializer.Deserialize(json, DryRunSidecarJsonContext.Default.DryRunSidecar);

            Assert.NotNull(parsed);
            Assert.Equal(2, parsed.DryRunActions.Length);
            Assert.Equal("filewrite", parsed.DryRunActions[0].Kind);
            Assert.Equal("MARKER_DESC_1", parsed.DryRunActions[0].Description);
            Assert.Equal("registry", parsed.DryRunActions[1].Kind);
            Assert.Equal("MARKER_DESC_2", parsed.DryRunActions[1].Description);
            Assert.Equal(unsupported, parsed.UnsupportedExtensions);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void WriteSidecar_EmptyActionsAndExtensions_WritesEmptyArrays()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"DryRunSidecarTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var msiOutputPath = Path.Combine(tempDir, "Setup.msi");

            var result = DryRunSidecarWriter.WriteSidecar([], [], msiOutputPath);

            Assert.True(result.IsSuccess);
            var json = File.ReadAllText(msiOutputPath + ".dryrun.json");
            var parsed = JsonSerializer.Deserialize(json, DryRunSidecarJsonContext.Default.DryRunSidecar);

            Assert.NotNull(parsed);
            Assert.Empty(parsed.DryRunActions);
            Assert.Empty(parsed.UnsupportedExtensions);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void WriteSidecar_WritesUtf8WithoutByteOrderMark()
    {
        // WHY: this codebase has been bitten by BOM-vs-no-BOM mismatches before (see
        // LESSONS.md) -- the sidecar is consumed by tooling that expects a bare UTF-8
        // stream, so a leading EF BB BF would silently break naive JSON parsers.
        var tempDir = Path.Combine(Path.GetTempPath(), $"DryRunSidecarTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var msiOutputPath = Path.Combine(tempDir, "Setup.msi");

            var result = DryRunSidecarWriter.WriteSidecar(
                [("filewrite", "desc")], ["Firewall"], msiOutputPath);

            Assert.True(result.IsSuccess);
            var bytes = File.ReadAllBytes(msiOutputPath + ".dryrun.json");
            var bom = Encoding.UTF8.GetPreamble();

            Assert.True(bytes.Length >= bom.Length);
            Assert.False(bytes.AsSpan(0, bom.Length).SequenceEqual(bom),
                "Sidecar must not start with a UTF-8 byte order mark.");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void WriteSidecar_UnwritableOutputPath_ReturnsIoErrorFailure()
    {
        // A directory that does not exist (and cannot be created implicitly by
        // File.WriteAllText) forces the underlying I/O to throw, exercising the
        // catch block's translation into a typed Result failure instead of an
        // unhandled exception escaping the compiler.
        var missingDir = Path.Combine(Path.GetTempPath(), $"DryRunSidecarTest_Missing_{Guid.NewGuid():N}", "nested");
        var msiOutputPath = Path.Combine(missingDir, "Setup.msi");

        var result = DryRunSidecarWriter.WriteSidecar(
            [("filewrite", "desc")], ["Firewall"], msiOutputPath);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.IoError, result.Error.Kind);
        Assert.Contains("DryRun sidecar write failed", result.Error.Message, StringComparison.Ordinal);
    }
}
