using System.Security.Cryptography;
using System.Text.Json;
using FalkForge;
using FalkForge.Compiler.Bundle.Builders;
using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Engine;
using FalkForge.Engine.Protocol.Bundle;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

namespace FalkForge.Integration.Tests;

/// <summary>
/// End-to-end proof that the embedded elevation companion — a payload the engine executes
/// ELEVATED (SYSTEM for per-machine installs) — cannot be swapped for attacker bytes anywhere
/// along its trust chain, and that a bundle without a companion degrades to per-user cleanly.
///
/// <para>The chain, link by link: companion bytes are verified against the overlay TOC hash at
/// extraction (<see cref="BundleReader"/>); the TOC hash is bound to the ECDSA-signed envelope
/// for signed bundles (<see cref="SignedPayloadTocVerifier"/>, INT006); and the TOC hash must
/// equal the manifest's declared companion hash before the bootstrapper ever wires the extracted
/// file for elevation (<see cref="BootstrapCompanionResolver"/>). Tampering any link fails loud —
/// never a SYSTEM RCE.</para>
/// </summary>
public sealed class ElevationCompanionTrustTests : IDisposable
{
    private static readonly IReadOnlySet<string> NoTrust =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private readonly string _dir;
    private readonly string _msiPath;
    private readonly string _companionPath;
    private readonly byte[] _companionBytes;

    public ElevationCompanionTrustTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"falk-companion-trust-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);

        _msiPath = Path.Combine(_dir, "App.msi");
        File.WriteAllBytes(_msiPath, RandomNumberGenerator.GetBytes(256));

        // A distinctive fake companion (MZ header + random body) embedded via the explicit
        // ElevationCompanionPath, so no published NativeAOT binary is required.
        _companionBytes = new byte[512];
        RandomNumberGenerator.Fill(_companionBytes);
        _companionBytes[0] = (byte)'M';
        _companionBytes[1] = (byte)'Z';
        _companionPath = Path.Combine(_dir, "companion-drop.exe");
        File.WriteAllBytes(_companionPath, _companionBytes);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private string BuildBundle(bool signed, bool omitCompanion = false, string outName = "out")
    {
        var builder = new BundleBuilder()
            .Name("CompanionTrust")
            .Manufacturer("Integration Tests")
            .Version("1.0.0")
            .UseSilentUI()
            .Chain(chain => chain.MsiPackage(_msiPath, pkg => pkg.Id("AppMsi").Version("1.0.0")));
        if (signed)
            builder = builder.Integrity(i => { }); // ephemeral ECDSA key
        if (omitCompanion)
            builder = builder.WithoutElevationCompanion();

        var compiler = new BundleCompiler
        {
            AllowPlaceholderStub = true,
            // Explicit companion path wins over the placeholder's companion-free default,
            // producing a companion-carrying bundle without a published NativeAOT engine.
            ElevationCompanionPath = omitCompanion ? null : _companionPath
        };

        var result = compiler.Compile(builder.Build(), Path.Combine(_dir, outName));
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        return result.Value;
    }

    private static (InstallerManifest Manifest, TocEntry[] Toc) ReadBundle(string bundlePath)
    {
        var content = BundleReader.Extract(bundlePath);
        Assert.True(content.IsSuccess, content.IsFailure ? content.Error.Message : null);
        var manifest = JsonSerializer.Deserialize<InstallerManifest>(content.Value.ManifestJsonBytes!);
        Assert.NotNull(manifest);
        return (manifest!, content.Value.TocEntries);
    }

