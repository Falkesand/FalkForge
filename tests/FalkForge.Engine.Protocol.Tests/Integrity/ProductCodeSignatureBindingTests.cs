using System.Security.Cryptography;
using FalkForge;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Integrity;

/// <summary>
/// Signed-uninstall plumbing: the integrity envelope carries a signed flat allow-set of the MSI product
/// codes the publisher authorizes for elevated uninstall. The set must be inside the signed bytes so a
/// same-user caller cannot add a product code to it (and have the elevated companion uninstall it as SYSTEM)
/// without invalidating the signature.
///
/// <para>These tests encode WHY the binding exists: (1) an EMPTY or omitted set must sign the byte-identical
/// bytes every already-shipped bundle signed (back-compat is non-negotiable in the trust core); (2) a
/// non-empty set signs different bytes than the empty set; (3) a non-empty set signs and verifies round-trip;
/// and (4) every tamper vector against the set — an added or removed product code — is rejected fail-loud
/// (INT001), because the verifier recomputes the signed bytes from the set on the envelope it is checking.</para>
/// </summary>
public sealed class ProductCodeSignatureBindingTests
{
    private const string CodeA = "{11111111-1111-1111-1111-111111111111}";
    private const string CodeB = "{22222222-2222-2222-2222-222222222222}";

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

    // ── Backward compatibility: an empty/omitted set signs byte-identically to before ────────────────────

    [Fact]
    public void SignedBytes_NoProductCodes_ByteIdenticalToLegacyFilesOnly()
    {
        // The hard back-compat property: adding the product-code set MUST NOT change the signed bytes for a
        // bundle that has none. A null OR empty set both reproduce the legacy files-only bytes, so every
        // already-shipped bundle (which has no set) still verifies.
        var files = Files(("A", "AABB"), ("B", "CCDD"));

        var legacy = IntegrityEnvelopeCodec.ComputeSignedBytes(files);
        var nullSet = IntegrityEnvelopeCodec.ComputeSignedBytes(
            files, epoch: 0, revoked: [], externalContainers: null, transformAssociations: null,
            productCodes: null);
        var emptySet = IntegrityEnvelopeCodec.ComputeSignedBytes(
            files, epoch: 0, revoked: [], externalContainers: null, transformAssociations: null,
            productCodes: []);

        Assert.Equal(legacy, nullSet);
        Assert.Equal(legacy, emptySet);
    }

    [Fact]
    public void SignedBytes_TransformBearingButProductCodeFree_ByteIdenticalToPreChange()
    {
        // A transform-map-bearing bundle (but no product-code set) must ALSO be byte-identical: the
        // product-code segment is appended only when the set is present and AFTER the transform segment, so
        // none of the earlier bytes move.
        var files = Files(("AppMsi", "AAAA"));
        var map = new[] { new PackageTransformAssociation { PackageId = "AppMsi", TransformIds = ["T1"] } };

        var preChange = IntegrityEnvelopeCodec.ComputeSignedBytes(
            files, epoch: 0, revoked: [], externalContainers: null, transformAssociations: map);
        var withNullSet = IntegrityEnvelopeCodec.ComputeSignedBytes(
            files, epoch: 0, revoked: [], externalContainers: null, transformAssociations: map,
            productCodes: null);

        Assert.Equal(preChange, withNullSet);
    }

    [Fact]
    public void ExistingSignedEnvelope_NoSet_StillVerifies_AndOmitsField()
    {
        // End-to-end back-compat: an envelope produced through the normal Sign path (no set) verifies exactly
        // as before, and carries no productCodes field.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var files = Files(("App", "AAAA"));
        var envelope = IntegrityEnvelopeCodec.Sign(files, key);

        Assert.Null(envelope.ProductCodes); // set-free: field omitted (null), not empty
        Assert.True(IntegrityEnvelopeCodec.VerifyTrusted(envelope, TrustSet(Fingerprint(key))).IsSuccess);
    }

    // ── A non-empty set changes the signed bytes ─────────────────────────────────────────────────────────

