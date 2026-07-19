using System.Security.Cryptography;
using FalkForge.Configuration;
using FalkForge.Models;
using FalkForge.Sbom;

namespace FalkForge.Compiler.Msi;

internal static class SbomHelper
{
    internal static Result<Unit> WriteSbomSidecar(
        PackageModel package,
        IReadOnlyList<ResolvedFile> files,
        string msiOutputPath)
    {
        var envSet = EnvVarCatalog.IsSbomGenerationRequested();

        // Skip when neither SbomOptions nor env var triggers generation
        if (package.SbomOptions is null && !envSet)
            return Result<Unit>.Success(Unit.Value);

        try
        {
            var components = new List<SbomComponent>();

            foreach (var file in files)
            {
                if (!File.Exists(file.SourcePath))
                    continue;

                string hash;
                using (var fs = File.OpenRead(file.SourcePath))
                    hash = Convert.ToHexString(SHA256.HashData(fs));

                components.Add(new SbomComponent
                {
                    Name = file.FileName,
                    Version = package.Version.ToString(),
                    Type = SbomComponentType.File,
                    Sha256Hash = hash
                });
            }

            // Add user-supplied components from SbomOptions
            if (package.SbomOptions is not null)
                components.AddRange(package.SbomOptions.AdditionalComponents);

            // Deterministic serial + timestamp under SOURCE_DATE_EPOCH so a reproducible build
            // emits a byte-identical SBOM sidecar (was Guid.NewGuid + UtcNow).
            var identity = ReproducibleSbomIdentity.Resolve(
                components, package.Name, package.Version.ToString());

            var doc = new SbomDocument
            {
                SerialNumber = identity.SerialNumber,
                Metadata = new SbomMetadata
                {
                    Name = package.Name,
                    Version = package.Version.ToString(),
                    Manufacturer = package.Manufacturer,
                    Timestamp = identity.Timestamp
                },
                Components = components,
                Dependencies = []
            };

            return SbomWriter.WriteToFile(doc, msiOutputPath + ".cdx.json");
        }
        catch (Exception ex)
        {
            return Result<Unit>.Failure(ErrorKind.IoError, $"SBOM generation failed: {ex.Message}");
        }
    }
}
