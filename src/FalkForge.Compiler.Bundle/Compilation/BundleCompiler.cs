using System.Security.Cryptography;
using FalkForge.Compiler.Bundle.Validation;

namespace FalkForge.Compiler.Bundle.Compilation;

public sealed class BundleCompiler
{
    private readonly ManifestGenerator _manifestGenerator = new();
    private readonly BundleValidator _validator = new();

    public string? EngineStubPath { get; set; }

    public Result<string> Compile(BundleModel model, string outputPath)
    {
        // Step 1: Validate
        var validation = _validator.Validate(model);
        if (validation.IsFailure)
            return Result<string>.Failure(validation.Error);

        // Step 2: Generate manifest
        var manifestResult = _manifestGenerator.Generate(model);
        if (manifestResult.IsFailure)
            return Result<string>.Failure(manifestResult.Error);

        var manifest = manifestResult.Value;

        // Step 3: Prepare payload metadata (skip remote-only packages); stream SHA256 to avoid ReadAllBytes
        var payloads = new List<PayloadEntry>();
        foreach (var package in model.Packages)
        {
            // Remote-only payloads are not embedded in the bundle
            if (package.RemotePayload is not null)
                continue;

            if (!File.Exists(package.SourcePath))
                return Result<string>.Failure(ErrorKind.PayloadError,
                    $"Package source not found: {package.SourcePath}");

            long originalSize;
            string hash;
            using (var fileStream = File.OpenRead(package.SourcePath))
            {
                originalSize = fileStream.Length;
                hash = Convert.ToHexString(SHA256.HashData(fileStream));
            }

            payloads.Add(new PayloadEntry
            {
                PackageId = package.Id,
                SourcePath = package.SourcePath,
                OriginalSize = originalSize,
                Sha256Hash = hash,
                ContainerId = package.ContainerId
            });
        }

        // Step 4: Create stub (minimal placeholder -- in production, this is the pre-compiled NativeAOT engine binary)
        var stubPath = CreateStub(outputPath);

        // Step 5: Embed payloads
        var outputFilePath = Path.Combine(outputPath, $"{model.Name}.exe");
        var embedder = new PayloadEmbedder();
        var embedResult = embedder.Embed(stubPath, outputFilePath, manifest, payloads);

        // Clean up stub
        try
        {
            File.Delete(stubPath);
        }
        catch (IOException)
        {
            /* best effort cleanup */
        }

        if (embedResult.IsFailure)
            return Result<string>.Failure(embedResult.Error);

        return outputFilePath;
    }

    private string CreateStub(string outputDir)
    {
        Directory.CreateDirectory(outputDir);

        if (EngineStubPath is not null && File.Exists(EngineStubPath))
        {
            var stubPath = Path.Combine(outputDir, $"stub_{Guid.NewGuid():N}.tmp");
            File.Copy(EngineStubPath, stubPath, overwrite: true);
            return stubPath;
        }

        // Fallback: empty placeholder (design-time / test)
        var fallbackPath = Path.Combine(outputDir, $"stub_{Guid.NewGuid():N}.tmp");
        File.WriteAllBytes(fallbackPath, []);
        return fallbackPath;
    }
}