using System.Collections.Generic;
using System.Runtime.Versioning;
using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Diagnostics;
using FalkForge.Models;
using FalkForge.Platform.Windows;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe;

/// <summary>
/// End-to-end proof that <see cref="MsiAuthoring.Compile"/> turns multi-culture
/// <c>SetLocalizationData</c> into real per-culture MSI language transforms (<c>.mst</c>).
///
/// <para>
/// The base MSI resolves <c>!(loc.*)</c> control text with the first configured culture.
/// For every additional culture the compiler rebuilds the UI localized to that culture and
/// emits an <c>&lt;msi-name&gt;.&lt;culture&gt;.mst</c> next to the MSI — the byte-difference
/// between the base database and the localized one. These tests compile a real MSI, then
/// <b>apply</b> the generated transform to a copy of the base and read the <c>Control</c>
/// table back through msi.dll to prove the transform actually carries the localized string.
/// Applying the transform at install time (so Windows Installer presents culture X) still
/// requires a real machine install and is covered by the end-to-end suites, not here.
/// </para>
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

    // A custom dialog carrying a single localizable control whose text is the !(loc.Greeting)
    // token. Keeping the localizable surface to one key means each culture only has to supply
    // "Greeting" — the stock templates would otherwise require every !(loc.*) key they reference.
    private PackageModel BuildLocalizedPackage(IReadOnlyList<LocalizationData> cultures)
    {
        string sourceFile = CreateSourceFile();
        return InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "LocalizedApp";
            p.Manufacturer = "FalkForge";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "FalkForge" / "LocalizedApp"));
            p.AddCustomDialog("GreetingDlg", dlg => dlg
                .Title("Welcome")
                .Sequence(1200)
                .FirstControl("Greeting")
                .Text("Greeting", 20, 20, 330, 40, "!(loc.Greeting)"));
            p.SetLocalizationData(cultures);
        });
    }

    private static LocalizationData Culture(string culture, string greeting)
        => new()
        {
            Culture = culture,
            Strings = new Dictionary<string, string> { ["Greeting"] = greeting },
        };

    [Fact]
    public void Compile_with_two_cultures_generates_a_transform_that_applies_the_localized_string()
    {
        // en-US (base) resolves Greeting → "Hello"; de-DE must produce a .mst that, applied to
        // the base, changes the control text to "Hallo".
        string outputDir = Path.Combine(_tempDir, "out");
        Directory.CreateDirectory(outputDir);

        PackageModel package = BuildLocalizedPackage(
        [
            Culture("en-US", "Hello"),
            Culture("de-DE", "Hallo"),
        ]);

        Result<string> result = MsiAuthoring.Compile(package, outputDir);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        string msiPath = result.Value;

        // Exactly one language transform is produced (for the single non-default culture).
        string[] transforms = Directory.GetFiles(outputDir, "*.mst");
        string mstPath = Assert.Single(transforms);
        Assert.Contains("de-DE", Path.GetFileName(mstPath));
        Assert.True(new FileInfo(mstPath).Length > 0, "the generated .mst must not be empty");

        // The base MSI carries the default-culture string.
        using (MsiDatabase baseDb = MsiDatabase.Open(msiPath, readOnly: true).Value)
        {
            Result<List<string?[]>> baseText = baseDb.QueryRows(
                "SELECT `Text` FROM `Control` WHERE `Dialog_` = 'GreetingDlg' AND `Control` = 'Greeting'",
                fieldCount: 1);
            Assert.True(baseText.IsSuccess);
            Assert.Equal("Hello", Assert.Single(baseText.Value)[0]);
        }

        // Applying the de-DE transform to a writable copy of the base yields the localized string —
        // proof the .mst actually carries the culture's Control text, not just that a file exists.
        string copyPath = Path.Combine(_tempDir, "applied.msi");
        File.Copy(msiPath, copyPath);
        using MsiDatabase copyDb = MsiDatabase.Open(copyPath, readOnly: false).Value;
        Result<Unit> apply = copyDb.ApplyTransform(mstPath);
        Assert.True(apply.IsSuccess, apply.IsFailure ? apply.Error.Message : null);

        Result<List<string?[]>> localizedText = copyDb.QueryRows(
            "SELECT `Text` FROM `Control` WHERE `Dialog_` = 'GreetingDlg' AND `Control` = 'Greeting'",
            fieldCount: 1);
        Assert.True(localizedText.IsSuccess);
        Assert.Equal("Hallo", Assert.Single(localizedText.Value)[0]);
    }

    [Fact]
    public void Compile_with_two_cultures_localizes_dialog_title_and_generates_a_transform_that_applies_it()
    {
        // Dialog.Title is a separate localizable MSI column from Control.Text. This proves title
        // resolution + per-culture MST override work exactly like Control.Text already does.
        string sourceFile = CreateSourceFile();
        string outputDir = Path.Combine(_tempDir, "out");
        Directory.CreateDirectory(outputDir);

        PackageModel package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "LocalizedApp";
            p.Manufacturer = "FalkForge";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "FalkForge" / "LocalizedApp"));
            p.AddCustomDialog("TitledDlg", dlg => dlg
                .Title("!(loc.DialogTitle)")
                .Sequence(1200)
                .FirstControl("Static")
                .Text("Static", 20, 20, 330, 40, "Just text"));
            p.SetLocalizationData(
            [
                new LocalizationData
                {
                    Culture = "en-US",
                    Strings = new Dictionary<string, string> { ["DialogTitle"] = "Hello Title" },
                },
                new LocalizationData
                {
                    Culture = "de-DE",
                    Strings = new Dictionary<string, string> { ["DialogTitle"] = "Hallo Titel" },
                },
            ]);
        });

        Result<string> result = MsiAuthoring.Compile(package, outputDir);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        string msiPath = result.Value;

        string[] transforms = Directory.GetFiles(outputDir, "*.mst");
        string mstPath = Assert.Single(transforms);

        // The base MSI carries the primary-culture title.
        using (MsiDatabase baseDb = MsiDatabase.Open(msiPath, readOnly: true).Value)
        {
            Result<List<string?[]>> baseTitle = baseDb.QueryRows(
                "SELECT `Title` FROM `Dialog` WHERE `Dialog` = 'TitledDlg'",
                fieldCount: 1);
            Assert.True(baseTitle.IsSuccess);
            Assert.Equal("Hello Title", Assert.Single(baseTitle.Value)[0]);
        }

        // Applying the de-DE transform yields the localized title — proof the .mst actually
        // carries the Dialog.Title column, not just Control.Text.
        string copyPath = Path.Combine(_tempDir, "applied_title.msi");
        File.Copy(msiPath, copyPath);
        using MsiDatabase copyDb = MsiDatabase.Open(copyPath, readOnly: false).Value;
        Result<Unit> apply = copyDb.ApplyTransform(mstPath);
        Assert.True(apply.IsSuccess, apply.IsFailure ? apply.Error.Message : null);

        Result<List<string?[]>> localizedTitle = copyDb.QueryRows(
            "SELECT `Title` FROM `Dialog` WHERE `Dialog` = 'TitledDlg'",
            fieldCount: 1);
        Assert.True(localizedTitle.IsSuccess);
        Assert.Equal("Hallo Titel", Assert.Single(localizedTitle.Value)[0]);
    }

    [Fact]
    public void Compile_with_two_cultures_localizes_UIText_entry_and_generates_a_transform_that_applies_it()
    {
        // The 21 fixed UIText rows (bytes, GB, MenuAbsent, ...) previously passed through as
        // hardcoded English literals with no !(loc.*) indirection. Authors can now override any
        // entry via a "UiText.<Key>" string in their own LocalizationData, and — like Control.Text
        // and Title — it participates in the per-culture MST rebuild.
        string sourceFile = CreateSourceFile();
        string outputDir = Path.Combine(_tempDir, "out");
        Directory.CreateDirectory(outputDir);

        PackageModel package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "LocalizedApp";
            p.Manufacturer = "FalkForge";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "FalkForge" / "LocalizedApp"));
            p.AddCustomDialog("PlainDlg", dlg => dlg
                .Title("Plain")
                .Sequence(1200)
                .FirstControl("Static")
                .Text("Static", 20, 20, 330, 40, "Just text"));
            p.SetLocalizationData(
            [
                new LocalizationData
                {
                    Culture = "en-US",
                    Strings = new Dictionary<string, string> { ["UiText.bytes"] = "Bytes!" },
                },
                new LocalizationData
                {
                    Culture = "de-DE",
                    Strings = new Dictionary<string, string> { ["UiText.bytes"] = "Byte" },
                },
            ]);
        });

        Result<string> result = MsiAuthoring.Compile(package, outputDir);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        string msiPath = result.Value;

        string[] transforms = Directory.GetFiles(outputDir, "*.mst");
        string mstPath = Assert.Single(transforms);

        using (MsiDatabase baseDb = MsiDatabase.Open(msiPath, readOnly: true).Value)
        {
            Result<List<string?[]>> baseText = baseDb.QueryRows(
                "SELECT `Text` FROM `UIText` WHERE `Key` = 'bytes'",
                fieldCount: 1);
            Assert.True(baseText.IsSuccess);
            Assert.Equal("Bytes!", Assert.Single(baseText.Value)[0]);
        }

        string copyPath = Path.Combine(_tempDir, "applied_uitext.msi");
        File.Copy(msiPath, copyPath);
        using MsiDatabase copyDb = MsiDatabase.Open(copyPath, readOnly: false).Value;
        Result<Unit> apply = copyDb.ApplyTransform(mstPath);
        Assert.True(apply.IsSuccess, apply.IsFailure ? apply.Error.Message : null);

        Result<List<string?[]>> localizedText = copyDb.QueryRows(
            "SELECT `Text` FROM `UIText` WHERE `Key` = 'bytes'",
            fieldCount: 1);
        Assert.True(localizedText.IsSuccess);
        Assert.Equal("Byte", Assert.Single(localizedText.Value)[0]);
    }

    [Fact]
    public void Compile_with_partially_translated_culture_falls_back_to_primary_for_untranslated_title()
    {
        // Mirrors the Control.Text partial-fallback proof above, for Dialog.Title: a culture that
        // does not translate the title must still build (no LOC003) and fall back to the primary
        // culture's title, while a control it DOES translate proves the transform isn't a no-op.
        string sourceFile = CreateSourceFile();
        string outputDir = Path.Combine(_tempDir, "out");
        Directory.CreateDirectory(outputDir);

        PackageModel package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "LocalizedApp";
            p.Manufacturer = "FalkForge";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "FalkForge" / "LocalizedApp"));
            p.AddCustomDialog("TitledDlg", dlg => dlg
                .Title("!(loc.DialogTitle)")
                .Sequence(1200)
                .FirstControl("Other")
                .Text("Other", 20, 20, 330, 40, "!(loc.Other)"));
            p.SetLocalizationData(
            [
                new LocalizationData
                {
                    Culture = "en-US",
                    Strings = new Dictionary<string, string>
                    {
                        ["DialogTitle"] = "Hello Title",
                        ["Other"] = "X",
                    },
                },
                // Partial translation: 'de' localizes only Other, not DialogTitle.
                new LocalizationData
                {
                    Culture = "de",
                    Strings = new Dictionary<string, string> { ["Other"] = "Y" },
                },
            ]);
        });

        Result<string> result = MsiAuthoring.Compile(package, outputDir);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        string msiPath = result.Value;

        string[] transforms = Directory.GetFiles(outputDir, "*.mst");
        string mstPath = Assert.Single(transforms);

        string copyPath = Path.Combine(_tempDir, "applied_partial_title.msi");
        File.Copy(msiPath, copyPath);
        using MsiDatabase copyDb = MsiDatabase.Open(copyPath, readOnly: false).Value;
        Result<Unit> apply = copyDb.ApplyTransform(mstPath);
        Assert.True(apply.IsSuccess, apply.IsFailure ? apply.Error.Message : null);

        // Title (untranslated by 'de') falls back to the primary en-US value...
        Result<List<string?[]>> title = copyDb.QueryRows(
            "SELECT `Title` FROM `Dialog` WHERE `Dialog` = 'TitledDlg'",
            fieldCount: 1);
        Assert.True(title.IsSuccess);
        Assert.Equal("Hello Title", Assert.Single(title.Value)[0]);

        // ...while Other IS localized to the German value, proving the transform isn't a no-op.
        Result<List<string?[]>> other = copyDb.QueryRows(
            "SELECT `Text` FROM `Control` WHERE `Dialog_` = 'TitledDlg' AND `Control` = 'Other'",
            fieldCount: 1);
        Assert.True(other.IsSuccess);
        Assert.Equal("Y", Assert.Single(other.Value)[0]);
    }

    [Fact]
    public void Compile_with_partially_translated_culture_falls_back_to_primary_for_untranslated_strings()
    {
        // A language transform overrides only the strings a culture localizes; anything the extra
        // culture leaves untranslated must keep the primary (default) culture's value — exactly how
        // MSI language transforms behave. So a PARTIAL translation must build successfully, and the
        // .mst must override ONLY the strings the extra culture actually redefines.
        //
        // Primary en-US defines both Welcome.Title and Other; German ('de') translates ONLY Other
        // and deliberately omits Welcome.Title. Before the fallback fix this hard-failed with LOC003
        // ("String ID 'Welcome.Title' not found in any culture") because the per-culture rebuild
        // resolved 'de' with no fallback to en-US.
        string sourceFile = CreateSourceFile();
        string outputDir = Path.Combine(_tempDir, "out");
        Directory.CreateDirectory(outputDir);

        PackageModel package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "LocalizedApp";
            p.Manufacturer = "FalkForge";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "FalkForge" / "LocalizedApp"));
            p.AddCustomDialog("GreetingDlg", dlg => dlg
                .Title("Welcome")
                .Sequence(1200)
                .FirstControl("Welcome")
                .Text("Welcome", 20, 20, 330, 40, "!(loc.Welcome.Title)")
                .Text("Other", 20, 70, 330, 40, "!(loc.Other)"));
            p.SetLocalizationData(
            [
                new LocalizationData
                {
                    Culture = "en-US",
                    Strings = new Dictionary<string, string>
                    {
                        ["Welcome.Title"] = "Hello",
                        ["Other"] = "X",
                    },
                },
                // Partial translation: 'de' localizes only Other, not Welcome.Title.
                new LocalizationData
                {
                    Culture = "de",
                    Strings = new Dictionary<string, string> { ["Other"] = "Y" },
                },
            ]);
        });

        Result<string> result = MsiAuthoring.Compile(package, outputDir);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        string msiPath = result.Value;

        // The partial culture still yields a transform (Other differs from the base).
        string[] transforms = Directory.GetFiles(outputDir, "*.mst");
        string mstPath = Assert.Single(transforms);
        Assert.Contains("de", Path.GetFileName(mstPath));

        // Apply the de transform to a writable copy of the base and read the Control table back.
        string copyPath = Path.Combine(_tempDir, "applied.msi");
        File.Copy(msiPath, copyPath);
        using MsiDatabase copyDb = MsiDatabase.Open(copyPath, readOnly: false).Value;
        Result<Unit> apply = copyDb.ApplyTransform(mstPath);
        Assert.True(apply.IsSuccess, apply.IsFailure ? apply.Error.Message : null);

        // Other is localized to the German value...
        Result<List<string?[]>> other = copyDb.QueryRows(
            "SELECT `Text` FROM `Control` WHERE `Dialog_` = 'GreetingDlg' AND `Control` = 'Other'",
            fieldCount: 1);
        Assert.True(other.IsSuccess);
        Assert.Equal("Y", Assert.Single(other.Value)[0]);

        // ...while the untranslated Welcome.Title falls back to the primary en-US value.
        Result<List<string?[]>> welcome = copyDb.QueryRows(
            "SELECT `Text` FROM `Control` WHERE `Dialog_` = 'GreetingDlg' AND `Control` = 'Welcome'",
            fieldCount: 1);
        Assert.True(welcome.IsSuccess);
        Assert.Equal("Hello", Assert.Single(welcome.Value)[0]);
    }

    [Fact]
    public void Compile_with_reference_undefined_in_every_culture_still_fails_LOC003()
    {
        // Fallback is to the PRIMARY culture, not silence: a !(loc.*) token defined in NO culture
        // (including the primary) is a genuine authoring bug and must still fail LOC003.
        string sourceFile = CreateSourceFile();
        string outputDir = Path.Combine(_tempDir, "out");
        Directory.CreateDirectory(outputDir);

        PackageModel package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "LocalizedApp";
            p.Manufacturer = "FalkForge";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "FalkForge" / "LocalizedApp"));
            p.AddCustomDialog("GreetingDlg", dlg => dlg
                .Title("Welcome")
                .Sequence(1200)
                .FirstControl("Greeting")
                .Text("Greeting", 20, 20, 330, 40, "!(loc.Nowhere)"));
            p.SetLocalizationData(
            [
                new LocalizationData
                {
                    Culture = "en-US",
                    Strings = new Dictionary<string, string> { ["Greeting"] = "Hello" },
                },
                new LocalizationData
                {
                    Culture = "de",
                    Strings = new Dictionary<string, string> { ["Greeting"] = "Hallo" },
                },
            ]);
        });

        Result<string> result = MsiAuthoring.Compile(package, outputDir);
        Assert.True(result.IsFailure);
        Assert.Contains("LOC003", result.Error.Message);
    }

    [Fact]
    public void Compile_with_three_cultures_generates_one_transform_per_additional_culture()
    {
        string outputDir = Path.Combine(_tempDir, "out");
        Directory.CreateDirectory(outputDir);

        PackageModel package = BuildLocalizedPackage(
        [
            Culture("en-US", "Hello"),
            Culture("de-DE", "Hallo"),
            Culture("fr-FR", "Bonjour"),
        ]);

        Result<string> result = MsiAuthoring.Compile(package, outputDir);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);

        string[] transforms = Directory.GetFiles(outputDir, "*.mst");
        Assert.Equal(2, transforms.Length);
        Assert.Contains(transforms, t => Path.GetFileName(t).Contains("de-DE"));
        Assert.Contains(transforms, t => Path.GetFileName(t).Contains("fr-FR"));
    }

    [Fact]
    public void Compile_with_single_culture_generates_no_transform_and_no_DLG005()
    {
        string outputDir = Path.Combine(_tempDir, "out");
        Directory.CreateDirectory(outputDir);

        PackageModel package = BuildLocalizedPackage([Culture("en-US", "Hello")]);

        var logger = new ListLogger();
        Result<string> result = MsiAuthoring.Compile(package, outputDir, [], logger);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);

        Assert.Empty(Directory.GetFiles(outputDir, "*.mst"));
        Assert.DoesNotContain(logger.EntriesAt(LogLevel.Warning), e => e.Message.Contains("localiz"));
    }

    [Fact]
    public void Compile_with_multiple_cultures_but_no_localizable_content_warns_DLG005_and_writes_no_mst()
    {
        // No dialog / no !(loc.*) surface: the cultures differ in name only, so every culture
        // produces an identical database and no transform is written. The author is warned
        // (DLG005) rather than silently left with a single-language installer.
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

        Assert.Empty(Directory.GetFiles(outputDir, "*.mst"));
        var warning = Assert.Single(logger.EntriesAt(LogLevel.Warning), e => e.Message.Contains("de-DE"));
        Assert.NotNull(warning.Properties);
        Assert.Equal("DLG005", warning.Properties!["code"]);
    }
}
