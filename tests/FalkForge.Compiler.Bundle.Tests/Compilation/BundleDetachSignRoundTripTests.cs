using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Engine.Protocol.Bundle;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Platform.Windows;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Compilation;

/// <summary>
/// End-to-end proof of the code-signing ceremony a bundle publisher actually performs:
/// <c>forge bundle detach</c> → <c>signtool</c> (an external Authenticode sign of the bare PE
/// stub) → <c>forge bundle reattach</c>. The existing <see cref="BundleDetacherTests"/> only ever
/// FAKE the "signed stub" (they append zero bytes to exercise the offset arithmetic) and only
/// re-read TOC METADATA — they never apply a real signature, never verify one after reattach, and
/// never decompress a single payload BYTE out of the reattached bundle. These tests close both
/// gaps: they prove the reattached bundle (a) still self-extracts its payload bytes byte-for-byte,
/// and (b) still carries a valid Authenticode signature whose digest verifies over the whole file
/// including the appended bundle data.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class BundleDetachSignRoundTripTests : IDisposable
{
    /// <summary>
    /// CERT_E_UNTRUSTEDROOT — the signature's digest verified but the (self-signed) certificate
    /// does not chain to a trusted root. For a fresh, un-installed self-signed code-signing cert
    /// this is the expected WinVerifyTrust outcome: it proves the signature is cryptographically
    /// intact (NOT a bad digest, NOT missing) while stopping short of full machine trust — exactly
    /// the state we can assert without writing to the machine's Root store (which would need admin
    /// / an interactive confirmation dialog and would make the test environment-dependent).
    /// </summary>
    private const string UntrustedRootStatus = "0x800B0109";

    private readonly string _tempDir;

    public BundleDetachSignRoundTripTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"DetachSignTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>
    /// The self-extraction guarantee, independent of any external tool: a bundle round-tripped
    /// through <c>Detach</c> → <c>Reattach</c> still yields every payload's ORIGINAL bytes when
    /// decompressed (not merely a matching TOC row). This is the property a user relies on — a
    /// reattached bundle that verifies but no longer installs its payloads would be useless — and
    /// it must hold whether or not a code-signing SDK is present, so it has no signtool gate.
    /// </summary>
    [Fact]
    public void DetachReattach_SelfExtractsOriginalPayloadBytes()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");

        var payload1 = RandomBytes(11_000);
        var payload2 = RandomBytes(7_500);
        var bundlePath = BuildBundle(
            CreateStubFile("MZ_SELF_EXTRACT_STUB"),
            ("SelfExtract1", payload1),
            ("SelfExtract2", payload2));

        var stubPath = Path.Combine(_tempDir, "detached_stub.bin");
        var dataPath = Path.Combine(_tempDir, "detached.data");
        Assert.True(BundleDetacher.Detach(bundlePath, stubPath, dataPath).IsSuccess);

        // Reattach with the unsigned detached stub (delta 0) — the pure round-trip.
        var reattachedPath = Path.Combine(_tempDir, "reattached.exe");
        Assert.True(BundleDetacher.Reattach(stubPath, dataPath, reattachedPath).IsSuccess);

        AssertPayloadsExtractByteForByte(
            reattachedPath,
            ("SelfExtract1", payload1),
            ("SelfExtract2", payload2));
    }

    /// <summary>
    /// Proves the full <c>forge bundle detach</c> → <c>signtool</c> → <c>forge bundle reattach</c>
    /// ceremony PRESERVES a genuine Authenticode signature end-to-end. Gated on signtool.exe from
    /// the Windows SDK — present on the windows-latest CI image (so this runs in CI) and skipped
    /// cleanly on machines without the SDK, mirroring the darice.cub probe/skip pattern the ICE
    /// tests use.
    /// <para>
    /// Authenticode locates the PE attribute-certificate table via the optional header's Security
    /// data directory and EXCLUDES that whole region from the signed digest. A naive reattach that
    /// simply appends the bundle data (manifest + payloads + TOC + footer) past the cert table
    /// leaves the table short of EOF, and Windows then reports the file as <b>unsigned</b>
    /// (TRUST_E_NOSIGNATURE). <see cref="BundleDetacher.Reattach"/> avoids that by extending the
    /// Security data-directory size (and the trailing WIN_CERTIFICATE length) to span the appended
    /// bytes, so the cert table again ends at EOF. Both patched fields lie inside Authenticode's
    /// excluded regions, so the digest the publisher signed over the bare stub still matches: the
    /// reattached bundle verifies to <see cref="UntrustedRootStatus"/> (the SAME status as the bare
    /// signed stub), NOT TRUST_E_NOSIGNATURE.
    /// </para>
    /// <para>
    /// Verified here via WinVerifyTrust (<see cref="AuthenticodeValidator"/>) on Windows 10.0.26200.
    /// The self-signed, un-installed cert stops the chain at an untrusted root — which is exactly
    /// what proves the DIGEST verified (not a bad digest, not a missing signature) without needing
    /// to write to the machine Root store. NOTE: this relies on default WinVerifyTrust behavior;
    /// a machine with the opt-in CVE-2013-3900 certificate-padding hardening enabled would reject
    /// the trailing bytes inside the enlarged WIN_CERTIFICATE — see the demo README caveat.
    /// </para>
    /// <para>
    /// The supporting properties are asserted too: the bare stub signs and is recognized by Windows,
    /// the signed stub's bytes survive reattach verbatim, and — because FalkForge's payload trust is
    /// the independent in-manifest ECDSA layer, not Authenticode — the reattached bundle still
    /// self-extracts its payloads byte-for-byte.
    /// </para>
    /// </summary>
    [Fact]
    public void DetachSignReattach_ReattachedBundlePreservesAuthenticode()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");

        var signtool = FindSignTool();
        Assert.SkipUnless(signtool is not null,
            "signtool.exe not found (Windows SDK absent) — skipping the Authenticode round-trip. " +
            "The signtool-independent self-extraction guarantee is covered by " +
            nameof(DetachReattach_SelfExtractsOriginalPayloadBytes) + ".");

        var payload1 = RandomBytes(9_000);
        var payload2 = RandomBytes(13_337);

        // The stub MUST be a genuine PE for signtool to sign it, so we build the bundle on top of a
        // real assembly (the production Compiler.Bundle DLL) rather than the ASCII placeholders the
        // arithmetic-only tests use.
        var realPe = typeof(BundleDetacher).Assembly.Location;
        Assert.False(string.IsNullOrEmpty(realPe), "Expected a real on-disk assembly to use as a PE stub.");
        var stubSource = Path.Combine(_tempDir, "pe_stub.dll");
        File.Copy(realPe, stubSource, overwrite: true);

        var bundlePath = BuildBundle(stubSource, ("Signed1", payload1), ("Signed2", payload2));

        var detachedStub = Path.Combine(_tempDir, "signed_stub.dll");
        var detachedData = Path.Combine(_tempDir, "signed.data");
        Assert.True(BundleDetacher.Detach(bundlePath, detachedStub, detachedData).IsSuccess);

        // Detach must recover the exact PE bytes so signtool has a genuine stub to sign.
        Assert.Equal(File.ReadAllBytes(stubSource), File.ReadAllBytes(detachedStub));

        // Apply a REAL Authenticode signature to the bare stub with an in-code self-signed
        // code-signing certificate, exactly as a publisher would with their own cert.
        SignWithSelfSignedCert(signtool!, detachedStub);

        var validator = new AuthenticodeValidator();

        // The bare signed stub IS recognized by Windows: WinVerifyTrust locates and digest-verifies
        // the signature and stops only at the (self-signed, un-installed) root — untrusted-root, NOT
        // a bad digest and NOT a missing signature. This is the baseline the reattached bundle fails
        // to preserve.
        var stubStatus = VerifyStatus(validator, detachedStub);
        Assert.Equal(UntrustedRootStatus, stubStatus);

        var reattachedPath = Path.Combine(_tempDir, "signed_reattached.exe");
        Assert.True(BundleDetacher.Reattach(detachedStub, detachedData, reattachedPath).IsSuccess);

        // (1) The signed, reattached bundle still self-extracts every payload byte-for-byte — the
        //     self-extraction path does not depend on Authenticode. Reattach edits only the two
        //     Authenticode-excluded header fields (the PE Security data-directory size and the last
        //     WIN_CERTIFICATE length); the reattached bundle is therefore NOT byte-identical to the
        //     signed stub in those fields — the meaningful proof that the signature blob survived
        //     intact is the untrusted-root (digest-valid) status asserted in (2) below, which a
        //     corrupted PKCS#7 blob could never produce.
        AssertPayloadsExtractByteForByte(reattachedPath, ("Signed1", payload1), ("Signed2", payload2));

        // (2) THE FIX: Reattach extended the PE Security data-directory (and the trailing
        //     WIN_CERTIFICATE) to span the appended bundle bytes, so the cert table again ends at
        //     EOF and the publisher's digest still verifies. The reattached bundle reports the SAME
        //     WinVerifyTrust status as the bare signed stub — untrusted-root, i.e. the signature is
        //     PRESENT and digest-valid, stopping only at the self-signed root — NOT NoSignature.
        var reattachedStatus = VerifyStatus(validator, reattachedPath);
        Assert.Equal(UntrustedRootStatus, reattachedStatus);
        Assert.Equal(stubStatus, reattachedStatus);
    }

    // --------------------------------------------------------------------------------------------
    // Helpers
    // --------------------------------------------------------------------------------------------

    /// <summary>
    /// Runs <c>AuthenticodeValidator.ValidateSignature</c> and returns the WinVerifyTrust status
    /// token (e.g. <c>0x800B0109</c>) it embeds in the failure message. A self-signed cert never
    /// yields full trust, so success (which returns no status token) is itself a failure here and
    /// surfaces loudly rather than being masked.
    /// </summary>
    private static string VerifyStatus(AuthenticodeValidator validator, string filePath)
    {
        var result = validator.ValidateSignature(filePath, expectedThumbprint: null, expectedPublicKeyHash: null);
        Assert.True(result.IsFailure,
            "A self-signed, un-trusted certificate must not produce a full-trust result; " +
            "getting success here means the test's trust assumptions no longer hold.");

        var message = result.Error.Message;
        var idx = message.IndexOf("0x", StringComparison.OrdinalIgnoreCase);
        Assert.True(idx >= 0 && idx + 10 <= message.Length,
            $"Expected a WinVerifyTrust status token in the message, got: {message}");
        return message.Substring(idx, 10);
    }

    private static void SignWithSelfSignedCert(string signtoolPath, string filePath)
    {
        const string pfxPassword = "falkforge-detach-sign-test";
        var pfxPath = Path.Combine(Path.GetDirectoryName(filePath)!, "signing_cert.pfx");

        using (var rsa = RSA.Create(2048))
        {
            var request = new CertificateRequest(
                "CN=FalkForge Detach Sign Test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(
                new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
            // EKU: Code Signing (1.3.6.1.5.5.7.3.3) — required for Authenticode.
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid("1.3.6.1.5.5.7.3.3") }, critical: true));

            using var cert = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
            File.WriteAllBytes(pfxPath, cert.Export(X509ContentType.Pfx, pfxPassword));
        }

        var psi = new ProcessStartInfo(signtoolPath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var arg in new[] { "sign", "/fd", "sha256", "/f", pfxPath, "/p", pfxPassword, filePath })
            psi.ArgumentList.Add(arg);

        // Drain stdout/stderr via async event callbacks BEFORE waiting on exit: reading the two
        // streams sequentially (ReadToEnd then ReadToEnd) can deadlock if signtool fills one pipe's
        // OS buffer while blocked writing to the other and this process is still blocked reading the
        // first — the event-driven pump reads both concurrently off the ThreadPool so neither side
        // can back up.
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (!process.WaitForExit(TimeSpan.FromMinutes(2)))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("signtool did not exit within 2 minutes.");
        }

        // Parameterless WaitForExit after the timed overload ensures the async output-relay events
        // have fully drained before we read the buffers below (per Process.WaitForExit remarks).
        process.WaitForExit();

        Assert.True(process.ExitCode == 0,
            $"signtool failed (exit {process.ExitCode}). stdout: {stdout} stderr: {stderr}");
    }

    /// <summary>
    /// Locates signtool.exe in the installed Windows SDKs, preferring an x64 build. Mirrors the
    /// production <c>CodeSigner.FindSignTool</c> search without coupling this test to that internal
    /// (it lives in a different assembly).
    /// </summary>
    private static string? FindSignTool()
    {
        string[] roots =
        [
            @"C:\Program Files (x86)\Windows Kits\10\bin",
            @"C:\Program Files\Windows Kits\10\bin"
        ];

        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
                continue;
            try
            {
                var hits = Directory.GetFiles(root, "signtool.exe", SearchOption.AllDirectories);
                if (hits.Length == 0)
                    continue;
                var x64 = hits
                    .Where(f => f.Contains("x64", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                return x64 ?? hits.OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase).First();
            }
            catch (UnauthorizedAccessException)
            {
                // Skip directories we cannot enumerate.
            }
        }

        return null;
    }

    private string BuildBundle(string stubPath, params (string PackageId, byte[] Data)[] payloads)
    {
        var payloadEntries = new PayloadEntry[payloads.Length];
        for (var i = 0; i < payloads.Length; i++)
        {
            var (packageId, data) = payloads[i];
            var payloadPath = Path.Combine(_tempDir, $"payload_{packageId}_{Guid.NewGuid():N}.bin");
            File.WriteAllBytes(payloadPath, data);
            payloadEntries[i] = new PayloadEntry
            {
                PackageId = packageId,
                SourcePath = payloadPath,
                OriginalSize = data.Length,
                Sha256Hash = Convert.ToHexString(SHA256.HashData(data))
            };
        }

        var bundlePath = Path.Combine(_tempDir, $"bundle_{Guid.NewGuid():N}.exe");
        var embedResult = new PayloadEmbedder().Embed(stubPath, bundlePath, CreateManifest(), payloadEntries);
        Assert.True(embedResult.IsSuccess, embedResult.IsFailure ? embedResult.Error.Message : null);
        return bundlePath;
    }

    private static void AssertPayloadsExtractByteForByte(
        string bundlePath, params (string PackageId, byte[] Data)[] expected)
    {
        var extract = PayloadEmbedder.Extract(bundlePath);
        Assert.True(extract.IsSuccess, extract.IsFailure ? extract.Error.Message : null);
        var entries = extract.Value.TocEntries;
        Assert.Equal(expected.Length, entries.Length);

        foreach (var (packageId, data) in expected)
        {
            var entry = Array.Find(entries, e => e.PackageId == packageId);
            Assert.NotNull(entry);

            // ExtractPayload decompresses AND SHA-256-verifies the payload — this is the real
            // self-extraction path, not a TOC metadata read.
            var payloadResult = BundleReader.ExtractPayload(bundlePath, entry);
            Assert.True(payloadResult.IsSuccess, payloadResult.IsFailure ? payloadResult.Error.Message : null);
            Assert.Equal(data, payloadResult.Value);
        }
    }

    private string CreateStubFile(string content)
    {
        var path = Path.Combine(_tempDir, $"stub_{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes(content));
        return path;
    }

    private static byte[] RandomBytes(int length)
    {
        var bytes = new byte[length];
        RandomNumberGenerator.Fill(bytes);
        return bytes;
    }

    private static InstallerManifest CreateManifest() => new()
    {
        Name = "DetachSignTest",
        Manufacturer = "TestCo",
        Version = "1.0.0",
        BundleId = Guid.NewGuid(),
        UpgradeCode = Guid.NewGuid(),
        Packages = [],
        Scope = InstallScope.PerMachine
    };
}
