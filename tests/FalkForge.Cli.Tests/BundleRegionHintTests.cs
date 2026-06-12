using FalkForge.Cli.Verification;
using Xunit;

namespace FalkForge.Cli.Tests;

/// <summary>
/// Tests for <see cref="BundleRegionHint"/>, which maps a byte offset within a FalkForge bundle
/// to a human-readable structural region (stub / manifest / payload / TOC / footer) and detects
/// whether the bundle carries a non-deterministic ECDSA ManifestSignature. The region hint makes
/// a bundle MISMATCH actionable ("the difference is in the manifest signature region"), and the
/// signed-detection drives the honest signed-bundle diagnostic.
/// </summary>
public sealed class BundleRegionHintTests
{
    [Fact]
    public void Classify_OffsetInFooter_ReturnsFooter()
    {
        // The footer is the last 24 bytes (16 magic + 8 TOC offset).
        var hint = BundleRegionHint.Classify(totalLength: 1000, tocOffset: 800, offset: 990);

        Assert.Equal("footer", hint);
    }

    [Fact]
    public void Classify_OffsetInToc_ReturnsToc()
    {
        // Between tocOffset and the footer start (totalLength - 24).
        var hint = BundleRegionHint.Classify(totalLength: 1000, tocOffset: 800, offset: 820);

        Assert.Equal("TOC", hint);
    }

    [Fact]
    public void Classify_OffsetBeforeToc_ReturnsPayloadOrManifest()
    {
        // Anything before the TOC is payload/manifest/stub data — a non-footer/non-TOC region.
        var hint = BundleRegionHint.Classify(totalLength: 1000, tocOffset: 800, offset: 100);

        Assert.NotEqual("footer", hint);
        Assert.NotEqual("TOC", hint);
    }

    [Fact]
    public void ManifestIsSigned_NullManifest_ReturnsFalse()
    {
        Assert.False(BundleRegionHint.ManifestIsSigned(null));
    }

    [Fact]
    public void ManifestIsSigned_ManifestWithSignatureField_ReturnsTrue()
    {
        // A signed bundle's embedded manifest JSON carries a non-null "manifestSignature".
        var json = "{\"manifestSignature\":\"BASE64SIGNATUREDATA==\",\"packages\":[]}"u8.ToArray();

        Assert.True(BundleRegionHint.ManifestIsSigned(json));
    }

    [Fact]
    public void ManifestIsSigned_ManifestWithoutSignature_ReturnsFalse()
    {
        var json = "{\"packages\":[]}"u8.ToArray();

        Assert.False(BundleRegionHint.ManifestIsSigned(json));
    }

    [Fact]
    public void ManifestIsSigned_NullSignatureField_ReturnsFalse()
    {
        var json = "{\"manifestSignature\":null,\"packages\":[]}"u8.ToArray();

        Assert.False(BundleRegionHint.ManifestIsSigned(json));
    }
}
