using System.Runtime.Versioning;
using System.Security.Cryptography;
using FalkForge.Compiler.Msi;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Cli.Tests;

/// <summary>
/// Proves <see cref="MsiInspector.Inspect"/> surfaces integrity-signature presence, format tag,
/// and declared key fingerprint(s) for display — non-cryptographic (it does not verify the
/// signature; <see cref="MsiIntegrityVerifier"/> is the verification path). Windows-only: requires
/// msi.dll to build and open a real MSI, mirroring <c>MsiIntegrityVerifierTests</c>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MsiInspectorSignatureTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"MsiInspectSigTest_{Guid.NewGuid():N}");

    public MsiInspectorSignatureTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { }
        }
    }

    private string CompileMsi(string label, bool signed)
    {
        var sourceDir = Path.Combine(_tempDir, $"{label}_source");
        Directory.CreateDirectory(sourceDir);
        var sourceFile = Path.Combine(sourceDir, "app.exe");
        File.WriteAllText(sourceFile, $"content for {label}");

        var outputDir = Path.Combine(_tempDir, $"{label}_output");
        Directory.CreateDirectory(outputDir);

        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = label;
            p.Manufacturer = "TestCorp";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / label));
            if (signed)
                p.Integrity(i => { });
        });

        var result = new MsiCompiler().Compile(package, outputDir);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        return result.Value;
    }

    [Fact]
    public void Inspect_SignedMsi_ReportsSignaturePresentWithFormatAndFingerprints()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        var msiPath = CompileMsi(nameof(Inspect_SignedMsi_ReportsSignaturePresentWithFormatAndFingerprints), signed: true);

        var result = MsiInspector.Inspect(msiPath);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.True(result.Value.SignaturePresent);
        Assert.Equal("falkforge-ecdsa-envelope-v2", result.Value.SignatureFormatTag);
        Assert.NotEmpty(result.Value.SignatureFingerprints);
        Assert.All(result.Value.SignatureFingerprints, fp => Assert.False(string.IsNullOrWhiteSpace(fp)));
    }

    [Fact]
    public void Inspect_UnsignedMsi_ReportsSignatureNotPresent()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        var msiPath = CompileMsi(nameof(Inspect_UnsignedMsi_ReportsSignatureNotPresent), signed: false);

        var result = MsiInspector.Inspect(msiPath);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.False(result.Value.SignaturePresent);
        Assert.Null(result.Value.SignatureFormatTag);
        Assert.Empty(result.Value.SignatureFingerprints);
    }

    [Fact]
    public void Inspect_SignedMsi_PqCompanionFingerprints_NeverOverlapClassical()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        // Merge Gate nit: a zero-config Integrity() build on a PQ-capable machine signs with both a
        // classical (ECDSA-P256) key and an ephemeral ML-DSA companion (EcdsaManifestSigner.
        // BuildProviders). forge verify --trusted-key only ever matches the classical fingerprint, so
        // the two lists must be disjoint — mixing them under one label is the exact copy-paste
        // footgun this split fixes.
        var msiPath = CompileMsi(nameof(Inspect_SignedMsi_PqCompanionFingerprints_NeverOverlapClassical), signed: true);

        var result = MsiInspector.Inspect(msiPath);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.NotEmpty(result.Value.SignatureFingerprints);
        Assert.Empty(result.Value.SignatureFingerprints.Intersect(result.Value.PqCompanionFingerprints, StringComparer.OrdinalIgnoreCase));

        if (MLDsa.IsSupported)
            Assert.NotEmpty(result.Value.PqCompanionFingerprints);
    }

    [Fact]
    public void Inspect_OversizedSidecar_ReportsSignaturePresentButNoFingerprints()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        // Mirrors MsiIntegrityVerifierTests.Verify_OversizedSidecar_ReturnsFailed_NotSilentlyNotSigned:
        // forge inspect must not attempt to read an oversized sidecar either (it shares
        // MsiIntegrityVerifier.LocateEnvelopeJson), but unlike forge verify it has nothing to fail —
        // it reports presence honestly (something is there) without fabricating fingerprints it never read.
        var msiPath = CompileMsi(nameof(Inspect_OversizedSidecar_ReportsSignaturePresentButNoFingerprints), signed: false);
        var sidecarPath = msiPath + ".sig.json";
        using (var fs = new FileStream(sidecarPath, FileMode.Create, FileAccess.Write))
        {
            fs.SetLength(5L * 1024 * 1024);
        }

        var result = MsiInspector.Inspect(msiPath);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.True(result.Value.SignaturePresent);
        Assert.Empty(result.Value.SignatureFingerprints);
        Assert.Empty(result.Value.PqCompanionFingerprints);
    }
}
