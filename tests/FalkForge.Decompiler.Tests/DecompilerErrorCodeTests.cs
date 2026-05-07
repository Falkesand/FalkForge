using System.Runtime.Versioning;
using System.Xml.Linq;
using FalkForge.Decompiler.Recipe;
using FalkForge.Decompiler.Recipe.Schemas;
using Xunit;

namespace FalkForge.Decompiler.Tests;

/// <summary>
/// Unit tests covering decompiler error codes that previously had zero test coverage:
/// DEC003, WBD003, WBD004, WBD006, WMM001.
/// Each test verifies that the Result is a failure and that the error message contains
/// the expected error code.
/// </summary>
public sealed class DecompilerErrorCodeTests
{
    // ── DEC003 ─────────────────────────────────────────────────────────────────
    // DEC003 is emitted by individual TableReaders when QueryTable returns a failure
    // (i.e., the table exists but the underlying query itself fails).

    [Fact]
    [SupportedOSPlatform("windows")]
    public void MsiDecompiler_PropertyTableQueryFailure_ReturnsDec003()
    {
        // Arrange: Property table exists but its QueryTable call fails
        using var access = new MockMsiTableAccess()
            .WithTableQueryFailure("Property", "Simulated read error");
        var decompiler = new MsiDecompiler(access);

        // Act
        var result = decompiler.Decompile("ignored.msi");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Contains("DEC003", result.Error.Message);
    }

    [Fact]
    public void PropertySchema_QueryTableFailure_ReturnsDec003()
    {
        // Arrange: Property table exists but QueryTable returns failure
        using var access = new MockMsiTableAccess()
            .WithTableQueryFailure("Property", "Disk I/O error on Property table");

        // Act
        var result = TableReadEngine.ReadOne(PropertySchema.Schema, access);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Contains("DEC003", result.Error.Message);
        Assert.Contains("Property table", result.Error.Message);
    }

    [Fact]
    public void PropertySchema_QueryTableFailure_AltAccess_ReturnsDec003()
    {
        // Arrange: verifies DEC003 path regardless of error message text
        using var access = new MockMsiTableAccess()
            .WithTableQueryFailure("Property", "Corrupt MSI storage");

        // Act
        var result = TableReadEngine.ReadOne(PropertySchema.Schema, access);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("DEC003", result.Error.Message);
    }

    [Fact]
    public void FeatureSchema_QueryTableFailure_ReturnsDec003()
    {
        // Arrange: Feature table exists but QueryTable returns failure
        using var access = new MockMsiTableAccess()
            .WithTableQueryFailure("Feature", "Corrupt feature storage");

        // Act
        var result = TableReadEngine.ReadOne(FeatureSchema.Schema, access);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Contains("DEC003", result.Error.Message);
        Assert.Contains("Feature table", result.Error.Message);
    }

    [Fact]
    public void FileSchema_QueryTableFailure_ReturnsDec003()
    {
        // Arrange: File table exists but QueryTable returns failure
        using var access = new MockMsiTableAccess()
            .WithTableQueryFailure("File", "Read error on File table");

        // Act
        var result = TableReadEngine.ReadOne(FileSchema.Schema, access);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Contains("DEC003", result.Error.Message);
        Assert.Contains("File table", result.Error.Message);
    }

    [Fact]
    public void RegistrySchema_QueryTableFailure_ReturnsDec003()
    {
        // Arrange: Registry table exists but QueryTable returns failure
        using var access = new MockMsiTableAccess()
            .WithTableQueryFailure("Registry", "I/O error on Registry table");

        // Act
        var result = TableReadEngine.ReadOne(RegistrySchema.Schema, access);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Contains("DEC003", result.Error.Message);
        Assert.Contains("Registry table", result.Error.Message);
    }

    [Fact]
    public void ComponentSchema_QueryTableFailure_ReturnsDec003()
    {
        // Arrange: Component table exists but QueryTable returns failure
        using var access = new MockMsiTableAccess()
            .WithTableQueryFailure("Component", "I/O error on Component table");

        // Act
        var result = TableReadEngine.ReadOne(ComponentSchema.Schema, access);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Contains("DEC003", result.Error.Message);
        Assert.Contains("Component table", result.Error.Message);
    }

    // ── WBD003 ─────────────────────────────────────────────────────────────────
    // WBD003 is emitted by WixBurnAccess.Open when the PE file does not contain
    // a .wixburn section. Tested via MockWixBurnAccess injecting the failure.

