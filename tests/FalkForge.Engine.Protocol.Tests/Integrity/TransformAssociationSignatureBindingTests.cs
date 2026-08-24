using System.Security.Cryptography;
using FalkForge;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Integrity;

/// <summary>
/// Signed-transform plumbing: the integrity envelope carries a signed package-to-transform
/// association map (which signed transforms a package may have applied). The map must be inside the
/// signed bytes so an attacker cannot re-associate a signed transform onto a different package, add
/// one, or strip one, without invalidating the signature.
///
/// <para>These tests encode WHY the binding exists: (1) an EMPTY or omitted map must sign the
/// byte-identical bytes every already-shipped bundle signed (back-compat is non-negotiable in the
/// trust core); (2) a non-empty map signs different bytes than the empty map; (3) a non-empty map
/// signs and verifies round-trip; and (4) every tamper vector against the map — added, removed, or
/// moved-to-another-package transform id — is rejected fail-loud (INT001), because the verifier
/// recomputes the signed bytes from the map on the envelope it is checking.</para>
///
/// <para>This part is plumbing only: production always signs an empty map (the producer arrives in a
/// later part), so these tests are its sole exercise.</para>
/// </summary>
public sealed class TransformAssociationSignatureBindingTests
{
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

    private static PackageTransformAssociation Assoc(string packageId, params string[] transformIds)
        => new() { PackageId = packageId, TransformIds = transformIds };

    // ── Backward compatibility: an empty/omitted map signs byte-identically to before ────────────────

    [Fact]
    public void SignedBytes_NoTransformAssociations_ByteIdenticalToLegacyFilesOnly()
    {
        // The hard back-compat property: adding the association map MUST NOT change the signed bytes for
        // a bundle that has none. A null OR empty map both reproduce the legacy files-only bytes, so every
        // already-shipped bundle (which has no map) still verifies.
        var files = Files(("A", "AABB"), ("B", "CCDD"));

        var legacy = IntegrityEnvelopeCodec.ComputeSignedBytes(files);
        var nullMap = IntegrityEnvelopeCodec.ComputeSignedBytes(
            files, epoch: 0, revoked: [], externalContainers: null, transformAssociations: null);
        var emptyMap = IntegrityEnvelopeCodec.ComputeSignedBytes(
            files, epoch: 0, revoked: [], externalContainers: null, transformAssociations: []);

        Assert.Equal(legacy, nullMap);
        Assert.Equal(legacy, emptyMap);
    }

    [Fact]
    public void SignedBytes_ContainerAndEpochBearingButTransformFree_ByteIdenticalToPreChange()
    {
        // An existing epoch/revocation/container-bearing bundle (but no transform map) must ALSO be
        // byte-identical: the transforms segment is appended only when the map is present and AFTER the
        // container segment, so none of the earlier bytes move.
        var files = Files(("App", "AAAA"));
        var containers = new[]
        {
            new ExternalContainerInfo
            {
                Id = "c",
                DownloadUrl = "https://cdn.example.com/c.ffcontainer",
                Sha256 = "AAAA",
                FileName = "c.ffcontainer",
                PackageIds = ["P"]
            }
        };

        var preChange = IntegrityEnvelopeCodec.ComputeSignedBytes(
            files, epoch: 7, revoked: new[] { "DEADBEEF" }, externalContainers: containers);
        var withNullMap = IntegrityEnvelopeCodec.ComputeSignedBytes(
            files, epoch: 7, revoked: new[] { "DEADBEEF" }, externalContainers: containers, transformAssociations: null);

        Assert.Equal(preChange, withNullMap);
    }

    [Fact]
    public void ExistingSignedEnvelope_EmptyMap_StillVerifies()
    {
        // End-to-end back-compat: an envelope produced through the normal Sign path (no map) verifies
        // exactly as before, and carries no transformAssociations field.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var files = Files(("App", "AAAA"));
        var envelope = IntegrityEnvelopeCodec.Sign(files, key);

        Assert.Null(envelope.TransformAssociations); // map-free: field omitted (null), not empty
        Assert.True(IntegrityEnvelopeCodec.VerifyTrusted(envelope, TrustSet(Fingerprint(key))).IsSuccess);
    }

    [Fact]
    public void SignedEnvelope_ExplicitlyEmptyMap_Verifies()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var files = Files(("App", "AAAA"));
        var envelope = IntegrityEnvelopeCodec.Sign(
            files, new[] { key }, epoch: 0, revoked: [], externalContainers: null, transformAssociations: []);

