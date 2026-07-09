using FalkForge.Cli.Commands;
using FalkForge.Cli.Settings;
using Xunit;

namespace FalkForge.Cli.Tests.Diff;

/// <summary>
/// Unit tests for <see cref="PlanDiffCommand"/> settings validation and routing.
/// Command-level integration is covered at the settings level and via the CLI routing.
/// </summary>
public sealed class PlanDiffCommandTests
{
    // -------------------------------------------------------------------------
    // Settings validation
    // -------------------------------------------------------------------------
    [Fact]
    public void PlanDiffSettings_Validate_BothPaths_Success()
    {
        var s = new PlanDiffSettings { OldPath = "a.msi", NewPath = "b.msi" };
        Assert.True(s.Validate().Successful);
    }

    [Fact]
    public void PlanDiffSettings_Validate_OldPathEmpty_Error()
    {
        var s = new PlanDiffSettings { OldPath = "", NewPath = "b.msi" };
        Assert.False(s.Validate().Successful);
    }

    [Fact]
    public void PlanDiffSettings_Validate_NewPathEmpty_Error()
    {
        var s = new PlanDiffSettings { OldPath = "a.msi", NewPath = "" };
        Assert.False(s.Validate().Successful);
    }

    [Fact]
    public void PlanDiffSettings_Validate_MarkdownAndJson_Error()
    {
        var s = new PlanDiffSettings { OldPath = "a.msi", NewPath = "b.msi", Markdown = true, Json = true };
        Assert.False(s.Validate().Successful);
    }

    [Fact]
    public void PlanDiffSettings_Validate_MarkdownAlone_Success()
    {
        var s = new PlanDiffSettings { OldPath = "a.msi", NewPath = "b.msi", Markdown = true };
        Assert.True(s.Validate().Successful);
    }

    [Fact]
    public void PlanDiffSettings_Validate_JsonAlone_Success()
    {
        var s = new PlanDiffSettings { OldPath = "a.msi", NewPath = "b.msi", Json = true };
        Assert.True(s.Validate().Successful);
    }

    // -------------------------------------------------------------------------
    // Missing file guard
    // -------------------------------------------------------------------------
    [Fact]
    public void Execute_OldFileNotFound_ReturnsRuntimeError()
    {
        var output = new TestConsoleOutput();
        var cmd = new PlanDiffCommand(output);
        var settings = new PlanDiffSettings
        {
            OldPath = @"C:\nonexistent\does_not_exist_old.msi",
            NewPath = @"C:\nonexistent\does_not_exist_new.msi",
        };

        var code = cmd.ExecuteSync(default!, settings, CancellationToken.None);

        Assert.Equal(ExitCodes.RuntimeError, code);
        Assert.Contains(output.Errors, e => e.Contains("not found"));
    }

    // -------------------------------------------------------------------------
    // Extension mismatch guard
    // -------------------------------------------------------------------------
    [Fact]
    public void Execute_MismatchedExtensions_ReturnsRuntimeError()
    {
        // Create two real temp files with different extensions
        var oldFile = Path.GetTempFileName();
        var exeFile = Path.ChangeExtension(Path.GetTempFileName(), ".exe");
        var msiFile = Path.ChangeExtension(oldFile, ".msi");

        try
        {
            System.IO.File.Move(oldFile, msiFile, overwrite: true);
            System.IO.File.WriteAllText(exeFile, "fake");

            var output = new TestConsoleOutput();
            var cmd = new PlanDiffCommand(output);
            var settings = new PlanDiffSettings { OldPath = msiFile, NewPath = exeFile };

            var code = cmd.ExecuteSync(default!, settings, CancellationToken.None);

            Assert.Equal(ExitCodes.RuntimeError, code);
            Assert.Contains(output.Errors, e => e.Contains("differ"));
        }
        finally
        {
            if (System.IO.File.Exists(msiFile)) System.IO.File.Delete(msiFile);
            if (System.IO.File.Exists(exeFile)) System.IO.File.Delete(exeFile);
        }
    }

    // -------------------------------------------------------------------------
    // Unknown extension guard
    // -------------------------------------------------------------------------
    [Fact]
    public void Execute_UnknownExtension_ReturnsRuntimeError()
    {
        var file1 = Path.ChangeExtension(Path.GetTempFileName(), ".zip");
        var file2 = Path.ChangeExtension(Path.GetTempFileName(), ".zip");

        try
        {
            System.IO.File.WriteAllText(file1, "fake");
            System.IO.File.WriteAllText(file2, "fake");

            var output = new TestConsoleOutput();
            var cmd = new PlanDiffCommand(output);
            var settings = new PlanDiffSettings { OldPath = file1, NewPath = file2 };

            var code = cmd.ExecuteSync(default!, settings, CancellationToken.None);

            Assert.Equal(ExitCodes.RuntimeError, code);
            Assert.Contains(output.Errors, e => e.Contains("Unsupported"));
        }
        finally
        {
            if (System.IO.File.Exists(file1)) System.IO.File.Delete(file1);
            if (System.IO.File.Exists(file2)) System.IO.File.Delete(file2);
        }
    }

    // -------------------------------------------------------------------------
    // Exit code semantics: diff-found is NOT an error (exit 0)
    // -------------------------------------------------------------------------
    [Fact]
    public void Execute_InvalidBundleContent_ReturnsRuntimeError_NotSuccessEvenIfDiffFound()
    {
        // Passes the extension check (.exe/.exe) but the content is not a valid bundle.
        var file1 = Path.ChangeExtension(Path.GetTempFileName(), ".exe");
        var file2 = Path.ChangeExtension(Path.GetTempFileName(), ".exe");

        try
        {
            System.IO.File.WriteAllBytes(file1, [0x4D, 0x5A]); // MZ header, not a FalkForge bundle
            System.IO.File.WriteAllBytes(file2, [0x4D, 0x5A]);

            var output = new TestConsoleOutput();
            var cmd = new PlanDiffCommand(output);
            var settings = new PlanDiffSettings { OldPath = file1, NewPath = file2 };

            var code = cmd.ExecuteSync(default!, settings, CancellationToken.None);

            // Content is not a valid bundle — expect RuntimeError, not Success.
            Assert.Equal(ExitCodes.RuntimeError, code);
        }
        finally
        {
            if (System.IO.File.Exists(file1)) System.IO.File.Delete(file1);
            if (System.IO.File.Exists(file2)) System.IO.File.Delete(file2);
        }
    }
}