    [Fact]
    [SupportedOSPlatform("windows")]
    public void WixBundleDecompiler_NoWixburnSection_ReturnsWbd003()
    {
        // Arrange: mock simulates WBD003 from WixBurnAccess.Open
        var mock = new MockWixBurnAccess()
            .WithBundleId(Guid.NewGuid())
            .WithManifestFailure(ErrorKind.BundleError, "WBD003: PE file does not contain a .wixburn section.");
        var decompiler = new WixBundleDecompiler(mock);

        // Act
        var result = decompiler.Decompile("dummy.exe");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains("WBD003", result.Error.Message);
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void WixBundleDecompiler_NoWixburnSection_DecompileToCSharp_ReturnsWbd003()
    {
        // Arrange
        var mock = new MockWixBurnAccess()
            .WithBundleId(Guid.NewGuid())
            .WithManifestFailure(ErrorKind.BundleError, "WBD003: PE file does not contain a .wixburn section.");
        var decompiler = new WixBundleDecompiler(mock);

        // Act
        var result = decompiler.DecompileToCSharp("dummy.exe");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains("WBD003", result.Error.Message);
    }

    // ── WBD004 ─────────────────────────────────────────────────────────────────
    // WBD004 is emitted by WixBurnAccess.Open when the .wixburn magic DWORD is
    // invalid or when the bundle contains no containers.

    [Fact]
    [SupportedOSPlatform("windows")]
    public void WixBundleDecompiler_InvalidWixburnMagic_ReturnsWbd004()
    {
        // Arrange: mock simulates WBD004 from invalid .wixburn magic
        var mock = new MockWixBurnAccess()
            .WithBundleId(Guid.NewGuid())
            .WithManifestFailure(ErrorKind.BundleError, "WBD004: Invalid .wixburn magic: expected 0x00F14300, found 0xDEADBEEF.");
        var decompiler = new WixBundleDecompiler(mock);

        // Act
        var result = decompiler.Decompile("dummy.exe");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains("WBD004", result.Error.Message);
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void WixBundleDecompiler_NoContainers_ReturnsWbd004()
    {
        // Arrange: mock simulates WBD004 from zero containers
        var mock = new MockWixBurnAccess()
            .WithBundleId(Guid.NewGuid())
            .WithManifestFailure(ErrorKind.BundleError, "WBD004: Bundle contains no containers.");
        var decompiler = new WixBundleDecompiler(mock);

        // Act
        var result = decompiler.Decompile("dummy.exe");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains("WBD004", result.Error.Message);
    }

    // ── WBD006 ─────────────────────────────────────────────────────────────────
    // WBD006 is emitted by WixBurnAccess.ReadManifest when the key "0" (manifest
    // file) is not found in the extracted UX cabinet.

    [Fact]
    [SupportedOSPlatform("windows")]
    public void WixBundleDecompiler_ManifestMissingFromCabinet_ReturnsWbd006()
    {
        // Arrange: mock simulates WBD006 from missing manifest file in cabinet
        var mock = new MockWixBurnAccess()
            .WithBundleId(Guid.NewGuid())
            .WithManifestFailure(ErrorKind.BundleError, "WBD006: Manifest file not found in UX container cabinet.");
        var decompiler = new WixBundleDecompiler(mock);

        // Act
        var result = decompiler.Decompile("dummy.exe");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains("WBD006", result.Error.Message);
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void WixBundleDecompiler_ManifestMissingFromCabinet_DecompileToCSharp_ReturnsWbd006()
    {
        // Arrange
        var mock = new MockWixBurnAccess()
            .WithBundleId(Guid.NewGuid())
            .WithManifestFailure(ErrorKind.BundleError, "WBD006: Manifest file not found in UX container cabinet.");
        var decompiler = new WixBundleDecompiler(mock);

        // Act
        var result = decompiler.DecompileToCSharp("dummy.exe");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains("WBD006", result.Error.Message);
    }

    // ── WMM001 ─────────────────────────────────────────────────────────────────
    // WMM001 is emitted by WixManifestMapper.Map when the XDocument has no root
    // element (manifest.Root is null). This path is exercised via WixBundleDecompiler
    // with a mock that returns an XDocument with no root element.

    [Fact]
    [SupportedOSPlatform("windows")]
    public void WixBundleDecompiler_RootlessXDocument_ReturnsWmm001()
    {
        // Arrange: XDocument with no root element triggers WMM001 in WixManifestMapper.Map
        var rootlessDocument = new XDocument();
        var mock = new MockWixBurnAccess()
            .WithBundleId(Guid.NewGuid())
            .WithManifestDocument(rootlessDocument);
        var decompiler = new WixBundleDecompiler(mock);

        // Act
        var result = decompiler.Decompile("dummy.exe");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains("WMM001", result.Error.Message);
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void WixBundleDecompiler_RootlessXDocument_DecompileToCSharp_ReturnsWmm001()
    {
        // Arrange
        var rootlessDocument = new XDocument();
        var mock = new MockWixBurnAccess()
            .WithBundleId(Guid.NewGuid())
            .WithManifestDocument(rootlessDocument);
        var decompiler = new WixBundleDecompiler(mock);

        // Act
        var result = decompiler.DecompileToCSharp("my-installer.exe");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains("WMM001", result.Error.Message);
    }

    [Fact]
    public void WixManifestMapper_RootlessXDocument_ReturnsWmm001()
    {
        // Arrange: XDocument.Root is null when XDocument has no root element
        var rootlessDocument = new XDocument();
        var bundleId = Guid.NewGuid();

        // Act
        var result = WixManifestMapper.Map(rootlessDocument, bundleId);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains("WMM001", result.Error.Message);
    }
}
