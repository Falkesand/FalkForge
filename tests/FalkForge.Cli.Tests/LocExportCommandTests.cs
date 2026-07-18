using System.Text.Json;
using FalkForge.Cli.Commands;
using FalkForge.Cli.Settings;
using Spectre.Console.Cli;
using Xunit;

namespace FalkForge.Cli.Tests;

/// <summary>
/// Tests for <c>forge loc export</c>. Exports the built-in localization JSON (baked into
/// FalkForge.Compiler.Msi as embedded resources) so users can start an override file without
/// cloning the FalkForge repo. Cross-platform: reads embedded resources only, no msi.dll needed.
/// </summary>
public sealed class LocExportCommandTests : IDisposable
{
    private readonly string _tempDir;

    public LocExportCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"falk_loc_export_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static CommandContext ExportContext() =>
        new([], new EmptyRemainingArguments(), "export", null);

    private static (int exitCode, TestConsoleOutput console) Run(LocExportSettings settings)
    {
        var console = new TestConsoleOutput();
        var command = new LocExportCommand(console);
        var exitCode = command.ExecuteSync(ExportContext(), settings, CancellationToken.None);
        return (exitCode, console);
    }

    [Fact]
    public void List_PrintsAvailableCultures_AndSucceeds()
    {
        var (exitCode, console) = Run(new LocExportSettings { List = true });

        Assert.Equal(ExitCodes.Success, exitCode);
        var allOutput = string.Join('\n', console.AllOutput);
        Assert.Contains("en-US", allOutput, StringComparison.Ordinal);
        Assert.Contains("sv-SE", allOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void List_DoesNotWriteAnyFiles()
    {
        var (exitCode, _) = Run(new LocExportSettings { List = true, Output = _tempDir });

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Empty(Directory.GetFiles(_tempDir));
    }

    [Fact]
    public void SingleCulture_WritesCultureJsonFileInOutputDirectory()
    {
        var (exitCode, _) = Run(new LocExportSettings { Culture = "en-US", Output = _tempDir });

        Assert.Equal(ExitCodes.Success, exitCode);
        var written = Path.Combine(_tempDir, "en-US.json");
        Assert.True(File.Exists(written));
    }

    [Fact]
    public void SingleCulture_ExportedContent_ParsesAndContainsExpectedKeys_GoldenContent()
    {
        // Golden-content check: the exported file must be the real built-in string pack, not a
        // stub — round-trips through JSON and matches known dialog-template keys/values.
        var (exitCode, _) = Run(new LocExportSettings { Culture = "en-US", Output = _tempDir });

        Assert.Equal(ExitCodes.Success, exitCode);
        var json = File.ReadAllText(Path.Combine(_tempDir, "en-US.json"));
        var strings = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;

        Assert.Equal("&Next >", strings["Button.Next"]);
        Assert.Equal("Welcome to [ProductName]", strings["Dialog.Welcome.Title"]);
    }

    [Fact]
    public void SingleCulture_SvSe_ExportedContent_HasSwedishStrings()
    {
        var (exitCode, _) = Run(new LocExportSettings { Culture = "sv-SE", Output = _tempDir });

        Assert.Equal(ExitCodes.Success, exitCode);
        var json = File.ReadAllText(Path.Combine(_tempDir, "sv-SE.json"));
        var strings = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;

        Assert.Equal("&Nästa >", strings["Button.Next"]);
    }

    [Fact]
    public void NoCulture_ExportsAllBuiltInCultures()
    {
        var (exitCode, _) = Run(new LocExportSettings { Output = _tempDir });

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.True(File.Exists(Path.Combine(_tempDir, "en-US.json")));
        Assert.True(File.Exists(Path.Combine(_tempDir, "sv-SE.json")));
    }

    [Fact]
    public void UnknownCulture_FailsLoud_WithAvailableCultureListInError()
    {
        var (exitCode, console) = Run(new LocExportSettings { Culture = "xx-XX", Output = _tempDir });

        Assert.Equal(ExitCodes.ValidationFailure, exitCode);
        var allErrors = string.Join('\n', console.Errors);
        Assert.Contains("xx-XX", allErrors, StringComparison.Ordinal);
        Assert.Contains("en-US", allErrors, StringComparison.Ordinal);
        Assert.Contains("sv-SE", allErrors, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownCulture_DoesNotWriteAnyFile()
    {
        Run(new LocExportSettings { Culture = "xx-XX", Output = _tempDir });

        Assert.Empty(Directory.GetFiles(_tempDir));
    }

    [Fact]
    public void SingleCulture_ExplicitJsonFilePath_WritesExactFile()
    {
        var explicitPath = Path.Combine(_tempDir, "custom-en-US.json");

        var (exitCode, _) = Run(new LocExportSettings { Culture = "en-US", Output = explicitPath });

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.True(File.Exists(explicitPath));
        // Must not also create the default-named file alongside it.
        Assert.False(File.Exists(Path.Combine(_tempDir, "en-US.json")));
    }

    [Fact]
    public void OutputDirectory_CreatedIfMissing()
    {
        var nested = Path.Combine(_tempDir, "nested", "dir");

        var (exitCode, _) = Run(new LocExportSettings { Culture = "en-US", Output = nested });

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.True(File.Exists(Path.Combine(nested, "en-US.json")));
    }

    [Fact]
    public void OutputPathCollidesWithExistingFile_FailsLoud_RuntimeError()
    {
        // Regression: the write step (Directory.CreateDirectory + File.WriteAllBytes) was
        // unguarded -- any IO failure (permission denied, disk full, path collision) threw a raw
        // exception out of Execute() instead of a clean exit code, unlike every sibling command
        // (InitCommand, MigrateCommand, PlanCommand all catch IOException/UnauthorizedAccessException
        // around their writes). Reproduce a reliable, portable failure: a *file* already occupies
        // the path we need to create as the output *directory* -- Directory.CreateDirectory throws
        // IOException on every platform for that collision, no ACL trickery needed.
        var collidingPath = Path.Combine(_tempDir, "blocked");
        File.WriteAllText(collidingPath, "not a directory");

        var (exitCode, console) = Run(new LocExportSettings { Culture = "en-US", Output = collidingPath });

        Assert.Equal(ExitCodes.RuntimeError, exitCode);
        Assert.NotEmpty(console.Errors);
    }

    [Fact]
    public void CultureMatch_IsCaseInsensitive_AndFileUsesCanonicalCasing()
    {
        // The library side (LocalizationBuilder's culture merge) is OrdinalIgnoreCase; the CLI's
        // own culture-name match must not be stricter than that. "EN-us" must resolve to the
        // built-in "en-US" pack, and the written file must use the canonical casing -- not the
        // user's typed casing -- so a directory export always produces predictable filenames.
        // NOTE: NTFS path lookups (File.Exists) are case-insensitive, so a wrong-casing check via
        // File.Exists would prove nothing -- assert on the actual on-disk name from a directory
        // listing instead, which preserves the casing used at creation time.
        var (exitCode, _) = Run(new LocExportSettings { Culture = "EN-us", Output = _tempDir });

        Assert.Equal(ExitCodes.Success, exitCode);
        var written = Assert.Single(Directory.GetFiles(_tempDir));
        Assert.Equal("en-US.json", Path.GetFileName(written), StringComparer.Ordinal);
    }

    [Fact]
    public void NoCulture_WithJsonSuffixedOutput_IsAmbiguous_FailsLoud()
    {
        // Exporting every built-in culture into a single named .json file is ambiguous -- which
        // culture's content goes there? Fail loud instead of silently picking one or creating a
        // directory literally named "foo.json".
        var jsonLikeOutput = Path.Combine(_tempDir, "foo.json");

        var (exitCode, console) = Run(new LocExportSettings { Output = jsonLikeOutput });

        Assert.Equal(ExitCodes.ValidationFailure, exitCode);
        Assert.NotEmpty(console.Errors);
        Assert.False(Directory.Exists(jsonLikeOutput));
    }
}
