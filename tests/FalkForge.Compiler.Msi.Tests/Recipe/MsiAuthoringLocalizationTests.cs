using System.Runtime.Versioning;
using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Diagnostics;
using FalkForge.Models;
using FalkForge.Platform.Windows;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe;

/// <summary>
/// End-to-end proof that <see cref="MsiAuthoring.Compile"/> surfaces the DLG005
/// multi-culture-localization-is-dropped warning through a real compile, not just
/// at the <see cref="Producers.DialogSetProducer"/> level (see
/// <c>DialogSetProducerTests</c> for the producer-only coverage). DLG005 exists
/// because <c>SetLocalizationData</c> only ever feeds the <c>!(loc.*)</c> resolver
/// for the first configured culture — additional cultures are silently dropped
/// unless a logger is attached to surface the warning.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MsiAuthoringLocalizationTests : IDisposable
{
    private readonly string _tempDir;

    public MsiAuthoringLocalizationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"MsiAuthoringLocalization_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); }
            catch (IOException) { }
        }
    }

    private string CreateSourceFile()
    {
        string sourceDir = Path.Combine(_tempDir, "src");
        Directory.CreateDirectory(sourceDir);
        string sourceFile = Path.Combine(sourceDir, "app.exe");
        File.WriteAllText(sourceFile, "fake exe content");
        return sourceFile;
    }

    [Fact]
    public void Compile_with_multiple_localization_cultures_logs_DLG005_warning()
    {
        // Two cultures configured but only the first is ever applied to the base MSI — the
        // second is silently dropped unless DLG005 surfaces. This proves the drop is visible
        // through a real MsiAuthoring.Compile call, not only at the producer level.
        string sourceFile = CreateSourceFile();
        string outputDir = Path.Combine(_tempDir, "out");
        Directory.CreateDirectory(outputDir);

        PackageModel package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "LocalizedApp";
            p.Manufacturer = "FalkForge";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "FalkForge" / "LocalizedApp"));
            p.UseDialogSet(MsiDialogSet.None);
            p.SetLocalizationData(
            [
                new LocalizationData { Culture = "en-US" },
                new LocalizationData { Culture = "de-DE" },
            ]);
        });

        var logger = new ListLogger();
        Result<string> result = MsiAuthoring.Compile(package, outputDir, [], logger);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);

        var warnings = logger.EntriesAt(LogLevel.Warning);
        var localizationWarning = Assert.Single(warnings, e => e.Message.Contains("MST"));
        Assert.NotNull(localizationWarning.Properties);
        Assert.Equal("DLG005", localizationWarning.Properties!["code"]);
        Assert.Contains("en-US", localizationWarning.Message);
    }

    [Fact]
    public void Compile_with_single_localization_culture_does_not_log_DLG005_warning()
    {
        string sourceFile = CreateSourceFile();
        string outputDir = Path.Combine(_tempDir, "out");
        Directory.CreateDirectory(outputDir);

        PackageModel package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "LocalizedApp";
            p.Manufacturer = "FalkForge";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "FalkForge" / "LocalizedApp"));
            p.UseDialogSet(MsiDialogSet.None);
            p.SetLocalizationData([new LocalizationData { Culture = "en-US" }]);
        });

        var logger = new ListLogger();
        Result<string> result = MsiAuthoring.Compile(package, outputDir, [], logger);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);

        Assert.DoesNotContain(logger.EntriesAt(LogLevel.Warning), e => e.Message.Contains("MST"));
    }
}
