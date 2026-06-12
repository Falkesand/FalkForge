using System.Security.Cryptography;
using FalkForge.Sbom;

namespace FalkForge.Compiler.Bundle.Compilation;

/// <summary>
/// Writes a CycloneDX 1.6 SBOM sidecar alongside a compiled bundle EXE.
/// The sidecar is opt-in: it is produced only when <see cref="BundleModel.SbomOptions"/>
/// is non-null on the model, or when the <c>FALKFORGE_GENERATE_SBOM</c> environment
/// variable is set. This mirrors the MsiCompiler SBOM pattern.
/// </summary>
internal static class BundleSbomHelper
{
    internal static Result<Unit> WriteSbomSidecar(
        BundleModel model,
        IReadOnlyList<PayloadEntry> payloads,
        string bundleOutputPath)
    {
        var envSet = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FALKFORGE_GENERATE_SBOM"));

        // Skip when neither SbomOptions nor env var triggers generation.
        if (model.SbomOptions is null && !envSet)
            return Result<Unit>.Success(Unit.Value);

        try
        {
            var components = new List<SbomComponent>();

            // Add one component per embedded payload (already hashed by BundleCompiler).
            foreach (var payload in payloads)
            {
                components.Add(new SbomComponent
                {
                    Name = payload.PackageId,
                    Version = model.Version,
                    Type = SbomComponentType.File,
                    Sha256Hash = payload.Sha256Hash
                });
            }

            // Add user-supplied components from SbomOptions.
            if (model.SbomOptions is not null)
                components.AddRange(model.SbomOptions.AdditionalComponents);

            var doc = new SbomDocument
            {
                SerialNumber = "urn:uuid:" + Guid.NewGuid(),
                Metadata = new SbomMetadata
                {
                    Name = model.Name,
                    Version = model.Version,
                    Manufacturer = model.Manufacturer,
                    Timestamp = GetReproducibleTimestamp()
                },
                Components = components,
                Dependencies = []
            };

            return SbomWriter.WriteToFile(doc, bundleOutputPath + ".cdx.json");
        }
        catch (Exception ex)
        {
            return Result<Unit>.Failure(ErrorKind.IoError, $"Bundle SBOM generation failed: {ex.Message}");
        }
    }

    private static DateTimeOffset GetReproducibleTimestamp()
    {
        var epoch = Environment.GetEnvironmentVariable("SOURCE_DATE_EPOCH");
        if (epoch is not null && long.TryParse(epoch, out var seconds))
            return DateTimeOffset.FromUnixTimeSeconds(seconds);
        return DateTimeOffset.UtcNow;
    }
}
