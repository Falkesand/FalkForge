using System.Security.Cryptography;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Models;
using FalkForge.Sbom;

namespace FalkForge.Compiler.Bundle.Compilation;

internal static class BundleIntegritySigner
{
    internal static Result<InstallerManifest> SignAndEnrich(
        InstallerManifest manifest,
        BundleModel model,
        IReadOnlyList<PayloadEntry> payloads)
    {
        if (model.Integrity is null)
            return manifest;

        if (!BundleSigilDetector.IsAvailable())
            return manifest;

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FALKFORGE_NO_SIGN")))
            return manifest;

        var tempDir = Path.Combine(Path.GetTempPath(), $"FalkBundleIntegrity_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var config = model.Integrity;

            // Step 1: Write payload hashes
            var hashDir = Path.Combine(tempDir, "payloads");
            Directory.CreateDirectory(hashDir);
            WritePayloadHashes(hashDir, payloads);

            // Step 2: Sign manifest
            var signer = new BundleSigilSigner();
            var manifestOutputPath = Path.Combine(tempDir, "manifest.sig.json");
            var manifestResult = signer.RunSignManifest(hashDir, manifestOutputPath, config);
            if (manifestResult.IsFailure)
                return Result<InstallerManifest>.Failure(manifestResult.Error);

            var manifestJson = File.ReadAllText(manifestOutputPath);

            // Step 3: Generate SBOM for attestation
            var sbomFormat = config.SbomFormat;
            var sbomPath = Path.Combine(tempDir, "sbom.json");
            var sbomResult = GenerateSbom(model, payloads, sbomPath);
            if (sbomResult.IsFailure)
                return Result<InstallerManifest>.Failure(sbomResult.Error);

            // Step 4: Attest SBOM (use a dummy artifact path since bundle isn't built yet)
            var dummyArtifactPath = Path.Combine(tempDir, "bundle.exe");
            File.WriteAllBytes(dummyArtifactPath, []);
            var attestOutputPath = Path.Combine(tempDir, "sbom.attest.json");
            var attestResult = signer.RunAttest(dummyArtifactPath, sbomPath, sbomFormat, attestOutputPath, config);
            if (attestResult.IsFailure)
                return Result<InstallerManifest>.Failure(attestResult.Error);

            var attestJson = File.ReadAllText(attestOutputPath);

            // Step 5: Return enriched manifest
            return new InstallerManifest
            {
                Name = manifest.Name,
                Manufacturer = manifest.Manufacturer,
                Version = manifest.Version,
                BundleId = manifest.BundleId,
                UpgradeCode = manifest.UpgradeCode,
                Packages = manifest.Packages,
                RelatedBundles = manifest.RelatedBundles,
                Chain = manifest.Chain,
                Variables = manifest.Variables,
                Features = manifest.Features,
                DependencyProviders = manifest.DependencyProviders,
                DependencyConsumers = manifest.DependencyConsumers,
                DependencyRequirements = manifest.DependencyRequirements,
                UiType = manifest.UiType,
                CustomUiProjectPath = manifest.CustomUiProjectPath,
                LicenseFile = manifest.LicenseFile,
                UpdateFeed = manifest.UpdateFeed,
                Scope = manifest.Scope,
                MaxBytesPerSecond = manifest.MaxBytesPerSecond,
                IsDryRun = manifest.IsDryRun,
                UnsupportedExtensions = manifest.UnsupportedExtensions,
                ManifestSignature = manifestJson,
                SbomAttestation = attestJson
            };
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    private static void WritePayloadHashes(string hashDir, IReadOnlyList<PayloadEntry> payloads)
    {
        foreach (var payload in payloads)
        {
            var hashFile = Path.Combine(hashDir, payload.PackageId + ".sha256");
            File.WriteAllText(hashFile, payload.Sha256Hash);
        }
    }

    private static Result<Unit> GenerateSbom(
        BundleModel model,
        IReadOnlyList<PayloadEntry> payloads,
        string outputPath)
    {
        var components = new List<SbomComponent>();

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
                Timestamp = DateTimeOffset.UtcNow
            },
            Components = components,
            Dependencies = []
        };

        return SbomWriter.WriteToFile(doc, outputPath);
    }
}
