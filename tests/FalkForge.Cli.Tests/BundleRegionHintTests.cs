using System.Security.Cryptography;
using FalkForge.Cli.Verification;
using FalkForge.Compiler.Bundle.Builders;
using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Engine.Protocol.Bundle;
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
        // A signed bundle's embedded manifest JSON carries a non-null "ManifestSignature"
        // (PascalCase — ManifestJsonContext uses the default naming policy).
        var json = "{\"ManifestSignature\":\"BASE64SIGNATUREDATA==\",\"Packages\":[]}"u8.ToArray();

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
        var json = "{\"ManifestSignature\":null,\"Packages\":[]}"u8.ToArray();

        Assert.False(BundleRegionHint.ManifestIsSigned(json));
    }

    [Fact]
    public void ManifestIsSigned_RealSignedBundleManifest_ReturnsTrue()
    {
        // Uses the actual compiler + reader path (BundleCompiler writes the manifest via
        // ManifestJsonContext, BundleReader.Extract returns the same bytes VerifyCommand
        // inspects), so this test pins the REAL serialized key casing rather than a
        // hand-crafted string. A signed bundle must be reported as signed, otherwise the
        // "forge verify" signed-bundle diagnostic never fires.
        var manifestBytes = CompileBundleAndReadManifest(signed: true);

        Assert.True(BundleRegionHint.ManifestIsSigned(manifestBytes));
    }

    [Fact]
    public void ManifestIsSigned_RealUnsignedBundleManifest_ReturnsFalse()
    {
        // Guard against an over-loose match: an unsigned bundle's manifest must not be
        // reported as signed even though other manifest keys share common substrings.
        var manifestBytes = CompileBundleAndReadManifest(signed: false);

        Assert.False(BundleRegionHint.ManifestIsSigned(manifestBytes));
    }

    private static byte[] CompileBundleAndReadManifest(bool signed)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"falk-regionhint-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var msiPath = Path.Combine(dir, "App.msi");
            File.WriteAllBytes(msiPath, RandomNumberGenerator.GetBytes(512));

            var builder = new BundleBuilder()
                .Name("RegionHintTest")
                .Manufacturer("Cli Tests")
                .Version("1.0.0")
                .UseSilentUI()
                .Chain(chain => chain.MsiPackage(msiPath, pkg => pkg.Id("AppMsi").Version("1.0.0")));

            if (signed)
                builder.Integrity(i => { }); // ephemeral ECDSA key

            var result = new BundleCompiler { AllowPlaceholderStub = true }.Compile(builder.Build(), Path.Combine(dir, "out"));
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);

            var content = BundleReader.Extract(result.Value);
            Assert.True(content.IsSuccess, content.IsFailure ? content.Error.Message : null);
            Assert.NotNull(content.Value.ManifestJsonBytes);

            return content.Value.ManifestJsonBytes!;
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
