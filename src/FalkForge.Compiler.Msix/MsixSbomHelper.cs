using FalkForge.Compiler.Msix.Packaging;
using FalkForge.Configuration;
using FalkForge.Sbom;

namespace FalkForge.Compiler.Msix;

/// <summary>
/// Writes a CycloneDX 1.6 SBOM sidecar alongside a compiled .msix package.
/// The sidecar is opt-in: it is produced only when <see cref="MsixModel.SbomOptions"/> is
/// non-null on the model, or when the <c>FALKFORGE_GENERATE_SBOM</c> environment variable is
/// set. This mirrors the MsiCompiler and BundleCompiler SBOM pattern.
/// </summary>
internal static class MsixSbomHelper
{
    internal static Result<Unit> WriteSbomSidecar(
        MsixModel model,
        IReadOnlyList<VfsFileEntry> layout,
        IReadOnlyDictionary<string, string> payloadHashes,
        string msixOutputPath)
    {
        var envSet = EnvVarCatalog.IsSbomGenerationRequested();

        // Skip when neither SbomOptions nor env var triggers generation.
        if (model.SbomOptions is null && !envSet)
            return Result<Unit>.Success(Unit.Value);

        try
        {
            var version = model.Version.ToString();
            var components = new List<SbomComponent>();

            foreach (var entry in layout)
            {
                // payloadHashes is captured by AppxPackageWriter while it copies each file into
                // the package (see MsixPackageResult) — not reopened here from the source path,
                // which could have changed since packaging (and signing) completed.
                if (!payloadHashes.TryGetValue(entry.PackageRelativePath, out var hash))
                    continue;

                components.Add(new SbomComponent
                {
                    Name = GetPackagedFileName(entry.PackageRelativePath),
                    Version = version,
                    Type = SbomComponentType.File,
                    Sha256Hash = hash
                });
            }

            // Add user-supplied components from SbomOptions. Each digest is validated first —
            // serializing an arbitrary caller-supplied string as a SHA-256 claim would let the
            // sidecar make an integrity attestation that is not even shaped like a hash
            // (CodeRabbit #3658582431).
            if (model.SbomOptions is not null)
            {
                foreach (var component in model.SbomOptions.AdditionalComponents)
                {
                    if (!SbomDigestValidator.IsValidSha256Hex(component.Sha256Hash))
                        return Result<Unit>.Failure(ErrorKind.Validation,
                            $"MSIX SBOM: additional component '{component.Name}' has a digest " +
                            $"'{component.Sha256Hash}' that is not a valid SHA-256 hash (expected 64 " +
                            "hexadecimal characters).");
                }

                components.AddRange(model.SbomOptions.AdditionalComponents);
            }

            // Deterministic serial + timestamp under SOURCE_DATE_EPOCH, so a reproducible build
            // emits a byte-identical sidecar. MSIX has no per-model epoch override yet.
            var identity = ReproducibleSbomIdentity.Resolve(components, model.Name, version);

            var doc = new SbomDocument
            {
                SerialNumber = identity.SerialNumber,
                Metadata = new SbomMetadata
                {
                    Name = model.Name,
                    Version = version,
                    Manufacturer = model.PublisherDisplayName,
                    Timestamp = identity.Timestamp
                },
                Components = components,
                Dependencies = []
            };

            return SbomWriter.WriteToFile(doc, msixOutputPath + ".cdx.json");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Result<Unit>.Failure(ErrorKind.IoError, $"MSIX SBOM generation failed: {ex.Message}");
        }
    }

    // The layout path is always '/'-separated (see VfsMapper), so Path.GetFileName is not
    // portable enough here — take the segment after the last separator directly.
    private static string GetPackagedFileName(string packageRelativePath)
    {
        var index = packageRelativePath.LastIndexOf('/');
        return index < 0 ? packageRelativePath : packageRelativePath[(index + 1)..];
    }
}