    private static string HashOf(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    // ── Link 1: bytes ↔ TOC. A flipped byte in the embedded companion is rejected at extraction. ──

    [Fact]
    public void TamperedCompanionBytesInBundle_RejectedAtExtraction_NeverLandsOnDisk()
    {
        var bundlePath = BuildBundle(signed: false);
        var (_, toc) = ReadBundle(bundlePath);
        var entry = Assert.Single(toc, e => e.PackageId == EngineCompanionPayload.PackageId);

        // Attacker flips one byte inside the companion's compressed payload region.
        using (var stream = new FileStream(bundlePath, FileMode.Open, FileAccess.ReadWrite))
        {
            stream.Seek(entry.Offset + entry.CompressedSize / 2, SeekOrigin.Begin);
            var original = stream.ReadByte();
            stream.Seek(-1, SeekOrigin.Current);
            stream.WriteByte((byte)(original ^ 0xFF));
        }

        var extractDir = Path.Combine(_dir, "extract-tampered");
        var extract = BundleReader.ExtractPayloadToFile(
            bundlePath, entry, extractDir, EngineCompanionPayload.PackageId);

        Assert.True(extract.IsFailure, "a tampered companion must fail extraction verification");
        Assert.False(File.Exists(Path.Combine(extractDir, EngineCompanionPayload.PackageId)),
            "no unverified companion bytes may land at the extraction path");
    }

    // ── Link 2: TOC ↔ signed envelope. A recomputed TOC hash over swapped companion bytes is
    //    rejected before extraction on a signed bundle (INT006). ──

    [Fact]
    public void SwappedCompanionWithMatchingTocHash_SignedBundle_RejectedByTocVerifier_Int006()
    {
        // 1. Genuinely signed companion-carrying bundle.
        var bundlePath = BuildBundle(signed: true);
        var (signedManifest, signedToc) = ReadBundle(bundlePath);
        Assert.NotNull(signedManifest.ManifestSignature);
        Assert.NotNull(signedManifest.EngineCompanionSha256);

        // 2. Attacker: swap the companion for their own binary and re-embed with the UNCHANGED
        //    signed manifest but a TOC hash matching the malicious bytes (post-signing overlay
        //    rewrite — the raw byte↔TOC check alone would accept this).
        var evilBytes = (byte[])_companionBytes.Clone();
        evilBytes[100] ^= 0xFF;
        var evilCompanion = Path.Combine(_dir, "evil-companion.exe");
        File.WriteAllBytes(evilCompanion, evilBytes);

        var msiBytes = File.ReadAllBytes(_msiPath);
        var attackerPayloads = new[]
        {
            new PayloadEntry
            {
                PackageId = "AppMsi",
                SourcePath = _msiPath,
                OriginalSize = msiBytes.Length,
                Sha256Hash = HashOf(msiBytes)
            },
            new PayloadEntry
            {
                PackageId = EngineCompanionPayload.PackageId,
                SourcePath = evilCompanion,
                OriginalSize = evilBytes.Length,
                Sha256Hash = HashOf(evilBytes) // TOC hash matches the malicious bytes
            }
        };

        var stubPath = Path.Combine(_dir, "stub.bin");
        File.WriteAllBytes(stubPath, []);
        var attackerBundle = Path.Combine(_dir, "attacker.exe");
        var embed = new PayloadEmbedder().Embed(stubPath, attackerBundle, signedManifest, attackerPayloads);
        Assert.True(embed.IsSuccess, embed.IsFailure ? embed.Error.Message : null);

        // 3. The signed-TOC binding rejects the swap before a byte of the companion is extracted.
        var (attackerManifest, attackerToc) = ReadBundle(attackerBundle);
        var trust = SignedPayloadTocVerifier.Verify(attackerManifest, attackerToc, NoTrust);
        Assert.True(trust.IsFailure, "a swapped companion with a rewritten TOC hash must be rejected");
        Assert.Equal(ErrorKind.IntegrityError, trust.Error.Kind);
        Assert.Contains("INT006", trust.Error.Message, StringComparison.Ordinal);

        // 4. Even if extraction were reached, the bootstrapper's manifest binding ALSO refuses to
        //    wire it (defense in depth; the only gate on unsigned bundles).
        var companionEntry = Array.Find(attackerToc, e => e.PackageId == EngineCompanionPayload.PackageId)!;
        Assert.NotEqual(
            attackerManifest.EngineCompanionSha256!, companionEntry.Sha256Hash, StringComparer.OrdinalIgnoreCase);
        var cacheDir = Path.Combine(_dir, "attacker-cache");
        Directory.CreateDirectory(cacheDir);
        var wire = BootstrapCompanionResolver.Resolve(attackerManifest, attackerToc, cacheDir);
        Assert.True(wire.IsFailure, "the bootstrapper must never wire a companion whose TOC hash disagrees with the manifest");
        Assert.Equal(ErrorKind.SecurityError, wire.Error.Kind);

        // 5. Sanity: the untampered signed bundle passes both gates.
        var cleanTrust = SignedPayloadTocVerifier.Verify(signedManifest, signedToc, NoTrust);
        Assert.True(cleanTrust.IsSuccess, cleanTrust.IsFailure ? cleanTrust.Error.Message : null);
    }

    // ── Link 3: declared ↔ wired. An undeclared companion is never wired; a companion-free
    //    bundle degrades to per-user cleanly. ──

    [Fact]
    public void CompanionCarryingBundle_ResolverBindsExtractedCompanionToManifestDeclaration()
    {
        var bundlePath = BuildBundle(signed: true, outName: "out-wire");
        var (manifest, toc) = ReadBundle(bundlePath);
        var entry = Assert.Single(toc, e => e.PackageId == EngineCompanionPayload.PackageId);

        // Mirror the bootstrapper's extraction loop for the companion payload.
        var cacheDir = Path.Combine(_dir, "wire-cache");
        var extracted = BundleReader.ExtractPayloadToFile(
            bundlePath, entry, cacheDir, EngineCompanionPayload.PackageId);
        Assert.True(extracted.IsSuccess, extracted.IsFailure ? extracted.Error.Message : null);

        var wire = BootstrapCompanionResolver.Resolve(manifest, toc, cacheDir);
        Assert.True(wire.IsSuccess, wire.IsFailure ? wire.Error.Message : null);
        Assert.Equal(extracted.Value, wire.Value.VerifiedPath);
        Assert.Equal(File.ReadAllBytes(_companionPath), File.ReadAllBytes(wire.Value.VerifiedPath!));
    }

    [Fact]
    public void BundleWithoutCompanion_BuildsAndResolvesToNone_PerUserFallback()
    {
        // Old-style / per-user-only authoring: no companion payload, no declaration, no failure —
        // the resolver reports none and the session simply runs without an elevation gateway.
        var bundlePath = BuildBundle(signed: false, omitCompanion: true, outName: "out-none");
        var (manifest, toc) = ReadBundle(bundlePath);

        Assert.Null(manifest.EngineCompanionSha256);
        Assert.DoesNotContain(toc, e => e.PackageId == EngineCompanionPayload.PackageId);

        var wire = BootstrapCompanionResolver.Resolve(manifest, toc, _dir);
        Assert.True(wire.IsSuccess, wire.IsFailure ? wire.Error.Message : null);
        Assert.Null(wire.Value.VerifiedPath);
    }

    [Fact]
    public void StrippedCompanionDeclaration_SmuggledPayloadIsNeverWired()
    {
        // Attacker strips EngineCompanionSha256 from the manifest but leaves the companion payload
        // in the TOC: the resolver must treat the payload as inert — an undeclared SYSTEM binary
        // is never handed to the elevation gateway. (On a signed bundle the apply-time integrity
        // gate additionally fails INT002 because the signed companion entry binds nowhere.)
        var bundlePath = BuildBundle(signed: false, outName: "out-stripped");
        var (manifest, toc) = ReadBundle(bundlePath);
        var stripped = manifest with { EngineCompanionSha256 = null };

        var cacheDir = Path.Combine(_dir, "stripped-cache");
        Directory.CreateDirectory(cacheDir);
        var entry = Assert.Single(toc, e => e.PackageId == EngineCompanionPayload.PackageId);
        var extracted = BundleReader.ExtractPayloadToFile(
            bundlePath, entry, cacheDir, EngineCompanionPayload.PackageId);
        Assert.True(extracted.IsSuccess, extracted.IsFailure ? extracted.Error.Message : null);

        var wire = BootstrapCompanionResolver.Resolve(stripped, toc, cacheDir);
        Assert.True(wire.IsSuccess, wire.IsFailure ? wire.Error.Message : null);
        Assert.Null(wire.Value.VerifiedPath);
    }
}
