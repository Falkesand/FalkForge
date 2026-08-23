namespace FalkForge.Engine.Elevation.Tests.Commands;

using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using FalkForge.Builders;
using FalkForge.Compiler.Msi;
using FalkForge.Engine.Elevation.Commands;
using FalkForge.Engine.Elevation.Tests.Mocks;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Testing;
using Xunit;

/// <summary>
/// The elevated companion sets a secure property through a transform it generates ITSELF in a staging
/// directory it owns, never a transform path the unelevated engine supplied. These tests inject a writable
/// temp staging directory (production uses the SYSTEM + Administrators-only directory under %ProgramData%)
/// so the generation, TRANSFORMS merge, and cleanup can be exercised without elevation; a real base MSI is
/// compiled so the transform actually sets the property. A malformed secret block fails closed.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MsiInstallCommandSecretTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), $"MsiInstallSecret_{Guid.NewGuid():N}");
    private readonly MockMsiApi _mockMsiApi = new();

    public MsiInstallCommandSecretTests() => Directory.CreateDirectory(_tempDir);

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
    public void Execute_SecretProperty_GeneratesTransform_SetsProperty_NeverOnCommandLine()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        var baseMsi = CompileBaseMsi();
        var staging = Path.Combine(_tempDir, "staging");
        Directory.CreateDirectory(staging);
        var command = new MsiInstallCommand(_mockMsiApi, new FakeStaging(staging));

        const string password = "P@ss \" & | ; < > w0rd!";
        string? valueDuringInstall = null;
        _mockMsiApi.OnInstallProductCalled = () =>
            valueDuringInstall = ReadTransformedProperty(baseMsi, _mockMsiApi.LastCommandLine!, "SQLPASSWORD");

        var payload = BuildPayload(
            baseMsi, string.Empty, HashOf(baseMsi),
            [("SQLPASSWORD", Encoding.UTF8.GetBytes(password))]);

        var result = command.Execute(payload);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.NotNull(_mockMsiApi.LastCommandLine);
        Assert.Contains("TRANSFORMS=\"", _mockMsiApi.LastCommandLine);
        Assert.DoesNotContain(password, _mockMsiApi.LastCommandLine);
        Assert.DoesNotContain("SQLPASSWORD", _mockMsiApi.LastCommandLine);

        // The generated transform genuinely set the property.
        Assert.Equal(password, valueDuringInstall);

        // Both staged files (working copy and transform) are gone afterward.
        Assert.Empty(Directory.GetFiles(staging, "~pw-*.msi"));
        Assert.Empty(Directory.GetFiles(staging, "st-*.mst"));
    }

    [Fact]
    public void Execute_AuthorTransformAndSecret_MergeIntoOneTransformsPair()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        var baseMsi = CompileBaseMsi();
        var staging = Path.Combine(_tempDir, "staging2");
        Directory.CreateDirectory(staging);
        var command = new MsiInstallCommand(_mockMsiApi, new FakeStaging(staging));

        var payload = BuildPayload(
            baseMsi, " TRANSFORMS=\"C:\\author\\lang.mst\"", HashOf(baseMsi),
            [("APIKEY", "hunter2"u8.ToArray())]);

        var result = command.Execute(payload);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(1, CountOccurrences(_mockMsiApi.LastCommandLine!, "TRANSFORMS=\""));
        Assert.Contains(@"C:\author\lang.mst;", _mockMsiApi.LastCommandLine);
        Assert.DoesNotContain("hunter2", _mockMsiApi.LastCommandLine);
    }

    [Fact]
    public void Execute_NoSecretBlock_InstallsNormally()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        var baseMsi = CompileBaseMsi();
        var command = new MsiInstallCommand(_mockMsiApi, new FakeStaging(_tempDir));

        // Three-field payload, no trailing secret block — the wire shape a non-secret install produces.
        var payload = BuildPayload(baseMsi, string.Empty, HashOf(baseMsi), []);

        var result = command.Execute(payload);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(1, _mockMsiApi.InstallProductCallCount);
        Assert.Null(_mockMsiApi.LastCommandLine);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(65)] // MaxSecretProperties is 64
    public void Execute_OutOfRangeSecretCount_ReturnsSecurityError(int count)
    {
        var baseMsi = Path.Combine(_tempDir, "fake.msi");
        File.WriteAllBytes(baseMsi, [0x00]);
        var command = new MsiInstallCommand(_mockMsiApi, new FakeStaging(_tempDir));

        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(baseMsi);
            w.Write(string.Empty);
            w.Write(HashOf(baseMsi));
            w.Write(count);
        }

        var result = command.Execute(stream.ToArray());

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Equal(0, _mockMsiApi.InstallProductCallCount);
    }

    [Fact]
    public void Execute_TruncatedSecretBlock_ReturnsSecurityError()
    {
        var baseMsi = Path.Combine(_tempDir, "fake.msi");
        File.WriteAllBytes(baseMsi, [0x00]);
        var command = new MsiInstallCommand(_mockMsiApi, new FakeStaging(_tempDir));

        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(baseMsi);
            w.Write(string.Empty);
            w.Write(HashOf(baseMsi));
            w.Write(1);           // one secret announced
            w.Write("SECRET");    // name
            w.Write(32);          // says 32 bytes follow
            w.Write(new byte[8]); // but only 8 are present
        }

        var result = command.Execute(stream.ToArray());

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Equal(0, _mockMsiApi.InstallProductCallCount);
    }

    [Fact]
    public void Execute_OversizedSecretValue_ReturnsSecurityError()
    {
        var baseMsi = Path.Combine(_tempDir, "fake.msi");
        File.WriteAllBytes(baseMsi, [0x00]);
        var command = new MsiInstallCommand(_mockMsiApi, new FakeStaging(_tempDir));

        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(baseMsi);
            w.Write(string.Empty);
            w.Write(HashOf(baseMsi));
            w.Write(1);
            w.Write("SECRET");
            w.Write((64 * 1024) + 1); // one byte over MaxSecretValueBytes
        }

        var result = command.Execute(stream.ToArray());

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Equal(0, _mockMsiApi.InstallProductCallCount);
    }

    [Fact]
    public void SweepStale_DeletesLeftoverWorkingCopiesAndTransforms()
    {
        var root = Path.Combine(_tempDir, "sweep");
        Directory.CreateDirectory(root);
        var pw = Path.Combine(root, "~pw-abcd.msi");
        var mst = Path.Combine(root, "st-abcd.mst");
        var keep = Path.Combine(root, "unrelated.txt");
        File.WriteAllText(pw, "x");
        File.WriteAllText(mst, "x");
        File.WriteAllText(keep, "x");

        SecureTransformStaging.SweepStale(root);

        Assert.False(File.Exists(pw));
        Assert.False(File.Exists(mst));
        Assert.True(File.Exists(keep)); // only staging artifacts are swept
    }

    [Fact]
    public void SweepStale_MissingDirectory_DoesNotThrow()
    {
        // Never breaks startup when the directory has not been created yet.
        var missing = Path.Combine(_tempDir, "does-not-exist");
        var exception = Record.Exception(() => SecureTransformStaging.SweepStale(missing));

        Assert.Null(exception);
        Assert.False(Directory.Exists(missing)); // the sweep must not create the directory
    }

    private static string ReadTransformedProperty(string baseMsi, string commandLine, string property)
    {
        var mst = ExtractTransformPath(commandLine);
        var secretMst = mst.Contains(';', StringComparison.Ordinal)
            ? mst[(mst.LastIndexOf(';') + 1)..]
            : mst;

        var applied = Path.Combine(Path.GetDirectoryName(secretMst)!, $"read-{Guid.NewGuid():N}.msi");
        File.Copy(baseMsi, applied);
        using var db = MsiDatabase.Open(applied, readOnly: false).Value;
        Assert.True(db.ApplyTransform(secretMst).IsSuccess);
        var rows = db.QueryRows($"SELECT `Value` FROM `Property` WHERE `Property` = '{property}'", 1);
        Assert.True(rows.IsSuccess);
        return Assert.Single(rows.Value)[0]!;
    }

    private static string ExtractTransformPath(string commandLine)
    {
        const string marker = "TRANSFORMS=\"";
        var start = commandLine.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var end = commandLine.IndexOf('"', start);
        return commandLine[start..end];
    }

    private static byte[] BuildPayload(
        string msiPath, string additionalArgs, string hash, (string name, byte[] value)[] secrets)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8);
        writer.Write(msiPath);
        writer.Write(additionalArgs);
        writer.Write(hash);
        if (secrets.Length > 0)
        {
            writer.Write(secrets.Length);
            foreach (var (name, value) in secrets)
            {
                writer.Write(name);
                writer.Write(value.Length);
                writer.Write(value);
            }
        }

        writer.Flush();
        return stream.ToArray();
    }

    private static string HashOf(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    private sealed class FakeStaging(string dir) : ISecureTransformStaging
    {
        public Result<string> Ensure() => dir;
    }
}
