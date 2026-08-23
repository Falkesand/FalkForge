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
        var fake = new FakeStaging(staging);
        var command = new MsiInstallCommand(_mockMsiApi, fake);

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
        Assert.Contains("TRANSFORMS=\"", _mockMsiApi.LastCommandLine, StringComparison.Ordinal);
        Assert.DoesNotContain(password, _mockMsiApi.LastCommandLine, StringComparison.Ordinal);
        Assert.DoesNotContain("SQLPASSWORD", _mockMsiApi.LastCommandLine, StringComparison.Ordinal);

        // The generated transform genuinely set the property.
        Assert.Equal(password, valueDuringInstall);

        // The per-install staging directory (working copy + transform) is deleted afterward by the lease.
        Assert.NotNull(fake.LastDirectory);
        Assert.False(Directory.Exists(fake.LastDirectory));
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
        Assert.Contains(@"C:\author\lang.mst;", _mockMsiApi.LastCommandLine, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", _mockMsiApi.LastCommandLine, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_InvalidSecretPropertyName_ReturnsSecurityError_NeverInstalls()
    {
        // A forged peer must not set an arbitrarily-named property just because the value rides the
        // transform instead of the command line. The name is validated against ^[A-Z_][A-Z0-9_.]*$.
        var baseMsi = Path.Combine(_tempDir, "fake.msi");
        File.WriteAllBytes(baseMsi, [0x00]);
        var command = new MsiInstallCommand(_mockMsiApi, new FakeStaging(_tempDir));

        var payload = BuildPayload(
            baseMsi, string.Empty, HashOf(baseMsi), [("bad name!", "x"u8.ToArray())]);

        var result = command.Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Equal(0, _mockMsiApi.InstallProductCallCount);
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
    public void SweepStale_DeletesLeftoverSubdirectoriesAndLooseFiles()
    {
        var root = Path.Combine(_tempDir, "sweep");
        Directory.CreateDirectory(root);

        // A per-install subdirectory a crash left behind, with its secret files still inside.
        var staleSub = Path.Combine(root, "stg-abcd");
        Directory.CreateDirectory(staleSub);
        File.WriteAllText(Path.Combine(staleSub, "~pw-x.msi"), "x");
        File.WriteAllText(Path.Combine(staleSub, "st-x.mst"), "x");

        // Legacy loose files at the root.
        var looseMst = Path.Combine(root, "st-abcd.mst");
        var keep = Path.Combine(root, "unrelated.txt");
        File.WriteAllText(looseMst, "x");
        File.WriteAllText(keep, "x");

        SecureTransformStaging.SweepStale(root);

        Assert.False(Directory.Exists(staleSub)); // whole stale subdirectory removed
        Assert.False(File.Exists(looseMst));
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

    private sealed class FakeStaging(string root) : ISecureTransformStaging
    {
        public string? LastDirectory { get; private set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "IDisposableAnalyzers.Correctness", "IDISP005:Return type should indicate that the value should be disposed",
            Justification = "Mirrors the production interface: the lease is returned in Result<T> and the " +
                "command under test owns and disposes it.")]
        public Result<SecureStagingLease> CreateStagingDirectory()
        {
            var dir = Path.Combine(root, $"stg-{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            LastDirectory = dir;
            // No pin handle in the fake: the test exercises generation/merge/cleanup, not the real
            // no-follow directory pin (which needs the SYSTEM-owned ProgramData path).
            return new SecureStagingLease(dir, null);
        }
    }
}
