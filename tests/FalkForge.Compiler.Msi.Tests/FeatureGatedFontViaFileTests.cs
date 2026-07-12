using System.Runtime.Versioning;
using FalkForge.Models;
using FalkForge.Platform.Windows;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests;

/// <summary>
/// Proves feature-gating for <c>FontModel</c>. Unlike Shortcut/Environment/IniFile/
/// FileAssociation, the real MSI <c>Font</c> table has exactly two columns — <c>File_</c> (a
/// foreign key into the File table) and <c>FontTitle</c> — no <c>Component_</c> and no
/// <c>Feature_</c>. A Font row is never itself an installable unit: it always describes a font
/// registered from an already-declared File, and that File's own component (already correctly
/// feature-gated by the established <c>FeatureBuilder.Files()</c> mechanism from the prior
/// service/registry branch) is what actually governs whether the font gets installed for a given
/// feature selection.
///
/// <c>FontModel.FeatureRef</c> is therefore metadata-only: <c>FeatureBuilder.Font(...)</c> stamps
/// it for API symmetry with the other five entry points and for round-tripping through the
/// decompiler, but no producer reads it — there is no honest MSI cell for it to drive. The real,
/// structurally-correct way to gate a font to a feature is to declare its source file inside the
/// same <c>FeatureBuilder</c> scope via <c>.Files(...)</c>, which this test proves end-to-end: the
/// font's <c>File_</c> resolves to a file whose component lands under the declaring feature and
/// not the sibling feature.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class FeatureGatedFontViaFileTests
{
    [Fact]
    public void Compile_FontFileDeclaredInFeature_FontFileComponentLandsUnderThatFeatureOnly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"MsiFeatFont_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var unrelatedFile = Path.Combine(tempDir, "unrelated.exe");
            File.WriteAllText(unrelatedFile, "fake exe unrelated to the font");

            var fontFile = Path.Combine(tempDir, "gatedfont.ttf");
            File.WriteAllText(fontFile, "fake font bytes");

            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(outputDir);

            var package = InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "FeatFontApp";
                p.Manufacturer = "TestCorp";
                p.Version = new Version(1, 0, 0);
                p.Feature("FeatureA", f =>
                    f.Files(fs => fs.Add(unrelatedFile).To(KnownFolder.ProgramFiles / "TestCorp" / "FeatFontApp")));
                p.Feature("FeatureB", f =>
                {
                    f.Files(fs => fs.Add(fontFile).To(KnownFolder.FontsFolder / "GatedFonts"));
                    f.Font("gatedfont.ttf", ft => ft.Title = "Gated Font Title");
                });
            });

            var compiler = new MsiCompiler(new WindowsFileSystem());
            var compileResult = compiler.Compile(package, outputDir);
            Assert.True(compileResult.IsSuccess,
                $"Compile failed: {(compileResult.IsFailure ? compileResult.Error.Message : "")}");

            using var db = MsiDatabase.Open(compileResult.Value, readOnly: true).Value;

            var fontRows = db.QueryRows(
                "SELECT `File_`, `FontTitle` FROM `Font` WHERE `FontTitle` = 'Gated Font Title'", 2).Value;
            var fileId = Assert.Single(fontRows)[0]!;

            var fileRows = db.QueryRows(
                $"SELECT `File`, `Component_` FROM `File` WHERE `File` = '{fileId}'", 2).Value;
            var componentId = Assert.Single(fileRows)[1]!;

            var featureComponentRows = db.QueryRows(
                "SELECT `Feature_`, `Component_` FROM `FeatureComponents`", 2).Value;

            Assert.Contains(featureComponentRows, r => r[1] == componentId && r[0] == "FeatureB");
            Assert.DoesNotContain(featureComponentRows, r => r[1] == componentId && r[0] == "FeatureA");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
