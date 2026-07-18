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
}
