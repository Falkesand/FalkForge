using System.Runtime.Versioning;
using System.Security.Cryptography;
using FalkForge.Compiler.Msi.Tables;
using FalkForge.Models;
using FalkForge.Sbom;

namespace FalkForge.Compiler.Msi.Signing;

[SupportedOSPlatform("windows")]
internal static class IntegritySigner
{
    internal static Result<Unit> SignAndEmbed(
        string msiPath,
        PackageModel package,
        IReadOnlyList<ResolvedFile> resolvedFiles)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"FalkIntegrity_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var config = package.Integrity;

            // Step 1: Write payload hashes to temp directory
            var hashDir = Path.Combine(tempDir, "payloads");
            Directory.CreateDirectory(hashDir);
            WritePayloadHashes(hashDir, resolvedFiles);

            // Step 2: Sign manifest
            var signer = new SigilSigner();
            var manifestOutputPath = Path.Combine(tempDir, "manifest.sig.json");
            var manifestResult = signer.RunSignManifest(hashDir, manifestOutputPath, config);
            if (manifestResult.IsFailure)
                return Result<Unit>.Failure(manifestResult.Error);

            var manifestJson = File.ReadAllText(manifestOutputPath);

            // Step 3: Generate SBOM JSON for attestation
            var sbomFormat = config?.SbomFormat ?? SbomFormat.Spdx;
            var sbomPath = Path.Combine(tempDir, "sbom.json");
            var sbomResult = GenerateSbomForAttestation(package, resolvedFiles, sbomPath);
            if (sbomResult.IsFailure)
                return Result<Unit>.Failure(sbomResult.Error);

            // Step 4: Attest SBOM
            var attestOutputPath = Path.Combine(tempDir, "sbom.attest.json");
            var attestResult = signer.RunAttest(msiPath, sbomPath, sbomFormat, attestOutputPath, config);
            if (attestResult.IsFailure)
                return Result<Unit>.Failure(attestResult.Error);

            var attestJson = File.ReadAllText(attestOutputPath);

            var sbomFormatString = sbomFormat switch
            {
                SbomFormat.CycloneDx => "cyclonedx",
                _ => "spdx"
            };

            // Step 5: Re-open MSI and embed integrity data
            var dbResult = MsiDatabase.Open(msiPath);
            if (dbResult.IsFailure)
                return Result<Unit>.Failure(dbResult.Error);

            using (var database = dbResult.Value)
            {
                var emitResult = IntegrityTableEmitter.EmitIntegrityData(
                    database, manifestJson, attestJson, sbomFormatString);
                if (emitResult.IsFailure)
                    return emitResult;

                var commitResult = database.Commit();
                if (commitResult.IsFailure)
                    return commitResult;
            }

            // Step 6: Write sidecar files
            File.Copy(manifestOutputPath, msiPath + ".sig.json", overwrite: true);
            File.Copy(attestOutputPath, msiPath + ".attest.json", overwrite: true);

            return Unit.Value;
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    private static void WritePayloadHashes(string hashDir, IReadOnlyList<ResolvedFile> files)
    {
        foreach (var file in files)
        {
            if (!File.Exists(file.SourcePath))
                continue;

            using var stream = File.OpenRead(file.SourcePath);
            var hash = Convert.ToHexString(SHA256.HashData(stream));
            var hashFile = Path.Combine(hashDir, file.FileName + ".sha256");
            File.WriteAllText(hashFile, hash);
        }
    }

    private static Result<Unit> GenerateSbomForAttestation(
        PackageModel package,
        IReadOnlyList<ResolvedFile> files,
        string outputPath)
    {
        var components = new List<SbomComponent>();

        foreach (var file in files)
        {
            if (!File.Exists(file.SourcePath))
                continue;

            using var stream = File.OpenRead(file.SourcePath);
            var hash = Convert.ToHexString(SHA256.HashData(stream));

            components.Add(new SbomComponent
            {
                Name = file.FileName,
                Version = package.Version.ToString(),
                Type = SbomComponentType.File,
                Sha256Hash = hash
            });
        }

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
                Timestamp = DateTimeOffset.UtcNow
            },
            Components = components,
            Dependencies = []
        };

        return SbomWriter.WriteToFile(doc, outputPath);
    }
}
