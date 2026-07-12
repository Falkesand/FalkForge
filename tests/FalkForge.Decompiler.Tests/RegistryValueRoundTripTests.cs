using System.Runtime.Versioning;
using FalkForge.Compiler.Msi;
using FalkForge.Models;
using FalkForge.Platform.Windows;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Decompiler.Tests;

/// <summary>
/// Round-trip tests for typed registry values: compile a real MSI whose Registry
/// table carries each Windows Installer type-prefix encoding (#, #x, #%, [~], ##),
/// then decompile it back with <see cref="MsiDecompiler.Decompile"/> and assert the
/// reconstructed <see cref="RegistryEntryModel"/> recovers the original
/// <see cref="RegistryEntryModel.ValueType"/> and value.
///
/// The decompiler's <c>ParseRegistryValue</c> must be the exact inverse of
/// <c>RegistryTableProducer.EncodeValue</c>. Before this fix a REG_BINARY (<c>#x</c>)
/// value was mis-read as REG_DWORD, and a single-element REG_MULTI_SZ (encoded
/// <c>[~]value[~]</c>) was mis-read as a plain string — decompile→recompile silently
/// corrupted the value type. An in-memory model assertion cannot catch this; only a
/// real compiled MSI read back through the reconstructor reveals it.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class RegistryValueRoundTripTests
{
    private static PackageModel Roundtrip()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"MsiRegRT_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourceFile = Path.Combine(tempDir, "app.exe");
            File.WriteAllText(sourceFile, "fake exe for registry round-trip test");

            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(outputDir);

            var package = InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "RegRoundTripApp";
                p.Manufacturer = "TestCorp";
                p.Version = new Version(1, 0, 0);
                p.Feature("Main", f =>
                {
                    f.Files(fs => fs.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / "RegRoundTripApp"));
                });
                p.Registry(r => r.Key(RegistryRoot.LocalMachine, @"Software\TestCorp\RegRoundTripApp", k =>
                {
                    k.DWord("Count", 5);
                    k.Binary("Blob", [0x0A, 0xFF, 0x00, 0x7F]);
                    k.MultiString("List", ["alpha", "beta"]);
                    k.MultiString("Single", ["solo"]);
                    k.ExpandString("Root", "%SystemRoot%\\App");
                    k.Value("Hashy", "#literal");
                    k.Value("Plain", "just text");
                }));
            });

            var compiler = new MsiCompiler(new WindowsFileSystem());
            var compileResult = compiler.Compile(package, outputDir);
            Assert.True(compileResult.IsSuccess,
                $"Compile failed: {(compileResult.IsFailure ? compileResult.Error.Message : "")}");

            var decompiler = new MsiDecompiler();
            var decompileResult = decompiler.Decompile(compileResult.Value);
            Assert.True(decompileResult.IsSuccess,
                $"Decompile failed: {(decompileResult.IsFailure ? decompileResult.Error.Message : "")}");

            return decompileResult.Value;
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    private static RegistryEntryModel Entry(PackageModel model, string valueName) =>
        model.RegistryEntries.Single(e => e.ValueName == valueName);

    [Fact]
    public void Decompile_BinaryRegistryValue_RecoversBinaryTypeAndBytes()
    {
        var model = Roundtrip();

        var blob = Entry(model, "Blob");
        Assert.Equal(RegistryValueType.Binary, blob.ValueType);
        var bytes = Assert.IsType<byte[]>(blob.Value);
        Assert.Equal(new byte[] { 0x0A, 0xFF, 0x00, 0x7F }, bytes);
    }

    [Fact]
    public void Decompile_MultiElementMultiStringRegistryValue_RecoversList()
    {
        var model = Roundtrip();

        var list = Entry(model, "List");
        Assert.Equal(RegistryValueType.MultiString, list.ValueType);
        var values = Assert.IsAssignableFrom<IReadOnlyList<string>>(list.Value);
        Assert.Equal(["alpha", "beta"], values);
    }

    [Fact]
    public void Decompile_SingleElementMultiStringRegistryValue_RecoversSingletonList()
    {
        var model = Roundtrip();

        var single = Entry(model, "Single");
        Assert.Equal(RegistryValueType.MultiString, single.ValueType);
        var values = Assert.IsAssignableFrom<IReadOnlyList<string>>(single.Value);
        Assert.Equal(["solo"], values);
    }

    [Fact]
    public void Decompile_DWordRegistryValue_RecoversDWordType()
    {
        var model = Roundtrip();

        var count = Entry(model, "Count");
        Assert.Equal(RegistryValueType.DWord, count.ValueType);
        Assert.Equal(5, Assert.IsType<int>(count.Value));
    }

    [Fact]
    public void Decompile_ExpandStringRegistryValue_RecoversExpandStringType()
    {
        var model = Roundtrip();

        var root = Entry(model, "Root");
        Assert.Equal(RegistryValueType.ExpandString, root.ValueType);
        Assert.Equal("%SystemRoot%\\App", Assert.IsType<string>(root.Value));
    }

    [Fact]
    public void Decompile_StringStartingWithHash_RecoversUnescapedLiteralString()
    {
        var model = Roundtrip();

        var hashy = Entry(model, "Hashy");
        Assert.Equal(RegistryValueType.String, hashy.ValueType);
        Assert.Equal("#literal", Assert.IsType<string>(hashy.Value));
    }

    [Fact]
    public void Decompile_PlainStringRegistryValue_RecoversStringType()
    {
        var model = Roundtrip();

        var plain = Entry(model, "Plain");
        Assert.Equal(RegistryValueType.String, plain.ValueType);
        Assert.Equal("just text", Assert.IsType<string>(plain.Value));
    }
}
