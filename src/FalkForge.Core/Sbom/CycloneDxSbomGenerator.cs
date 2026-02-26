using System.Text.Json;

namespace FalkForge.Sbom;

public sealed class CycloneDxSbomGenerator : ISbomGenerator
{
    private static readonly JsonWriterOptions WriterOptions = new() { Indented = true };

    private static string MapComponentType(SbomComponentType type) => type switch
    {
        SbomComponentType.Library     => "library",
        SbomComponentType.Application => "application",
        SbomComponentType.Framework   => "framework",
        SbomComponentType.File        => "file",
        _                             => "library"
    };

    public Result<Unit> Generate(SbomDocument document, Stream output)
    {
        try
        {
            using var writer = new Utf8JsonWriter(output, WriterOptions);

            writer.WriteStartObject();
            writer.WriteString("bomFormat", "CycloneDX");
            writer.WriteString("specVersion", "1.6");
            writer.WriteString("serialNumber", document.SerialNumber);
            writer.WriteNumber("version", 1);

            // metadata
            writer.WritePropertyName("metadata");
            writer.WriteStartObject();
            writer.WriteString("timestamp", document.Metadata.Timestamp.ToString("yyyy-MM-ddTHH:mm:ssZ"));
            writer.WritePropertyName("component");
            writer.WriteStartObject();
            writer.WriteString("type", "application");
            writer.WriteString("name", document.Metadata.Name);
            writer.WriteString("version", document.Metadata.Version);
            writer.WritePropertyName("supplier");
            writer.WriteStartObject();
            writer.WriteString("name", document.Metadata.Manufacturer);
            writer.WriteEndObject(); // supplier
            writer.WriteEndObject(); // component
            writer.WritePropertyName("tools");
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteString("vendor", "FalkForge");
            writer.WriteString("name", "FalkForge");
            writer.WriteEndObject();
            writer.WriteEndArray(); // tools
            writer.WriteEndObject(); // metadata

            // components
            writer.WritePropertyName("components");
            writer.WriteStartArray();
            foreach (var component in document.Components)
            {
                writer.WriteStartObject();
                writer.WriteString("type", MapComponentType(component.Type));
                writer.WriteString("name", component.Name);
                writer.WriteString("version", component.Version);
                if (component.Publisher is not null)
                {
                    writer.WritePropertyName("supplier");
                    writer.WriteStartObject();
                    writer.WriteString("name", component.Publisher);
                    writer.WriteEndObject();
                }
                writer.WritePropertyName("hashes");
                writer.WriteStartArray();
                writer.WriteStartObject();
                writer.WriteString("alg", "SHA-256");
                writer.WriteString("content", component.Sha256Hash);
                writer.WriteEndObject();
                writer.WriteEndArray(); // hashes
                writer.WriteEndObject(); // component
            }
            writer.WriteEndArray(); // components

            // dependencies
            writer.WritePropertyName("dependencies");
            writer.WriteStartArray();
            foreach (var dep in document.Dependencies)
            {
                writer.WriteStartObject();
                writer.WriteString("ref", dep.Ref);
                writer.WritePropertyName("dependsOn");
                writer.WriteStartArray();
                foreach (var d in dep.DependsOn)
                    writer.WriteStringValue(d);
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray(); // dependencies

            writer.WriteEndObject(); // root
            writer.Flush();

            return Result<Unit>.Success(Unit.Value);
        }
        catch (Exception ex)
        {
            return Result<Unit>.Failure(ErrorKind.IoError, $"Failed to generate SBOM: {ex.Message}");
        }
    }
}
