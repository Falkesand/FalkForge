using System.Runtime.Versioning;
using FalkForge.Compiler.Msi;
using FalkForge.Models;
using FalkForge.Platform.Windows;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Decompiler.Tests;

/// <summary>
/// Round-trip test for the File-table Vital flag: compile a real MSI with one vital
/// (default) and one non-vital file, then decompile it back and assert the
/// reconstructed <see cref="FileEntryModel.Vital"/> flags match.
///
/// The compiler encodes msidbFileAttributesVital (512) into File.Attributes only when
/// the file is vital (bit SET = vital, bit CLEAR = non-vital). Before this fix the
/// decompiler never read the Attributes column back, so every decompiled file was
/// Vital = true regardless — a non-vital file silently became vital on decompile→recompile.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class FileVitalRoundTripTests
{
    [Fact]
    public void Decompile_NonVitalFile_RecoversVitalFalse_WhileDefaultStaysVital()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"MsiVitalRT_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var vitalSource = Path.Combine(tempDir, "vital.exe");
            File.WriteAllText(vitalSource, "fake exe for vital round-trip test");
            var optionalSource = Path.Combine(tempDir, "optional.dll");
            File.WriteAllText(optionalSource, "fake dll for vital round-trip test");

            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(outputDir);

            var package = InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "VitalRoundTripApp";
                p.Manufacturer = "TestCorp";
                p.Version = new Version(1, 0, 0);
                p.Feature("Main", f =>
                {
                    f.Files(fs => fs.Add(vitalSource).To(KnownFolder.ProgramFiles / "TestCorp" / "VitalRoundTripApp"));
                    f.Files(fs => fs.Add(optionalSource)
                        .To(KnownFolder.ProgramFiles / "TestCorp" / "VitalRoundTripApp")
                        .NotVital());
                });
            });

            var compiler = new MsiCompiler(new WindowsFileSystem());
            var compileResult = compiler.Compile(package, outputDir);
            Assert.True(compileResult.IsSuccess,
                $"Compile failed: {(compileResult.IsFailure ? compileResult.Error.Message : "")}");

            var decompiler = new MsiDecompiler();
            var decompileResult = decompiler.Decompile(compileResult.Value);
            Assert.True(decompileResult.IsSuccess,
                $"Decompile failed: {(decompileResult.IsFailure ? decompileResult.Error.Message : "")}");

            var model = decompileResult.Value;
            var vital = model.Files.Single(f => f.FileName == "vital.exe");
            var optional = model.Files.Single(f => f.FileName == "optional.dll");

            Assert.True(vital.Vital, "Default file must round-trip as Vital = true.");
            Assert.False(optional.Vital, "NotVital() file must round-trip as Vital = false.");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
