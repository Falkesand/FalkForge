using System.Security.Cryptography;
using System.Text.Json;
using FalkForge;
using FalkForge.Compiler.Bundle.Builders;
using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Engine.Protocol.Bundle;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

namespace FalkForge.Integration.Tests;

/// <summary>
/// End-to-end proof of the "signed bundle, TOC-hash tamper" attack and the binding that closes it.
///
/// <para>The attack: take a validly integrity-signed bundle, replace a payload's bytes, and rewrite
/// the matching overlay <see cref="TocEntry.Sha256Hash"/> to the tampered bytes' hash — leaving the
/// signed manifest and its ECDSA signature completely untouched. Runtime extraction verifies the
/// payload bytes only against the (unsigned) TOC hash, so it accepts the tampered payload; nothing
/// binds the executed bytes to the signed manifest hash.</para>
///
/// <para>This test reproduces the attacker's bundle with the real compiler + embedder (original
/// signed manifest, tampered payload, matching tampered TOC hash) and proves both halves: the raw
/// extractor is fooled, and <see cref="SignedPayloadTocVerifier"/> rejects it.</para>
/// </summary>
public sealed class BundlePayloadTrustBindingTests
{
    // Consistency-only: the e2e bundle is signed with an ephemeral key of unknown fingerprint, so the
    // TOC-binding tests use an empty trusted set. The trust-set rejection path is covered by the
    // codec/gate unit tests.
    private static readonly IReadOnlySet<string> NoTrust = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private static (string msiPath, string dir) FakePayload(string name, byte[] bytes)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"falk-trust-e2e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var msiPath = Path.Combine(dir, name);
        File.WriteAllBytes(msiPath, bytes);
        return (msiPath, dir);
    }

    private static InstallerManifest ExtractManifest(BundleContent content)
    {
        var manifest = JsonSerializer.Deserialize<InstallerManifest>(content.ManifestJsonBytes!);
        Assert.NotNull(manifest);
        return manifest!;
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    [Fact]
    public void TamperedPayloadWithMatchingTocHash_FoolsRawExtractor_ButSignedTocVerifierRejects()
    {
        // 1. Build a validly integrity-signed bundle over the ORIGINAL payload.
        var originalBytes = RandomNumberGenerator.GetBytes(512);
        var (msiPath, dir) = FakePayload("App.msi", originalBytes);
        try
        {
            var model = new BundleBuilder()
                .Name("TrustBind")
                .Manufacturer("Integration Tests")
                .Version("1.0.0")
                .UseSilentUI()
                .Integrity(i => { }) // ephemeral ECDSA key, no external tool needed
                .Chain(chain => chain.MsiPackage(msiPath, pkg => pkg.Id("AppMsi").Version("1.0.0")))
                .Build();

            var buildResult = new BundleCompiler().Compile(model, Path.Combine(dir, "out"));
            Assert.True(buildResult.IsSuccess, buildResult.IsFailure ? buildResult.Error.Message : null);

            var signedContent = PayloadEmbedder.Extract(buildResult.Value);
            Assert.True(signedContent.IsSuccess, signedContent.IsFailure ? signedContent.Error.Message : null);
            var signedManifest = ExtractManifest(signedContent.Value);
            Assert.NotNull(signedManifest.ManifestSignature); // the bundle really is signed

            // 2. Attacker: replace the payload bytes and re-embed the bundle with the UNCHANGED
            //    signed manifest but a TOC hash that matches the tampered bytes. This is exactly a
            //    post-signing overlay rewrite — the signature still covers the original hash.
            var tamperedBytes = (byte[])originalBytes.Clone();
            tamperedBytes[0] ^= 0xFF; // flip a byte -> different payload, different hash
            var tamperedMsi = Path.Combine(dir, "App.tampered.msi");
            File.WriteAllBytes(tamperedMsi, tamperedBytes);
            var tamperedHash = HashFile(tamperedMsi);

            var stubPath = Path.Combine(dir, "stub.bin");
            File.WriteAllBytes(stubPath, []);
            var attackerBundle = Path.Combine(dir, "attacker.exe");

            var tamperedPayload = new PayloadEntry
            {
                PackageId = "AppMsi",
                SourcePath = tamperedMsi,
                OriginalSize = tamperedBytes.Length,
                Sha256Hash = tamperedHash // TOC hash matches the tampered bytes
            };

            var embedResult = new PayloadEmbedder().Embed(
                stubPath, attackerBundle, signedManifest, new[] { tamperedPayload });
            Assert.True(embedResult.IsSuccess, embedResult.IsFailure ? embedResult.Error.Message : null);

            var attackerContent = PayloadEmbedder.Extract(attackerBundle);
            Assert.True(attackerContent.IsSuccess, attackerContent.IsFailure ? attackerContent.Error.Message : null);
            var attackerToc = attackerContent.Value.TocEntries;
            var appEntry = Array.Find(attackerToc, e => e.PackageId == "AppMsi")!;

            // 3a. The raw extractor is FOOLED: bytes == TOC hash, so the tampered payload extracts
            //     cleanly. This documents why TOC-only verification is insufficient.
            var extractDest = Path.Combine(dir, "extracted");
            var rawExtract = BundleReader.ExtractPayloadToFile(
                attackerBundle, appEntry, extractDest, "AppMsi.dat");
            Assert.True(rawExtract.IsSuccess,
                "Raw extractor should accept the tampered payload against the tampered TOC hash");
            Assert.Equal(tamperedHash, HashFile(rawExtract.Value)); // tampered bytes landed on disk

            // 3b. The binding REJECTS it: the TOC hash disagrees with the signed manifest hash. The
            //     bundle is signed with an ephemeral key of unknown fingerprint, so we verify in
            //     consistency-only mode (empty trusted set) — the TOC-tamper (INT006) still fires.
            var attackerManifest = ExtractManifest(attackerContent.Value);
            var trust = SignedPayloadTocVerifier.Verify(attackerManifest, attackerToc, NoTrust);
            Assert.True(trust.IsFailure, "SignedPayloadTocVerifier must reject the post-signing TOC tamper");
            Assert.Equal(ErrorKind.IntegrityError, trust.Error.Kind);
            Assert.Contains("INT006", trust.Error.Message, StringComparison.Ordinal);

            // 4. Sanity: the untampered, genuinely-signed bundle passes the same binding.
            var cleanTrust = SignedPayloadTocVerifier.Verify(signedManifest, signedContent.Value.TocEntries, NoTrust);
            Assert.True(cleanTrust.IsSuccess, cleanTrust.IsFailure ? cleanTrust.Error.Message : null);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// B2 — the strip-downgrade attack (C14 Stage 2). An attacker takes a validly signed bundle, sets
    /// <see cref="InstallerManifest.ManifestSignature"/> to null (drops the envelope), tampers the
    /// payloads, and rewrites the TOC hashes to match. Both gates then see an "unsigned" bundle. On a
    /// fresh install that is allowed for backward compatibility; on the <b>update path</b> — where the
    /// engine downloads and executes an automatic replacement of already-trusted software — it is a
    /// straight RCE and must be refused.
    ///
    /// <para>This proves the asymmetry the launcher wires on: the same stripped bundle is REJECTED
    /// (INT007, before any extraction) when the update launcher asserts require-signed, and still
    /// ALLOWED on a fresh install (require-signed off).</para>
    /// </summary>
    [Fact]
    public void StrippedSignatureUpdate_RejectedWhenRequireSigned_ButFreshInstallAllowed_B2()
    {
        var originalBytes = RandomNumberGenerator.GetBytes(256);
        var (msiPath, dir) = FakePayload("App.msi", originalBytes);
        try
        {
            // Build a validly integrity-signed bundle, then strip its signature (the attacker's move).
            var model = new BundleBuilder()
                .Name("StripB2")
                .Manufacturer("Integration Tests")
                .Version("1.0.0")
                .UseSilentUI()
                .Integrity(i => { })
                .Chain(chain => chain.MsiPackage(msiPath, pkg => pkg.Id("AppMsi").Version("1.0.0")))
                .Build();

            var buildResult = new BundleCompiler().Compile(model, Path.Combine(dir, "out"));
            Assert.True(buildResult.IsSuccess, buildResult.IsFailure ? buildResult.Error.Message : null);

            var content = PayloadEmbedder.Extract(buildResult.Value);
            Assert.True(content.IsSuccess, content.IsFailure ? content.Error.Message : null);
            var signedManifest = ExtractManifest(content.Value);
            Assert.NotNull(signedManifest.ManifestSignature); // really signed before the strip

            var strippedManifest = signedManifest with { ManifestSignature = null };
            var toc = content.Value.TocEntries;

            // Update path (launcher asserts --require-signed): the stripped update is refused before
            // any payload is extracted or executed.
            var updatePath = SignedPayloadTocVerifier.Verify(strippedManifest, toc, NoTrust, requireSigned: true);
            Assert.True(updatePath.IsFailure, "a stripped/unsigned update must be rejected on the update path");
            Assert.Equal(ErrorKind.IntegrityError, updatePath.Error.Kind);
            Assert.Contains("INT007", updatePath.Error.Message, StringComparison.Ordinal);

            // Fresh install (require-signed off): the same unsigned bundle the user chose to run still
            // extracts — backward compatible with pre-Integrity() bundles.
            var freshInstall = SignedPayloadTocVerifier.Verify(strippedManifest, toc, NoTrust, requireSigned: false);
            Assert.True(freshInstall.IsSuccess, freshInstall.IsFailure ? freshInstall.Error.Message : null);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