        Assert.Null(envelope.TransformAssociations); // empty normalized to null on the wire
        Assert.True(IntegrityEnvelopeCodec.VerifyTrusted(envelope, TrustSet(Fingerprint(key))).IsSuccess);
    }

    // ── A non-empty map changes the signed bytes ─────────────────────────────────────────────────────

    [Fact]
    public void SignedBytes_NonEmptyMap_DiffersFromEmptyMap()
    {
        var files = Files(("App", "AAAA"));

        var emptyBytes = IntegrityEnvelopeCodec.ComputeSignedBytes(
            files, epoch: 0, revoked: [], externalContainers: null, transformAssociations: []);
        var mapBytes = IntegrityEnvelopeCodec.ComputeSignedBytes(
            files, epoch: 0, revoked: [], externalContainers: null,
            transformAssociations: new[] { Assoc("App", "T1") });

        Assert.NotEqual(emptyBytes, mapBytes);
    }

    // ── Injectivity + order independence of the canonical form ───────────────────────────────────────

    [Fact]
    public void Canonicalize_IsInjective_AcrossDelimiterCraftedFields()
    {
        // Length-prefixing (len:value;) must make the encoding injective: an attacker cannot craft a
        // transform id containing the ';'/':' separators to make two DISTINCT maps canonicalize the same.
        var a = new[] { Assoc("P", "T1", "T2") };
        var b = new[] { Assoc("P", "T1;1:T2") }; // one crafted id vs two real ones

        Assert.NotEqual(
            IntegrityEnvelopeCodec.CanonicalizeTransformAssociations(a),
            IntegrityEnvelopeCodec.CanonicalizeTransformAssociations(b));
    }

    [Fact]
    public void Canonicalize_IsOrderIndependent_ByPackageId()
    {
        // Reordering the association list (an allow-list, not an application order) is benign, so the
        // canonical form is order independent — a legitimately reordered map must not fail the binding.
        var forward = new[] { Assoc("a", "TA"), Assoc("b", "TB") };
        var reversed = new[] { forward[1], forward[0] };

        Assert.Equal(
            IntegrityEnvelopeCodec.CanonicalizeTransformAssociations(forward),
            IntegrityEnvelopeCodec.CanonicalizeTransformAssociations(reversed));
    }

    [Fact]
    public void Canonicalize_EmptyOrNull_YieldsEmptyString()
    {
        Assert.Equal(string.Empty, IntegrityEnvelopeCodec.CanonicalizeTransformAssociations(null));
        Assert.Equal(string.Empty, IntegrityEnvelopeCodec.CanonicalizeTransformAssociations([]));
    }

    // ── Round-trip: a map-bearing envelope signs and verifies ────────────────────────────────────────

    [Fact]
    public void SignedEnvelope_WithMap_SameMap_Verifies()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var files = Files(("AppMsi", "AAAA"));
        var map = new[] { Assoc("AppMsi", "PatchTransform", "LangTransform") };

        var envelope = IntegrityEnvelopeCodec.Sign(
            files, new[] { key }, epoch: 0, revoked: [], externalContainers: null, transformAssociations: map);

        Assert.NotNull(envelope.TransformAssociations);
        Assert.Single(envelope.TransformAssociations);
        Assert.True(IntegrityEnvelopeCodec.VerifyTrusted(envelope, TrustSet(Fingerprint(key))).IsSuccess);
    }

    // ── Tamper vectors: mutating the map on the envelope breaks the signature (INT001) ───────────────

    [Fact]
    public void TamperedMap_AddedTransform_FailsInt001()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var files = Files(("AppMsi", "AAAA"));
        var envelope = IntegrityEnvelopeCodec.Sign(
            files, new[] { key }, epoch: 0, revoked: [], externalContainers: null,
            transformAssociations: new[] { Assoc("AppMsi", "T1") });

        // Add an unsigned transform id to the package. The verifier recomputes the signed bytes from the
        // altered map, so the signature no longer verifies.
        envelope.TransformAssociations = new[] { Assoc("AppMsi", "T1", "Injected") };

        var result = IntegrityEnvelopeCodec.MatchTrustedSignature(envelope, TrustSet(Fingerprint(key)));

        Assert.True(result.IsFailure, "an added transform id must break the signature");
        Assert.Equal(ErrorKind.IntegrityError, result.Error.Kind);
        Assert.Contains("INT001", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TamperedMap_RemovedTransform_FailsInt001()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var files = Files(("AppMsi", "AAAA"));
        var envelope = IntegrityEnvelopeCodec.Sign(
            files, new[] { key }, epoch: 0, revoked: [], externalContainers: null,
            transformAssociations: new[] { Assoc("AppMsi", "T1", "T2") });

        envelope.TransformAssociations = new[] { Assoc("AppMsi", "T1") };

        var result = IntegrityEnvelopeCodec.MatchTrustedSignature(envelope, TrustSet(Fingerprint(key)));

        Assert.True(result.IsFailure, "a stripped transform id must break the signature");
        Assert.Equal(ErrorKind.IntegrityError, result.Error.Kind);
        Assert.Contains("INT001", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TamperedMap_TransformMovedToAnotherPackage_FailsInt001()
    {
        // The core threat: re-associate a signed transform onto a different package. The packageId is part
        // of the canonical form, so moving T1 from PkgA to PkgB changes the signed bytes and fails.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var files = Files(("PkgA", "AAAA"), ("PkgB", "BBBB"));
        var envelope = IntegrityEnvelopeCodec.Sign(
            files, new[] { key }, epoch: 0, revoked: [], externalContainers: null,
            transformAssociations: new[] { Assoc("PkgA", "T1") });

        envelope.TransformAssociations = new[] { Assoc("PkgB", "T1") };

        var result = IntegrityEnvelopeCodec.MatchTrustedSignature(envelope, TrustSet(Fingerprint(key)));

        Assert.True(result.IsFailure, "moving a signed transform to another package must break the signature");
        Assert.Equal(ErrorKind.IntegrityError, result.Error.Kind);
        Assert.Contains("INT001", result.Error.Message, StringComparison.Ordinal);
    }
}
