using System.Text.Json;
using FalkForge.Models;

namespace FalkForge.Sbom;

public static class IntegritySbomGenerator
{
    private static readonly JsonWriterOptions WriterOptions = new() { Indented = true };

    public static string Generate(SbomFormat format, SbomPackageInfo package, IReadOnlyList<SbomFileEntry> files) =>
        format switch
        {
            SbomFormat.Spdx => GenerateSpdx(package, files),
            SbomFormat.CycloneDx => GenerateCycloneDx(package, files),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported SBOM format.")
        };

    public static string GenerateSpdx(SbomPackageInfo package, IReadOnlyList<SbomFileEntry> files)
    {
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("spdxVersion", "SPDX-2.3");
            writer.WriteString("dataLicense", "CC0-1.0");
            writer.WriteString("SPDXID", "SPDXRef-DOCUMENT");
            writer.WriteString("name", $"{package.Name}-{package.Version}");
            writer.WriteString("documentNamespace", $"https://falkforge.dev/sbom/{Guid.NewGuid()}");

            // creationInfo
            writer.WritePropertyName("creationInfo");
            writer.WriteStartObject();
            writer.WriteString("created", DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
            writer.WritePropertyName("creators");
            writer.WriteStartArray();
            writer.WriteStringValue("Tool: FalkForge");
            writer.WriteEndArray();
            writer.WriteEndObject();

            // packages
            writer.WritePropertyName("packages");
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteString("SPDXID", "SPDXRef-Package");
            writer.WriteString("name", package.Name);
            writer.WriteString("versionInfo", package.Version);
            writer.WriteString("supplier", $"Organization: {package.Manufacturer}");
            writer.WriteString("downloadLocation", "NOASSERTION");
            writer.WriteEndObject();
            writer.WriteEndArray();

            // files
            writer.WritePropertyName("files");
            writer.WriteStartArray();
            for (var i = 0; i < files.Count; i++)
            {
                var file = files[i];
                writer.WriteStartObject();
                writer.WriteString("SPDXID", $"SPDXRef-File-{i}");
                writer.WriteString("fileName", file.FileName);
                writer.WritePropertyName("checksums");
                writer.WriteStartArray();
                writer.WriteStartObject();
                writer.WriteString("algorithm", "SHA256");
                writer.WriteString("checksumValue", file.Sha256Hash);
                writer.WriteEndObject();
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }

    public static string GenerateCycloneDx(SbomPackageInfo package, IReadOnlyList<SbomFileEntry> files)
    {
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("bomFormat", "CycloneDX");
            writer.WriteString("specVersion", "1.5");
            writer.WriteString("serialNumber", $"urn:uuid:{Guid.NewGuid()}");

            // metadata
            writer.WritePropertyName("metadata");
            writer.WriteStartObject();
            writer.WriteString("timestamp", DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
            writer.WritePropertyName("tools");
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteString("name", "FalkForge");
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WritePropertyName("component");
            writer.WriteStartObject();
            writer.WriteString("type", "application");
            writer.WriteString("name", package.Name);
            writer.WriteString("version", package.Version);
            writer.WriteEndObject();
            writer.WriteEndObject();

            // components
            writer.WritePropertyName("components");
            writer.WriteStartArray();
            foreach (var file in files)
            {
                writer.WriteStartObject();
                writer.WriteString("type", "file");
                writer.WriteString("name", file.FileName);
                writer.WritePropertyName("hashes");
                writer.WriteStartArray();
                writer.WriteStartObject();
                writer.WriteString("alg", "SHA-256");
                writer.WriteString("content", file.Sha256Hash);
                writer.WriteEndObject();
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }
}
