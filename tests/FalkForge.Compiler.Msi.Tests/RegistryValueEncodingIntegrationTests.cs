using System.Runtime.Versioning;
using FalkForge.Models;
using FalkForge.Platform.Windows;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests;

/// <summary>
/// Proves that <see cref="RegistryEntryModel.ValueType"/> actually drives the encoded
/// <c>Registry.Value</c> string in the compiled MSI, per the Windows Installer
/// "Registry Table" type-prefix convention (#, #x, #%, [~]). Before this fix
/// RegistryTableProducer wrote every value via a raw <c>ToString()</c>, so
/// <c>DWord("Name", 5)</c> silently installed as the REG_SZ string "5" instead of a
/// REG_DWORD — this test compiles a real MSI and reads the Registry table back to
/// guard against that regression (an in-memory model assertion would not have caught
/// it, since ValueType was set correctly on the model all along; only the compiled
/// bytes reveal the bug).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class RegistryValueEncodingIntegrationTests
{
    [Fact]
    public void Compile_TypedRegistryValues_EncodeWithCorrectMsiTypePrefix()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"MsiRegEnc_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourceFile = Path.Combine(tempDir, "app.exe");
            File.WriteAllText(sourceFile, "fake exe for registry value encoding test");

            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(outputDir);

            var package = InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "RegEncApp";
                p.Manufacturer = "TestCorp";
                p.Version = new Version(1, 0, 0);
                p.Feature("Main", f =>
                {
                    f.Files(fs => fs.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / "RegEncApp"));
                });
                p.Registry(r => r.Key(RegistryRoot.LocalMachine, @"Software\TestCorp\RegEncApp", k =>
                {
                    k.DWord("Count", 5);
                    k.Binary("Blob", [0x0A, 0xFF]);
                    k.MultiString("List", ["alpha", "beta"]);
                    k.ExpandString("Root", "%SystemRoot%\\App");
                    k.Value("Hashy", "#literal");
                }));
            });

            var fileSystem = new WindowsFileSystem();
            var compiler = new MsiCompiler(fileSystem);
            var compileResult = compiler.Compile(package, outputDir);
            Assert.True(compileResult.IsSuccess,
                $"Compile failed: {(compileResult.IsFailure ? compileResult.Error.Message : "")}");

            using var db = MsiDatabase.Open(compileResult.Value, readOnly: true).Value;
            var rows = db.QueryRows("SELECT `Name`, `Value` FROM `Registry`", 2).Value;
            var valuesByName = rows.ToDictionary(r => r[0]!, r => r[1]);

            Assert.Equal("#5", valuesByName["Count"]);
            Assert.Equal("#x0AFF", valuesByName["Blob"]);
            Assert.Equal("alpha[~]beta", valuesByName["List"]);
            Assert.Equal("#%%SystemRoot%\\App", valuesByName["Root"]);
            Assert.Equal("##literal", valuesByName["Hashy"]);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Compile_QWordRegistryValue_FailsLoudRatherThanMisencoding()
    {
        // WHY: the MSI Registry table has no native REG_QWORD encoding (only SZ,
        // EXPAND_SZ, MULTI_SZ, DWORD, and BINARY are representable). Silently
        // reinterpreting a QWord as a DWord (truncating to 32 bits) or as a plain
        // string would corrupt the installed value without any signal to the
        // package author. The producer must refuse to compile instead.
        var package = new PackageModel
        {
            Name = "RegQWordApp",
            Manufacturer = "TestCorp",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(),
            ProductCode = Guid.NewGuid(),
            RegistryEntries =
            [
                new RegistryEntryModel
                {
                    Root = RegistryRoot.LocalMachine,
                    Key = @"Software\TestCorp\RegQWordApp",
                    ValueName = "Big",
                    Value = 123456789012345L,
                    ValueType = RegistryValueType.QWord,
                },
            ],
        };

        var tempDir = Path.Combine(Path.GetTempPath(), $"MsiRegQWord_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var compiler = new MsiCompiler(new WindowsFileSystem());
            var compileResult = compiler.Compile(package, tempDir);

            Assert.True(compileResult.IsFailure);
            Assert.Contains("QWord", compileResult.Error.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