    [Fact]
    public void SignedBytes_NonEmptySet_DiffersFromEmptySet()
    {
        var files = Files(("App", "AAAA"));

        var emptyBytes = IntegrityEnvelopeCodec.ComputeSignedBytes(
            files, epoch: 0, revoked: [], externalContainers: null, transformAssociations: null,
            productCodes: []);
        var setBytes = IntegrityEnvelopeCodec.ComputeSignedBytes(
            files, epoch: 0, revoked: [], externalContainers: null, transformAssociations: null,
            productCodes: [CodeA]);

        Assert.NotEqual(emptyBytes, setBytes);
    }

    // ── Injectivity + order independence of the canonical form ───────────────────────────────────────────

    [Fact]
    public void Canonicalize_IsInjective_AcrossDelimiterCraftedValues()
    {
        // Length-prefixing (count; then len:value;) must make the encoding injective: an attacker cannot craft
        // one value containing the ';'/':' separators to make two DISTINCT sets canonicalize the same.
        var a = new[] { "T1", "T2" };
        var b = new[] { "T1;1:T2" }; // one crafted value vs two real ones

        Assert.NotEqual(
            IntegrityEnvelopeCodec.CanonicalizeProductCodes(a),
            IntegrityEnvelopeCodec.CanonicalizeProductCodes(b));
    }

    [Fact]
    public void Canonicalize_IsOrderIndependent()
    {
        // The set is an allow-list, not an ordering, so a benign reorder must not change the canonical form.
        var forward = new[] { CodeA, CodeB };
        var reversed = new[] { CodeB, CodeA };

        Assert.Equal(
            IntegrityEnvelopeCodec.CanonicalizeProductCodes(forward),
            IntegrityEnvelopeCodec.CanonicalizeProductCodes(reversed));
    }

    [Fact]
    public void Canonicalize_EmptyOrNull_YieldsEmptyString()
    {
        Assert.Equal(string.Empty, IntegrityEnvelopeCodec.CanonicalizeProductCodes(null));
        Assert.Equal(string.Empty, IntegrityEnvelopeCodec.CanonicalizeProductCodes([]));
    }

    // ── Round-trip: a set-bearing envelope signs and verifies ────────────────────────────────────────────

    [Fact]
    public void SignedEnvelope_WithSet_SameSet_Verifies()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var files = Files(("AppMsi", "AAAA"));

        var envelope = IntegrityEnvelopeCodec.Sign(
            files, [key], epoch: 0, revoked: [], externalContainers: null, transformAssociations: null,
            productCodes: [CodeA, CodeB]);

        Assert.NotNull(envelope.ProductCodes);
        Assert.Equal(2, envelope.ProductCodes.Count);
        Assert.True(IntegrityEnvelopeCodec.VerifyTrusted(envelope, TrustSet(Fingerprint(key))).IsSuccess);
    }

    // ── Tamper vectors: mutating the set on the envelope breaks the signature (INT001) ───────────────────

    [Fact]
    public void TamperedSet_AddedProductCode_FailsInt001()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var files = Files(("AppMsi", "AAAA"));
        var envelope = IntegrityEnvelopeCodec.Sign(
            files, [key], epoch: 0, revoked: [], externalContainers: null, transformAssociations: null,
            productCodes: [CodeA]);

        // Add a product code the publisher never signed for. The verifier recomputes the signed bytes from
        // the altered set, so the signature no longer verifies — this is the exact same-user escalation the
        // binding stops.
        envelope.ProductCodes = [CodeA, CodeB];

        var result = IntegrityEnvelopeCodec.MatchTrustedSignature(envelope, TrustSet(Fingerprint(key)));

        Assert.True(result.IsFailure, "an added product code must break the signature");
        Assert.Equal(ErrorKind.IntegrityError, result.Error.Kind);
        Assert.Contains("INT001", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TamperedSet_RemovedProductCode_FailsInt001()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var files = Files(("AppMsi", "AAAA"));
        var envelope = IntegrityEnvelopeCodec.Sign(
            files, [key], epoch: 0, revoked: [], externalContainers: null, transformAssociations: null,
            productCodes: [CodeA, CodeB]);

        envelope.ProductCodes = [CodeA];

        var result = IntegrityEnvelopeCodec.MatchTrustedSignature(envelope, TrustSet(Fingerprint(key)));

        Assert.True(result.IsFailure, "a stripped product code must break the signature");
        Assert.Equal(ErrorKind.IntegrityError, result.Error.Kind);
        Assert.Contains("INT001", result.Error.Message, StringComparison.Ordinal);
    }
}
