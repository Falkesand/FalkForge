using FalkForge.Sbom;
using Xunit;

namespace FalkForge.Core.Tests.Sbom;

/// <summary>
/// The MSI, Bundle, and MSIX SBOM writers (and BundleValidator's BDL033 public-key pin check)
/// all shared one copy each of this exact 64-hex-char validation loop before being consolidated
/// here. This is now the single place the shape rule is specified and tested.
/// </summary>
public sealed class SbomDigestValidatorTests
{
    private const string ValidSha256Hex = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    [Fact]
    public void IsValidSha256Hex_ExactlyValidLowercaseHex_ReturnsTrue()
    {
        Assert.True(SbomDigestValidator.IsValidSha256Hex(ValidSha256Hex));
    }

    [Fact]
    public void IsValidSha256Hex_UppercaseHex_ReturnsTrue()
    {
        Assert.True(SbomDigestValidator.IsValidSha256Hex(ValidSha256Hex.ToUpperInvariant()));
    }

    [Fact]
    public void IsValidSha256Hex_MixedCaseHex_ReturnsTrue()
    {
        Assert.True(SbomDigestValidator.IsValidSha256Hex("E3b0C44298fc1c149AFBF4c8996fb92427ae41e4649b934ca495991b7852b855"[..64]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ABC123")]
    [InlineData("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b85")] // 63 chars (one short)
    public void IsValidSha256Hex_TooShort_ReturnsFalse(string value)
    {
        Assert.False(SbomDigestValidator.IsValidSha256Hex(value));
    }

    [Fact]
    public void IsValidSha256Hex_TooLong_ReturnsFalse()
    {
        Assert.False(SbomDigestValidator.IsValidSha256Hex(ValidSha256Hex + "ff"));
    }

    [Theory]
    [InlineData("zz3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b85")] // 'z' is not hex
    [InlineData("g3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")] // 'g' is not hex
    public void IsValidSha256Hex_NonHexCharacters_ReturnsFalse(string value)
    {
        Assert.False(SbomDigestValidator.IsValidSha256Hex(value));
    }
}
