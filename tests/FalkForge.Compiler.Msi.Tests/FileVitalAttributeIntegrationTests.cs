using System.Globalization;
using System.Runtime.Versioning;
using FalkForge.Platform.Windows;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests;

/// <summary>
/// Proves that <see cref="FalkForge.Models.FileEntryModel.Vital"/> actually drives the
/// compiled <c>File.Attributes</c> column instead of being silently ignored.
/// Before this fix <c>FileTableProducer</c> hardcoded every row to
/// <c>msidbFileAttributesVital</c> (512), so a package author had no way to mark a file
/// non-vital (letting a copy failure be skipped instead of aborting the install) — the
/// knob existed on the model but no fluent surface reached it, and the compiler ignored
/// it even when set directly. This compiles a real MSI and reads the File table back,
/// since an in-memory model assertion would not catch the compiler discarding the flag.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class FileVitalAttributeIntegrationTests
{
    private const int MsidbFileAttributesVital = 512;

    [Fact]
    public void Compile_NotVitalFile_ClearsVitalBit_WhileDefaultFileStaysVital()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"MsiVital_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var vitalSource = Path.Combine(tempDir, "vital.exe");
            File.WriteAllText(vitalSource, "fake exe for vital attribute test");
            var optionalSource = Path.Combine(tempDir, "optional.dll");
            File.WriteAllText(optionalSource, "fake dll for vital attribute test");

            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(outputDir);

            var package = InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "VitalAttrApp";
                p.Manufacturer = "TestCorp";
                p.Version = new Version(1, 0, 0);
                p.Feature("Main", f =>
                {
                    f.Files(fs => fs.Add(vitalSource).To(KnownFolder.ProgramFiles / "TestCorp" / "VitalAttrApp"));
                    f.Files(fs => fs.Add(optionalSource)
                        .To(KnownFolder.ProgramFiles / "TestCorp" / "VitalAttrApp")
                        .NotVital());
                });
            });

            var fileSystem = new WindowsFileSystem();
            var compiler = new MsiCompiler(fileSystem);
            var compileResult = compiler.Compile(package, outputDir);
            Assert.True(compileResult.IsSuccess,
                $"Compile failed: {(compileResult.IsFailure ? compileResult.Error.Message : "")}");

            using var db = MsiDatabase.Open(compileResult.Value, readOnly: true).Value;
            var rows = db.QueryRows("SELECT `FileName`, `Attributes` FROM `File`", 2).Value;
            var attributesByFileName = rows.ToDictionary(
                r => r[0]!,
                r => int.Parse(r[1]!, CultureInfo.InvariantCulture));

            var vitalAttributes = attributesByFileName["vital.exe"];
            var optionalAttributes = attributesByFileName["optional.dll"];

            Assert.True((vitalAttributes & MsidbFileAttributesVital) != 0,
                $"Default (vital) file must carry msidbFileAttributesVital (512); got {vitalAttributes}.");
            Assert.True((optionalAttributes & MsidbFileAttributesVital) == 0,
                $"NotVital() file must NOT carry msidbFileAttributesVital (512); got {optionalAttributes}.");

            // No other attribute bit is produced by this compiler today, so the exact values
            // pin the OR-in logic instead of just the single bit — a regression that flips
            // unrelated bits would also be caught.
            Assert.Equal(MsidbFileAttributesVital, vitalAttributes);
            Assert.Equal(0, optionalAttributes);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
