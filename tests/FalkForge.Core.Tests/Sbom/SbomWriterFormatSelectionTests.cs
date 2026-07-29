using System.Text.Json;
using FalkForge.Models;
using FalkForge.Sbom;
using Xunit;

namespace FalkForge.Core.Tests.Sbom;

/// <summary>
/// <see cref="SbomWriter"/> must let <see cref="SbomFormat"/> choose the generator. It previously
/// hardcoded CycloneDX, so the enum produced labels only — and every assertion here therefore reads
/// the emitted document's own self-declaration (<c>spdxVersion</c> / <c>bomFormat</c>) rather than
/// any label FalkForge attaches, since the label was the part that lied.
/// </summary>
public sealed class SbomWriterFormatSelectionTests : IDisposable
{
    private const string Sha256 = "aabbccddee001122334455667788990011223344556677889900aabbccddeeff";
    private const string Sha1 = "da39a3ee5e6b4b0d3255bfef95601890afd80709";

    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"SbomWriter_{Guid.NewGuid():N}");

    public SbomWriterFormatSelectionTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { }
        }
    }

    private static SbomDocument MakeDocument() => new()
    {
        SerialNumber = "urn:uuid:11111111-2222-3333-4444-555555555555",
        Metadata = new SbomMetadata
        {
            Name = "TestApp",
            Version = "1.2.3",
            Manufacturer = "Contoso",
            Timestamp = DateTimeOffset.UnixEpoch,
        },
        Components =
        [
            new SbomComponent
            {
                Name = "app.exe",
                Version = "1.2.3",
                Type = SbomComponentType.File,
                Sha256Hash = Sha256,
                Sha1Hash = Sha1,
            },
        ],
        Dependencies = [],
    };

    [Fact]
    public void WriteToString_Spdx_EmitsADocumentThatDeclaresItselfSpdx()
    {
        var result = SbomWriter.WriteToString(MakeDocument(), SbomFormat.Spdx);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
        using var doc = JsonDocument.Parse(result.Value);
        Assert.Equal("SPDX-2.3", doc.RootElement.GetProperty("spdxVersion").GetString());
        Assert.False(doc.RootElement.TryGetProperty("bomFormat", out _));
    }

    [Fact]
    public void WriteToString_CycloneDx_EmitsADocumentThatDeclaresItselfCycloneDx()
    {
        var result = SbomWriter.WriteToString(MakeDocument(), SbomFormat.CycloneDx);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
        using var doc = JsonDocument.Parse(result.Value);
        Assert.Equal("CycloneDX", doc.RootElement.GetProperty("bomFormat").GetString());
        Assert.False(doc.RootElement.TryGetProperty("spdxVersion", out _));
    }

    [Fact]
    public void WriteToFile_Spdx_WritesSpdxBytesToDisk()
    {
        var path = Path.Combine(_tempDir, "sbom.json");

        var result = SbomWriter.WriteToFile(MakeDocument(), path, SbomFormat.Spdx);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal("SPDX-2.3", doc.RootElement.GetProperty("spdxVersion").GetString());
    }

    [Fact]
    public void WriteToFile_FormatOmitted_StaysCycloneDx()
    {
        // The `.cdx.json` sidecars (SbomHelper, BundleSbomHelper, MsixSbomHelper) are a CycloneDX
        // feature by name and by contract — they take no SbomFormat and must not start emitting SPDX
        // just because the default SbomFormat enum value is Spdx. This pins the default so a future
        // "align the defaults" change has to break a test that says why.
        var path = Path.Combine(_tempDir, "default.cdx.json");

        var result = SbomWriter.WriteToFile(MakeDocument(), path);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal("CycloneDX", doc.RootElement.GetProperty("bomFormat").GetString());
    }

    [Fact]
    public void WriteToString_SpdxWithAFileMissingItsSha1_SurfacesTheFailureRatherThanEmittingBadSpdx()
    {
        var document = new SbomDocument
        {
            SerialNumber = "urn:uuid:11111111-2222-3333-4444-555555555555",
            Metadata = new SbomMetadata
            {
                Name = "TestApp",
                Version = "1.2.3",
                Manufacturer = "Contoso",
                Timestamp = DateTimeOffset.UnixEpoch,
            },
            Components =
            [
                new SbomComponent
                {
                    Name = "app.exe",
                    Version = "1.2.3",
                    Type = SbomComponentType.File,
                    Sha256Hash = Sha256,
                },
            ],
            Dependencies = [],
        };

        var result = SbomWriter.WriteToString(document, SbomFormat.Spdx);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
    }

    [Fact]
    public void WriteToFile_UnknownFormat_FailsInsteadOfSilentlyPickingOne()
    {
        // Defaulting an unrecognised enum value to either generator is precisely how a document ends
        // up carrying a format label it does not honour.
        var path = Path.Combine(_tempDir, "unknown.json");

        var result = SbomWriter.WriteToFile(MakeDocument(), path, (SbomFormat)999);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.False(File.Exists(path), "Nothing may be written for a format the writer cannot honour.");
    }
}
