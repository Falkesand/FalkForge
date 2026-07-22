using System.Security.Cryptography;
using FalkForge;
using FalkForge.Engine.Protocol.Bundle;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Integrity;

/// <summary>
/// A6 SSRF hardening: the external-container set (download URL + whole-file hash + package membership)
/// must be bound into the ECDSA-signed message. Before this, only per-payload hashes were signed, so a
/// tampered bundle could repoint <see cref="ExternalContainerInfo.DownloadUrl"/> at an internal host
/// (SSRF/DoS) without invalidating the signature — the payloads still bound to the signed set, but the
/// URL the engine fetched from was unauthenticated.
///
/// <para>These tests encode WHY the binding exists: (1) a bundle with NO external containers must sign
/// the byte-identical files-only message every already-shipped bundle signed (back-compat is
/// non-negotiable in the trust core); (2) a bundle WITH containers signs and verifies round-trip; and
/// (3) every tamper vector against the declared container set — URL, hash, membership, added container,
/// removed container — is rejected fail-loud. Editing the envelope's own signed copy instead breaks the
/// signature (INT001), so there is no third path that verifies.</para>
/// </summary>
public sealed class ExternalContainerSignatureBindingTests
{
    private const string HashA = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string HashB = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

    private static string Fingerprint(ECDsa key)
        => Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo()));

    private static IReadOnlySet<string> TrustSet(params string[] fps)
        => new HashSet<string>(fps, StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<ManifestFileEntry> Files(params (string name, string sha)[] items)
    {
        var list = new List<ManifestFileEntry>(items.Length);
        foreach (var (name, sha) in items)
            list.Add(new ManifestFileEntry { Name = name, Sha256 = sha });
        return list;
    }

    private static ExternalContainerInfo Container(
        string id, string url, string sha, string fileName, params string[] pkgs) => new()
        {
            Id = id,
            DownloadUrl = url,
            Sha256 = sha,
            FileName = fileName,
            PackageIds = pkgs
        };

    private static readonly ExternalContainerInfo Extras =
        Container("extras", "https://cdn.example.com/extras.ffcontainer", HashB, "extras.ffcontainer", "Extras");

    private static TocEntry FullEntry(string id, string tocHash) => new()
    {
        PackageId = id,
        Offset = 0,
        CompressedSize = 1,
        OriginalSize = 1,
        Sha256Hash = tocHash
    };

    /// <summary>
    /// Builds a signed manifest whose envelope binds <paramref name="containers"/> (their member package
    /// ids are also in the signed file set, as a real build produces), with the manifest's declared
    /// <see cref="InstallerManifest.ExternalContainers"/> set to the same array — so the two can then be
    /// put in deliberate disagreement by the tamper tests.
    /// </summary>
    private static InstallerManifest SignedWithContainers(ECDsa key, params ExternalContainerInfo[] containers)
    {
        var files = new List<ManifestFileEntry> { new() { Name = "AppMsi", Sha256 = HashA } };
        foreach (var c in containers)
            foreach (var pid in c.PackageIds)
                files.Add(new ManifestFileEntry { Name = pid, Sha256 = HashB });

        var envelope = IntegrityEnvelopeCodec.Sign(files, new[] { key }, epoch: 0, revoked: [], containers);

        return new InstallerManifest
        {
            Name = "T",
            Manufacturer = "M",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerMachine,
            Packages =
            [
                new PackageInfo
                {
                    Id = "AppMsi",
                    Type = PackageType.MsiPackage,
                    DisplayName = "AppMsi",
                    SourcePath = "AppMsi.msi",
                    Sha256Hash = HashA
                }
            ],
            ExternalContainers = containers,
            ManifestSignature = IntegrityEnvelopeCodec.Serialize(envelope)
        };
    }

    // The embedded TOC the verifier is given (INT013 binds the manifest's container set regardless of TOC).
    private static TocEntry[] AppToc() => [FullEntry("AppMsi", HashA)];

    // ── Backward compatibility: a container-free bundle signs byte-identically to before ─────────────

    [Fact]
    public void SignedBytes_NoExternalContainers_ByteIdenticalToLegacyFilesOnly()
    {
        // The hard back-compat property: adding external-container support MUST NOT change the signed bytes
        // for a bundle that has none. A null OR empty container set both reproduce the legacy files-only
        // bytes, so every already-shipped bundle (which has no external containers) still verifies.
        var files = Files(("A", "AABB"), ("B", "CCDD"));

        var legacy = IntegrityEnvelopeCodec.ComputeSignedBytes(files);
        var epochAware = IntegrityEnvelopeCodec.ComputeSignedBytes(files, epoch: 0, revoked: []);
        var nullContainers = IntegrityEnvelopeCodec.ComputeSignedBytes(files, epoch: 0, revoked: [], externalContainers: null);
        var emptyContainers = IntegrityEnvelopeCodec.ComputeSignedBytes(files, epoch: 0, revoked: [], externalContainers: []);

        Assert.Equal(legacy, epochAware);
        Assert.Equal(legacy, nullContainers);
        Assert.Equal(legacy, emptyContainers);
    }

    [Fact]
    public void SignedBytes_EpochBearingContainerFree_ByteIdenticalToPreHardening()
    {
        // An existing epoch/revocation-bearing bundle (but no external containers) must ALSO be byte-identical:
        // the container segment is appended only when containers are present, so the epoch/revocation bytes
        // are untouched. This guards against silently re-framing the already-shipped epoch bundles.
        var files = Files(("App", "AAAA"));

        var preHardening = IntegrityEnvelopeCodec.ComputeSignedBytes(files, epoch: 7, revoked: new[] { "DEADBEEF" });
        var withNullContainers = IntegrityEnvelopeCodec.ComputeSignedBytes(
            files, epoch: 7, revoked: new[] { "DEADBEEF" }, externalContainers: null);

        Assert.Equal(preHardening, withNullContainers);
    }

    [Fact]
    public void ExistingContainerFreeSignedEnvelope_StillVerifiesUnchanged()
    {
        // End-to-end back-compat: a container-free envelope produced through the normal Sign path verifies
        // exactly as before — the envelope carries no externalContainers field and the binding is a no-op.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var files = Files(("App", "AAAA"));
        var envelope = IntegrityEnvelopeCodec.Sign(files, key);

        Assert.Null(envelope.ExternalContainers); // container-free: field omitted (null), not empty
        Assert.True(IntegrityEnvelopeCodec.VerifyTrusted(envelope, TrustSet(Fingerprint(key))).IsSuccess);
    }

    // ── Injectivity: distinct container sets produce distinct signed bytes ───────────────────────────

    [Fact]
    public void SignedBytes_DifferentDownloadUrl_ProducesDifferentSignedBytes()
    {
        // The whole point: the URL is inside the signed message. Two sets differing only by DownloadUrl
        // must sign different bytes, so a URL repoint cannot survive the signature.
        var files = Files(("App", "AAAA"));
        var good = new[] { Container("c", "https://cdn.example.com/c.ffcontainer", HashA, "c.ffcontainer", "P") };
        var evil = new[] { Container("c", "https://169.254.169.254/c.ffcontainer", HashA, "c.ffcontainer", "P") };

        var goodBytes = IntegrityEnvelopeCodec.ComputeSignedBytes(files, 0, [], good);
        var evilBytes = IntegrityEnvelopeCodec.ComputeSignedBytes(files, 0, [], evil);

        Assert.NotEqual(goodBytes, evilBytes);
    }

    [Fact]
    public void Canonicalize_IsInjective_AcrossDelimiterCraftedFields()
    {
        // Length-prefixing (len:value;) must make the encoding injective: an attacker cannot craft a field
        // value containing the ';'/':' separators to make two DISTINCT container sets canonicalize the same.
        var a = new[] { Container("c", "u", "h", "f", "P1", "P2") };
        var b = new[] { Container("c", "u", "h", "f", "P1;1:P2") }; // one crafted pkg id vs two real ones

        Assert.NotEqual(
            IntegrityEnvelopeCodec.CanonicalizeExternalContainers(a),
            IntegrityEnvelopeCodec.CanonicalizeExternalContainers(b));
    }

    [Fact]
    public void Canonicalize_IsOrderIndependent_ByContainerId()
    {
        // Reordering the download list is benign (same URLs contacted), so the canonical form is order
        // independent — a legitimately reordered manifest must not fail the binding.
        var forward = new[]
        {
            Container("a", "ua", "ha", "fa", "PA"),
            Container("b", "ub", "hb", "fb", "PB")
        };
        var reversed = new[] { forward[1], forward[0] };

        Assert.Equal(
            IntegrityEnvelopeCodec.CanonicalizeExternalContainers(forward),
            IntegrityEnvelopeCodec.CanonicalizeExternalContainers(reversed));
    }

    // ── Round-trip: a container-bearing bundle signs and verifies ────────────────────────────────────

    [Fact]
    public void SignedBundle_WithContainers_UntamperedManifest_Verifies()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = SignedWithContainers(key, Extras);

        // The envelope carries the signed container copy…
        var envelope = IntegrityEnvelopeCodec.Parse(manifest.ManifestSignature!)!;
        Assert.NotNull(envelope.ExternalContainers);
        Assert.Single(envelope.ExternalContainers!);

        // …and the declared set matches it, so verification passes.
        var result = SignedPayloadTocVerifier.Verify(manifest, AppToc(), TrustSet(Fingerprint(key)));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    // ── Tamper vectors: every edit to the declared container set is rejected (INT013) ────────────────

    [Fact]
    public void TamperedDownloadUrl_Rejected_Int013()
    {
        // The residual attack: repoint the download URL at an internal host. The envelope's signed copy is
        // untouched (signature still valid), but the manifest the acquirer reads now disagrees — INT013.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = SignedWithContainers(key, Extras);

        var tampered = manifest with
        {
            ExternalContainers =
            [
                Container(Extras.Id, "https://169.254.169.254/extras.ffcontainer", Extras.Sha256, Extras.FileName, "Extras")
            ]
        };

        var result = SignedPayloadTocVerifier.Verify(tampered, AppToc(), TrustSet(Fingerprint(key)));

        Assert.True(result.IsFailure, "a repointed container DownloadUrl on a signed bundle must be rejected");
        Assert.Equal(ErrorKind.IntegrityError, result.Error.Kind);
        Assert.Contains("INT013", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TamperedWholeFileHash_Rejected_Int013()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = SignedWithContainers(key, Extras);

        var tampered = manifest with
        {
            ExternalContainers = [Container(Extras.Id, Extras.DownloadUrl, HashA, Extras.FileName, "Extras")]
        };

        var result = SignedPayloadTocVerifier.Verify(tampered, AppToc(), TrustSet(Fingerprint(key)));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.IntegrityError, result.Error.Kind);
        Assert.Contains("INT013", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TamperedMembership_Rejected_Int013()
    {
        // Rewriting the declared PackageIds (to smuggle a different payload set) is tamper on the signed set.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = SignedWithContainers(key, Extras);

        var tampered = manifest with
        {
            ExternalContainers = [Container(Extras.Id, Extras.DownloadUrl, Extras.Sha256, Extras.FileName, "Extras", "Injected")]
        };

        var result = SignedPayloadTocVerifier.Verify(tampered, AppToc(), TrustSet(Fingerprint(key)));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.IntegrityError, result.Error.Kind);
        Assert.Contains("INT013", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddedContainer_Rejected_Int013()
    {
        // An attacker appends a second, attacker-hosted container the publisher never signed.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = SignedWithContainers(key, Extras);

        var tampered = manifest with
        {
            ExternalContainers =
            [
                Extras,
                Container("evil", "https://evil.example.com/x.ffcontainer", HashA, "x.ffcontainer", "X")
            ]
        };

        var result = SignedPayloadTocVerifier.Verify(tampered, AppToc(), TrustSet(Fingerprint(key)));

        Assert.True(result.IsFailure, "an added, unsigned external container must be rejected");
        Assert.Equal(ErrorKind.IntegrityError, result.Error.Kind);
        Assert.Contains("INT013", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RemovedContainer_Rejected_Int013()
    {
        // Stripping a signed container from the manifest is tamper too (e.g. to drop a mitigating payload).
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = SignedWithContainers(key, Extras);

        var tampered = manifest with { ExternalContainers = [] };

        var result = SignedPayloadTocVerifier.Verify(tampered, AppToc(), TrustSet(Fingerprint(key)));

        Assert.True(result.IsFailure, "a stripped external-container declaration on a signed bundle must be rejected");
        Assert.Equal(ErrorKind.IntegrityError, result.Error.Kind);
        Assert.Contains("INT013", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TamperingBothManifestAndEnvelopeCopies_BreaksSignature_Int001()
    {
        // The only other path: an attacker edits the envelope's OWN signed container copy to match the evil
        // manifest. But that copy is folded into the signed bytes, so the signature no longer verifies —
        // there is no way to repoint the URL that survives verification.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = SignedWithContainers(key, Extras);

        const string evilUrl = "https://169.254.169.254/extras.ffcontainer";
        var tampered = manifest with
        {
            // Edit the envelope JSON's container URL AND the declared set to agree on the evil URL.
            ManifestSignature = manifest.ManifestSignature!.Replace(Extras.DownloadUrl, evilUrl, StringComparison.Ordinal),
            ExternalContainers = [Container(Extras.Id, evilUrl, Extras.Sha256, Extras.FileName, "Extras")]
        };

        var result = SignedPayloadTocVerifier.Verify(tampered, AppToc(), TrustSet(Fingerprint(key)));

        Assert.True(result.IsFailure, "editing the envelope's signed container copy must break the signature");
        Assert.Equal(ErrorKind.IntegrityError, result.Error.Kind);
        Assert.Contains("INT001", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnsignedBundle_WithContainers_PassesThrough_NoSignedSetToBind()
    {
        // Unsigned bundles have no signed set to bind the declaration to — behavior unchanged (the acquirer's
        // whole-file + per-payload TOC hash checks still apply). The binding is a signed-bundle guarantee.
        var manifest = new InstallerManifest
        {
            Name = "T",
            Manufacturer = "M",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerMachine,
            Packages = [],
            ExternalContainers = [Extras],
            ManifestSignature = null
        };

        var result = SignedPayloadTocVerifier.Verify(manifest, [], NoTrust);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    private static readonly IReadOnlySet<string> NoTrust = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}
