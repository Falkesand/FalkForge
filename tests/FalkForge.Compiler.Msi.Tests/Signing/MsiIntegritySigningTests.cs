using System.Runtime.Versioning;
using FalkForge.Compiler.Msi.Tables;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Signing;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Signing;

/// <summary>
/// MSI integrity signing must not silently depend on the external <c>sigil</c> CLI. Before this fix,
/// <c>MsiAuthoring</c> step 8.5 additionally gated on <c>SigilDetector.IsAvailable()</c>: an
/// <c>Integrity()</c>-configured build on a machine without sigil on PATH shipped a completely unsigned
/// MSI with zero warning. The bundle compiler never had this problem (its <c>EcdsaManifestSigner</c> path
/// is pure .NET); these tests prove the MSI compiler now signs the identical way — pure-.NET ECDSA always
/// runs, sigil's DSSE SBOM attestation is a strictly-additive, never-fatal extra.
///
/// <para>Shares a serialized xUnit collection with <see cref="SigilDetectorTests"/>: both test classes
/// touch the process-wide <see cref="SigilDetector"/> cache and (here) the process-wide <c>PATH</c>
/// environment variable, so they must never run concurrently with each other.</para>
/// </summary>
[Collection("SigilProcess")]
[SupportedOSPlatform("windows")]
public sealed class MsiIntegritySigningTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"MsiIntegrityTest_{Guid.NewGuid():N}");
    private readonly string? _originalPath = Environment.GetEnvironmentVariable("PATH");

    public MsiIntegritySigningTests()
    {
        Directory.CreateDirectory(_tempDir);
        Environment.SetEnvironmentVariable("FALKFORGE_NO_SIGN", null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PATH", _originalPath);
        Environment.SetEnvironmentVariable("FALKFORGE_NO_SIGN", null);
        SigilDetector.Reset();
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>
    /// Resolves the FakeSigil test-double project's own build output directory (where its
    /// <c>sigil.exe</c> apphost lands), deriving the Configuration/TargetFramework segments from
    /// this very assembly's own <see cref="AppContext.BaseDirectory"/> rather than hardcoding
    /// "Debug"/"net10.0" — robust to a Release run or a future TFM bump. FakeSigil's
    /// ProjectReference uses <c>ReferenceOutputAssembly="false"</c> precisely so its output is
    /// NOT copied next to this test host (see that csproj's comment for why), so tests that want
    /// it reachable must explicitly prepend this directory to PATH themselves.
    /// </summary>
    private static string ResolveFakeSigilDirectory()
    {
        var binDir = new DirectoryInfo(AppContext.BaseDirectory);       // .../bin/<Config>/<TFM>
        var configDir = binDir.Parent ?? throw new DirectoryNotFoundException();
        var projectDir = configDir.Parent?.Parent ?? throw new DirectoryNotFoundException(); // .../<ThisProject>
        var testsRoot = projectDir.Parent ?? throw new DirectoryNotFoundException();          // .../tests
        return Path.Combine(testsRoot.FullName, "FalkForge.Compiler.Msi.Tests.FakeSigil", "bin", configDir.Name, binDir.Name);
    }

    private (string sourceFile, string outputDir) CreatePackageInputs(string label)
    {
        var sourceDir = Path.Combine(_tempDir, $"{label}_source");
        Directory.CreateDirectory(sourceDir);
        var sourceFile = Path.Combine(sourceDir, "app.exe");
        File.WriteAllText(sourceFile, $"fake executable content for {label}");

        var outputDir = Path.Combine(_tempDir, $"{label}_output");
        Directory.CreateDirectory(outputDir);

        return (sourceFile, outputDir);
    }

    private static List<string?[]> ReadIntegrityRows(string msiPath)
    {
        var dbResult = MsiDatabase.Open(msiPath, readOnly: true);
        Assert.True(dbResult.IsSuccess, dbResult.IsFailure ? dbResult.Error.Message : null);
        using var db = dbResult.Value;

        var rowsResult = db.QueryRows("SELECT `Id`, `Format`, `Data` FROM `_FalkForgeIntegrity`", 3);
        Assert.True(rowsResult.IsSuccess, rowsResult.IsFailure ? rowsResult.Error.Message : null);
        return rowsResult.Value;
    }

    [Fact]
    public void Compile_WithIntegrity_SignsWithEcdsa_EvenWhenSigilIsNotOnPath()
    {
        // Force sigil unreachable regardless of what is actually installed on the host machine (sigil
        // IS present on some dev boxes), so this test proves the pure-.NET path unconditionally, not
        // just "happens to pass here".
        Environment.SetEnvironmentVariable("PATH", string.Empty);
        SigilDetector.Reset();
        Assert.False(SigilDetector.IsAvailable(), "Test setup invariant: sigil must be unreachable with an empty PATH.");

        var (sourceFile, outputDir) = CreatePackageInputs(nameof(Compile_WithIntegrity_SignsWithEcdsa_EvenWhenSigilIsNotOnPath));
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "IntegrityNoSigilApp";
            p.Manufacturer = "TestCorp";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / "IntegrityNoSigilApp"));
            p.Integrity(i => { });
        });

        var compiler = new MsiCompiler();
        var result = compiler.Compile(package, outputDir);

        Assert.True(result.IsSuccess, $"Compile failed: {(result.IsFailure ? result.Error.Message : "")}");

        var rows = ReadIntegrityRows(result.Value);
        var manifestRow = Assert.Single(rows, r => r[0] == "ManifestSignature");
        Assert.Equal(IntegrityTableEmitter.ManifestSignatureFormat, manifestRow[1]);
        Assert.NotEqual("sigil-manifest-v1", manifestRow[1]); // the old, no-longer-accurate format tag

        var envelope = IntegrityEnvelopeCodec.Parse(manifestRow[2]!);
        Assert.NotNull(envelope);
        Assert.True(IntegrityEnvelopeCodec.VerifySignature(envelope!),
            "The embedded ECDSA envelope must cryptographically verify against its own embedded key.");
        Assert.Contains(envelope!.Files, f => f.Name == "app.exe");

        // Without sigil, no SBOM attestation can be produced — but that must never have blocked the
        // signature above.
        Assert.DoesNotContain(rows, r => r[0] == "SbomAttestation");

        // The sidecar signature file is always written too, mirroring the embedded table row.
        Assert.True(File.Exists(result.Value + ".sig.json"));
    }

    [Fact]
    public void Compile_WithIntegrity_AndNoSignEnvVar_SkipsSigningEntirely()
    {
        var (sourceFile, outputDir) = CreatePackageInputs(nameof(Compile_WithIntegrity_AndNoSignEnvVar_SkipsSigningEntirely));
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "IntegrityNoSignApp";
            p.Manufacturer = "TestCorp";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / "IntegrityNoSignApp"));
            p.Integrity(i => { });
        });

        Environment.SetEnvironmentVariable("FALKFORGE_NO_SIGN", "1");
        try
        {
            var compiler = new MsiCompiler();
            var result = compiler.Compile(package, outputDir);

            Assert.True(result.IsSuccess, $"Compile failed: {(result.IsFailure ? result.Error.Message : "")}");

            // No _FalkForgeIntegrity table at all — the explicit opt-out still fully disables signing,
            // exactly like the bundle side's FALKFORGE_NO_SIGN handling.
            var dbResult = MsiDatabase.Open(result.Value, readOnly: true);
            using var db = dbResult.Value;
            var rowsResult = db.QueryRows("SELECT `Id`, `Format`, `Data` FROM `_FalkForgeIntegrity`", 3);
            Assert.True(rowsResult.IsFailure, "Expected no _FalkForgeIntegrity table when FALKFORGE_NO_SIGN is set.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("FALKFORGE_NO_SIGN", null);
        }
    }

    [Fact]
    public void Compile_WithIntegrity_WhenSigilSubcommandFails_StillEmbedsEcdsaSignature()
    {
        // Deterministic CI coverage for the never-fatal SBOM-attestation contract: sigil being
        // reachable on PATH must not change or block the always-on ECDSA signature, even when its
        // sign-manifest/attest subcommands fail. CI has no reason to have real sigil installed, so this
        // does not depend on host state — the FakeSigil project (referenced purely for its build
        // output, never linked into this assembly's code) puts a `sigil.exe` right next to this test
        // assembly that answers `--version` successfully but fails every other subcommand, exactly
        // like a real but unconfigured sigil install.
        var fakeSigilDir = ResolveFakeSigilDirectory();
        Assert.True(File.Exists(Path.Combine(fakeSigilDir, "sigil.exe")),
            $"Test setup invariant: FakeSigil build output not found at '{fakeSigilDir}'.");
        Environment.SetEnvironmentVariable("PATH", fakeSigilDir + Path.PathSeparator + _originalPath);
        SigilDetector.Reset();
        Assert.True(SigilDetector.IsAvailable(), "Test setup invariant: the fake sigil.exe must answer --version.");

        var (sourceFile, outputDir) = CreatePackageInputs(nameof(Compile_WithIntegrity_WhenSigilSubcommandFails_StillEmbedsEcdsaSignature));
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "IntegrityFakeSigilApp";
            p.Manufacturer = "TestCorp";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / "IntegrityFakeSigilApp"));
            p.Integrity(i => { });
        });

        var compiler = new MsiCompiler();
        var result = compiler.Compile(package, outputDir);

        Assert.True(result.IsSuccess, $"Compile failed: {(result.IsFailure ? result.Error.Message : "")}");

        var rows = ReadIntegrityRows(result.Value);
        var manifestRow = Assert.Single(rows, r => r[0] == "ManifestSignature");
        Assert.Equal(IntegrityTableEmitter.ManifestSignatureFormat, manifestRow[1]);
        var envelope = IntegrityEnvelopeCodec.Parse(manifestRow[2]!);
        Assert.NotNull(envelope);
        Assert.True(IntegrityEnvelopeCodec.VerifySignature(envelope!));

        // The fake sigil's sign-manifest/attest subcommands always fail (exit 1) — proving the SBOM
        // row/sidecar are genuinely optional and their absence never blocks the mandatory signature
        // above, deterministically, regardless of what is (or is not) installed on the host machine.
        Assert.DoesNotContain(rows, r => r[0] == "SbomAttestation");
        Assert.False(File.Exists(result.Value + ".attest.json"));
    }

    [Fact]
    public void Compile_WithIntegrity_AndNotConfigured_HasNoIntegrityTable()
    {
        // Negative case: without Integrity() at all, the _FalkForgeIntegrity table must not exist —
        // no table, no rows, nothing to accidentally read as "signed".
        var (sourceFile, outputDir) = CreatePackageInputs(nameof(Compile_WithIntegrity_AndNotConfigured_HasNoIntegrityTable));
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "NoIntegrityApp";
            p.Manufacturer = "TestCorp";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / "NoIntegrityApp"));
            // No .Integrity(...) call.
        });

        var compiler = new MsiCompiler();
        var result = compiler.Compile(package, outputDir);

        Assert.True(result.IsSuccess, $"Compile failed: {(result.IsFailure ? result.Error.Message : "")}");

        var dbResult = MsiDatabase.Open(result.Value, readOnly: true);
        Assert.True(dbResult.IsSuccess, dbResult.IsFailure ? dbResult.Error.Message : null);
        using var db = dbResult.Value;
        var rowsResult = db.QueryRows("SELECT `Id`, `Format`, `Data` FROM `_FalkForgeIntegrity`", 3);
        Assert.True(rowsResult.IsFailure, "Expected no _FalkForgeIntegrity table when Integrity() is never configured.");
        Assert.False(File.Exists(result.Value + ".sig.json"));
    }
}
