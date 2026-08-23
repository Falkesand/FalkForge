namespace FalkForge.Platform.Windows.Tests;

using System.Runtime.Versioning;
using System.Text;
using FalkForge.Builders;
using FalkForge.Compiler.Msi;
using FalkForge.Platform.Windows;
using FalkForge.Testing;
using Xunit;

/// <summary>
/// Proves the ported <see cref="MsiTransformGenerator"/> writes a transform that actually SETS the secret
/// property: it compiles a real MSI, generates the transform, applies it to a fresh copy through the
/// compiler's <see cref="MsiDatabase"/>, and reads the value back from the Property table — never merely
/// asserting the .mst file is non-empty. A special-character password (every shell metacharacter the
/// command-line path would reject) round-trips, which is the whole reason the transform exists.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MsiTransformGeneratorTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), $"MsiTransformGenTest_{Guid.NewGuid():N}");

    public MsiTransformGeneratorTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose() => TestTemp.TryDelete(_tempDir);

    private string CompileBaseMsi()
    {
        var sourceDir = Path.Combine(_tempDir, "src");
        Directory.CreateDirectory(sourceDir);
        var sourceFile = Path.Combine(sourceDir, "app.exe");
        File.WriteAllText(sourceFile, "payload");

        var outputDir = Path.Combine(_tempDir, "out");
        Directory.CreateDirectory(outputDir);

        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "SecretApp";
            p.Manufacturer = "TestCorp";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / "SecretApp"));
        });

        var result = new MsiCompiler().Compile(package, outputDir);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        return result.Value;
    }

    [Fact]
    public void GenerateSecretTransform_SpecialCharacterPassword_SetsPropertyReadableAfterApply()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        var baseMsi = CompileBaseMsi();
        var staging = Path.Combine(_tempDir, "staging");
        Directory.CreateDirectory(staging);

        // Every character the command-line ProhibitedValueChars set rejects, plus spaces.
        const string password = "P@ss \" & | ; < > w0rd!";
        using var secret = SensitiveBytes.FromPlaintext(Encoding.UTF8.GetBytes(password));
        var secrets = new Dictionary<string, SensitiveBytes>(StringComparer.OrdinalIgnoreCase)
        {
            ["SQLPASSWORD"] = secret
        };

        var result = MsiTransformGenerator.GenerateSecretTransform(baseMsi, secrets, staging);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var mstPath = result.Value;
        Assert.True(File.Exists(mstPath), $"MST not found at: {mstPath}");

        // The working copy that carried the secret must be gone; only the transform remains.
        Assert.Empty(Directory.GetFiles(staging, "~pw-*.msi"));

        // Apply the transform to a fresh copy of the base and read the property back.
        var applied = Path.Combine(_tempDir, "applied.msi");
        File.Copy(baseMsi, applied);
        using var db = MsiDatabase.Open(applied, readOnly: false).Value;
        var applyResult = db.ApplyTransform(mstPath);
        Assert.True(applyResult.IsSuccess, applyResult.IsFailure ? applyResult.Error.Message : null);

        var rows = db.QueryRows(
            "SELECT `Value` FROM `Property` WHERE `Property` = 'SQLPASSWORD'", 1);
        Assert.True(rows.IsSuccess, rows.IsFailure ? rows.Error.Message : null);
        Assert.Equal(password, Assert.Single(rows.Value)[0]);
    }

    [Fact]
    public void GenerateSecretTransform_UpdatesAnExistingProperty()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        var baseMsi = CompileBaseMsi();
        var staging = Path.Combine(_tempDir, "staging2");
        Directory.CreateDirectory(staging);

        // Manufacturer maps to the Manufacturer property, which already exists in the base MSI — this
        // exercises the UPDATE branch rather than INSERT.
        using var secret = SensitiveBytes.FromPlaintext("OverwrittenCorp"u8);
        var secrets = new Dictionary<string, SensitiveBytes>(StringComparer.OrdinalIgnoreCase)
        {
            ["Manufacturer"] = secret
        };

        var result = MsiTransformGenerator.GenerateSecretTransform(baseMsi, secrets, staging);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);

        var applied = Path.Combine(_tempDir, "applied2.msi");
        File.Copy(baseMsi, applied);
        using var db = MsiDatabase.Open(applied, readOnly: false).Value;
        Assert.True(db.ApplyTransform(result.Value).IsSuccess);

        var rows = db.QueryRows(
            "SELECT `Value` FROM `Property` WHERE `Property` = 'Manufacturer'", 1);
        Assert.True(rows.IsSuccess, rows.IsFailure ? rows.Error.Message : null);
        Assert.Equal("OverwrittenCorp", Assert.Single(rows.Value)[0]);
    }

    [Fact]
    public void GenerateSecretTransform_MissingBase_ReturnsFileNotFound()
    {
        var staging = Path.Combine(_tempDir, "staging3");
        Directory.CreateDirectory(staging);
        using var secret = SensitiveBytes.FromPlaintext("x"u8);
        var secrets = new Dictionary<string, SensitiveBytes> { ["P"] = secret };

        var result = MsiTransformGenerator.GenerateSecretTransform(
            Path.Combine(_tempDir, "nope.msi"), secrets, staging);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.FileNotFound, result.Error.Kind);
    }

    [Fact]
    public void GenerateSecretTransform_NoSecrets_ReturnsExecutionError()
    {
        var staging = Path.Combine(_tempDir, "staging4");
        Directory.CreateDirectory(staging);

        var result = MsiTransformGenerator.GenerateSecretTransform(
            Path.Combine(_tempDir, "nope.msi"),
            new Dictionary<string, SensitiveBytes>(),
            staging);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.ExecutionError, result.Error.Kind);
    }
}
