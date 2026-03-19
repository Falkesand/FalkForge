using System.Security.Cryptography;
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
        var envSet = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FALKFORGE_GENERATE_SBOM"));

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

            var doc = new SbomDocument
            {
                SerialNumber = "urn:uuid:" + Guid.NewGuid(),
                Metadata = new SbomMetadata
                {
                    Name = package.Name,
                    Version = package.Version.ToString(),
                    Manufacturer = package.Manufacturer,
                    Timestamp = GetReproducibleTimestamp()
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

    private static DateTimeOffset GetReproducibleTimestamp()
    {
        var epoch = Environment.GetEnvironmentVariable("SOURCE_DATE_EPOCH");
        if (epoch is not null && long.TryParse(epoch, out var seconds))
            return DateTimeOffset.FromUnixTimeSeconds(seconds);
        return DateTimeOffset.UtcNow;
    }
}
