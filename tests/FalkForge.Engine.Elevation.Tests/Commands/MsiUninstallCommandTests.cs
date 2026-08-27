namespace FalkForge.Engine.Elevation.Tests.Commands;

using System.Security.Cryptography;
using FalkForge.Engine.Elevation.Commands;
using FalkForge.Engine.Elevation.Tests.Mocks;
using Xunit;

/// <summary>
/// The elevated companion must not uninstall a product it cannot trace to a publisher key it holds itself.
/// Before this change the companion uninstalled whatever product code the caller named, as SYSTEM, with only
/// a GUID-format check — a same-user caller could remove any installed product. These tests drive
/// <see cref="MsiUninstallCommand.Execute"/> with the versioned signed-manifest wire and assert that the
/// require-signed gate plus the signed product-code allow-set refuse every request that cannot establish the
/// publisher authorized removing THIS product; the mock installer is never called on any rejection. The
/// trusted-key set is injected so the gate runs with a known publisher rather than the (empty) baked set of a
/// framework build.
/// </summary>
public sealed class MsiUninstallCommandTests
{
    private const string AuthorizedProductCode = "{12345678-1234-1234-1234-123456789012}";
    private const string OtherProductCode = "{99999999-9999-9999-9999-999999999999}";

    private readonly MockMsiApi _msi = new();

    private MsiUninstallCommand Command(IReadOnlySet<string> trusted) =>
        new(_msi, trusted, SignedManifestPayload.NoRoles, SignedManifestPayload.NoPqCompanions);

    // ── The core authorization gap: only a signed, covered product code may be uninstalled ───────────────

    [Fact]
    public void Execute_ProductCodeInSignedSet_Uninstalls()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifestJson = SignedManifestPayload.UninstallManifestJson([AuthorizedProductCode], key);
        var payload = SignedManifestPayload.BuildUninstall(AuthorizedProductCode, manifestJson);

