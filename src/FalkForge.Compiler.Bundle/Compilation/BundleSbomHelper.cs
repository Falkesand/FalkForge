using FalkForge.Configuration;
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
        var envSet = EnvVarCatalog.IsSbomGenerationRequested();

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

            // Add user-supplied components from SbomOptions. Each digest is validated first —
            // serializing an arbitrary caller-supplied string as a SHA-256 claim would let the
            // sidecar make an integrity attestation that is not even shaped like a hash.
            if (model.SbomOptions is not null)
            {
                foreach (var component in model.SbomOptions.AdditionalComponents)
                {
                    if (!SbomDigestValidator.IsValidSha256Hex(component.Sha256Hash))
                        return Result<Unit>.Failure(ErrorKind.Validation,
                            $"Bundle SBOM: additional component '{component.Name}' has a digest " +
                            $"'{component.Sha256Hash}' that is not a valid SHA-256 hash (expected 64 " +
                            "hexadecimal characters).");
                }

                components.AddRange(model.SbomOptions.AdditionalComponents);
            }

            // Serial number + timestamp are deterministic under an explicit Reproducible()
            // epoch override or, absent that, SOURCE_DATE_EPOCH — so a reproducible build emits
            // a byte-identical SBOM sidecar (was Guid.NewGuid + UtcNow).
            var identity = ReproducibleSbomIdentity.Resolve(
                components, model.Name, model.Version, model.ReproducibleOptions?.SourceDateEpoch);

            var doc = new SbomDocument
            {
                SerialNumber = identity.SerialNumber,
                Metadata = new SbomMetadata
                {
                    Name = model.Name,
                    Version = model.Version,
                    Manufacturer = model.Manufacturer,
                    Timestamp = identity.Timestamp
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
}
