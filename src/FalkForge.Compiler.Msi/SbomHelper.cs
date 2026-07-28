using FalkForge.Configuration;
using FalkForge.Models;
using FalkForge.Sbom;

namespace FalkForge.Compiler.Msi;

internal static class SbomHelper
{
    internal static Result<Unit> WriteSbomSidecar(
        PackageModel package,
        IReadOnlyList<ResolvedFile> files,
        IReadOnlyDictionary<string, string> packagedFileHashes,
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
                // packagedFileHashes is captured by CabinetBuilder while the native FCI
                // compressor reads each file's bytes into the cabinet (see
                // CabinetBuilder.Callbacks.cs) — not reopened here from the source path, which
                // could have changed since packaging completed (TOCTOU). A file absent from the
                // map (e.g. it was never actually added to a cabinet) is skipped rather than
                // falling back to a re-read of a possibly-stale source file.
                if (!packagedFileHashes.TryGetValue(file.SourcePath, out var hash))
                    continue;

                components.Add(new SbomComponent
                {
                    Name = file.FileName,
                    Version = package.Version.ToString(),
                    Type = SbomComponentType.File,
                    Sha256Hash = hash
                });
            }

            // Add user-supplied components from SbomOptions. Each digest is validated first —
            // serializing an arbitrary caller-supplied string as a SHA-256 claim would let the
            // sidecar make an integrity attestation that is not even shaped like a hash.
            if (package.SbomOptions is not null)
            {
                foreach (var component in package.SbomOptions.AdditionalComponents)
                {
                    if (!SbomDigestValidator.IsValidSha256Hex(component.Sha256Hash))
                        return Result<Unit>.Failure(ErrorKind.Validation,
                            $"MSI SBOM: additional component '{component.Name}' has a digest " +
                            $"'{component.Sha256Hash}' that is not a valid SHA-256 hash (expected 64 " +
                            "hexadecimal characters).");
                }

                components.AddRange(package.SbomOptions.AdditionalComponents);
            }

            // Deterministic serial + timestamp under an explicit Reproducible() epoch override
            // or, absent that, SOURCE_DATE_EPOCH — so a reproducible build emits a
            // byte-identical SBOM sidecar (was Guid.NewGuid + UtcNow).
            var identity = ReproducibleSbomIdentity.Resolve(
                components, package.Name, package.Version.ToString(), package.ReproducibleOptions?.SourceDateEpoch);

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