        var result = Command(SignedManifestPayload.TrustedSet(key)).Execute(payload);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, _msi.ConfigureProductCallCount);
        Assert.Equal(AuthorizedProductCode, _msi.LastProductCode);
        Assert.Equal(0, _msi.LastInstallLevel);
        Assert.Equal(2, _msi.LastInstallState);
    }

    [Fact]
    public void Execute_ProductCodeNotInSignedSet_Rejected_NeverUninstalls()
    {
        // The publisher signed for a different product code than the caller named. Refuse — a same-user
        // caller cannot add its target to the signed set without breaking the signature.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifestJson = SignedManifestPayload.UninstallManifestJson([OtherProductCode], key);
        var payload = SignedManifestPayload.BuildUninstall(AuthorizedProductCode, manifestJson);

        var result = Command(SignedManifestPayload.TrustedSet(key)).Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("signed allow-set", result.Error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, _msi.ConfigureProductCallCount);
    }

    [Fact]
    public void Execute_BundleSignedNoProductCodes_Rejected_NeverUninstalls()
    {
        // A signed bundle that declared no product code authorizes no uninstall at all.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifestJson = SignedManifestPayload.UninstallManifestJson([], key);
        var payload = SignedManifestPayload.BuildUninstall(AuthorizedProductCode, manifestJson);

        var result = Command(SignedManifestPayload.TrustedSet(key)).Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Equal(0, _msi.ConfigureProductCallCount);
    }

    // ── Publisher-gate failures: empty baked set / unsigned / untrusted key ──────────────────────────────

    [Fact]
    public void Execute_EmptyBakedSet_Rejected_NeverUninstalls()
    {
        // An empty baked set on the require-signed path fails closed — a required signature with no
        // trust anchor cannot establish authorship. The refusal used to be the generic INT009 text; it
        // now names the publisher-key remedy instead, but the decision underneath is the same:
        // SecurityError, and the MSI API is never called.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifestJson = SignedManifestPayload.UninstallManifestJson([AuthorizedProductCode], key);
        var payload = SignedManifestPayload.BuildUninstall(AuthorizedProductCode, manifestJson);

        var result = Command(new HashSet<string>(StringComparer.OrdinalIgnoreCase)).Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("FalkForgeTrustedKey", result.Error.Message, StringComparison.Ordinal);
        Assert.Equal(0, _msi.ConfigureProductCallCount);
    }

    [Fact]
    public void Execute_UnsignedManifest_Rejected_NeverUninstalls()
    {
        // A manifest with no signature envelope on the require-signed path is INT007.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifestJson = SignedManifestPayload.UnsignedUninstallManifestJson();
        var payload = SignedManifestPayload.BuildUninstall(AuthorizedProductCode, manifestJson);

        var result = Command(SignedManifestPayload.TrustedSet(key)).Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("INT007", result.Error.Message, StringComparison.Ordinal);
        Assert.Equal(0, _msi.ConfigureProductCallCount);
    }

    [Fact]
    public void Execute_SignedByUntrustedKey_Rejected_NeverUninstalls()
    {
        // Signed by the publisher, but the baked set trusts only a stranger. The gate runs the quorum path
        // and collects no trusted signature (INT010) — refused for lack of an anchored publisher.
        using var publisher = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var stranger = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifestJson = SignedManifestPayload.UninstallManifestJson([AuthorizedProductCode], publisher);
        var payload = SignedManifestPayload.BuildUninstall(AuthorizedProductCode, manifestJson);

        var result = Command(SignedManifestPayload.TrustedSet(stranger)).Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("INT010", result.Error.Message, StringComparison.Ordinal);
        Assert.Equal(0, _msi.ConfigureProductCallCount);
    }

    [Fact]
    public void Execute_TamperedProductCodeSet_Rejected_NeverUninstalls()
    {
        // Signed for the OTHER code, then the envelope's product-code set is overwritten to include the
        // caller's target. The gate recomputes the signed bytes from the altered set, so the signature no
        // longer verifies — the caller cannot widen the signed allow-set. At the gate (a role-bearing baked
        // set runs the quorum path) a signature that no longer validates collects zero trusted signatures, so
        // the Install quorum is unsatisfied (INT010); the raw codec-level break surfaces as INT001 and is
        // covered by ProductCodeSignatureBindingTests. Either way the tamper is caught before any uninstall.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifestJson = SignedManifestPayload.TamperedUninstallManifestJson(
            signedCodes: [OtherProductCode], tamperedCodes: [AuthorizedProductCode], signingKey: key);
        var payload = SignedManifestPayload.BuildUninstall(AuthorizedProductCode, manifestJson);

        var result = Command(SignedManifestPayload.TrustedSet(key)).Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("INT010", result.Error.Message, StringComparison.Ordinal);
        Assert.Equal(0, _msi.ConfigureProductCallCount);
    }

    // ── Fail-closed wire: the old bare-product-code format is refused, no fallback ────────────────────────

    [Fact]
    public void Execute_OldBareProductCodeFormat_Rejected_NeverUninstalls()
    {
        // A pre-change payload (just the product code, no magic, no manifest) sent to the new companion must
        // be refused outright — there is no fallback to the old parse, so a same-user caller cannot skip the
        // signed-manifest requirement by speaking the old wire.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var payload = SignedManifestPayload.BuildOldFormatUninstall(AuthorizedProductCode);

        var result = Command(SignedManifestPayload.TrustedSet(key)).Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Equal(0, _msi.ConfigureProductCallCount);
    }

    [Fact]
    public void Execute_NoManifest_Rejected_NeverUninstalls()
    {
        // Correct magic + product code but an empty manifest string: refused, never a legacy allow-through.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var payload = SignedManifestPayload.BuildUninstall(AuthorizedProductCode, manifestJson: string.Empty);

        var result = Command(SignedManifestPayload.TrustedSet(key)).Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("no signed manifest", result.Error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, _msi.ConfigureProductCallCount);
    }

    // ── The tight GUID check is preserved, and fires before any crypto work ───────────────────────────────

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("12345678-1234-1234-1234-123456789012")]
    [InlineData("{invalid}")]
    [InlineData("")]
    // .NET regex `$` matches end-of-string OR immediately before a single trailing '\n', even without
    // RegexOptions.Multiline, so an otherwise-well-formed GUID with a trailing newline would slip through an
    // otherwise-correct ^...$ anchor.
    [InlineData(AuthorizedProductCode + "\n")]
    public void Execute_InvalidGuid_Rejected_NeverUninstalls(string invalidProductCode)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        // A valid signed manifest so the failure is provably the GUID check, not the publisher gate.
        var manifestJson = SignedManifestPayload.UninstallManifestJson([AuthorizedProductCode], key);
        var payload = SignedManifestPayload.BuildUninstall(invalidProductCode, manifestJson);

        var result = Command(SignedManifestPayload.TrustedSet(key)).Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("GUID", result.Error.Message, StringComparison.Ordinal);
        Assert.Equal(0, _msi.ConfigureProductCallCount);
    }

    // ── Exit-code handling on the authorized path ─────────────────────────────────────────────────────────

    [Fact]
    public void Execute_RebootRequired_ReturnsSuccess()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        _msi.ConfigureProductReturnCode = 3010;
        var manifestJson = SignedManifestPayload.UninstallManifestJson([AuthorizedProductCode], key);
        var payload = SignedManifestPayload.BuildUninstall(AuthorizedProductCode, manifestJson);

        var result = Command(SignedManifestPayload.TrustedSet(key)).Execute(payload);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Execute_UninstallFailure_ReturnsError()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        _msi.ConfigureProductReturnCode = 1603;
        var manifestJson = SignedManifestPayload.UninstallManifestJson([AuthorizedProductCode], key);
        var payload = SignedManifestPayload.BuildUninstall(AuthorizedProductCode, manifestJson);

        var result = Command(SignedManifestPayload.TrustedSet(key)).Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.ExecutionError, result.Error.Kind);
        Assert.Contains("1603", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_SetsUIToSilent()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifestJson = SignedManifestPayload.UninstallManifestJson([AuthorizedProductCode], key);
        var payload = SignedManifestPayload.BuildUninstall(AuthorizedProductCode, manifestJson);

        Command(SignedManifestPayload.TrustedSet(key)).Execute(payload);

        Assert.Equal(1, _msi.SetInternalUICallCount);
        Assert.Equal(2, _msi.LastUILevel);
    }
}
