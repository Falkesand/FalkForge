using System.Runtime.Versioning;
using FalkForge.Compiler.Msix.Manifest;
using FalkForge.Compiler.Msix.Packaging;
using FalkForge.Compiler.Msix.Registry;
using FalkForge.Models;

namespace FalkForge.Compiler.Msix;

[SupportedOSPlatform("windows")]
public sealed class MsixCompiler
{
    public Result<string> Compile(MsixModel model, string outputPath)
    {
        // Step 1: Validate
        var validation = MsixValidator.Validate(model);
        if (validation.IsFailure)
            return Result<string>.Failure(validation.Error);

        // Step 2: Resolve VFS layout
        var layout = VfsMapper.Resolve(model);

        // Step 3: Generate AppxManifest.xml
        var manifestResult = AppxManifestGenerator.Generate(model);
        if (manifestResult.IsFailure)
            return Result<string>.Failure(manifestResult.Error);

        // Step 4: Build registry hive (optional)
        byte[]? registryHive = null;
        if (model.RegistryEntries.Count > 0)
        {
            var hiveResult = RegistryHiveBuilder.Build(model.RegistryEntries);
            if (hiveResult.IsFailure)
                return Result<string>.Failure(hiveResult.Error);
            registryHive = hiveResult.Value;
        }

        // Step 5: Create MSIX package
        var sanitizedName = FileNameSanitizer.Sanitize(model.DisplayName.Trim());
        var msixFileName = $"{sanitizedName}-{model.Version}.msix";
        var msixPath = Path.Combine(outputPath, msixFileName);
        Directory.CreateDirectory(outputPath);

        var packageResult = AppxPackageWriter.CreatePackage(msixPath, manifestResult.Value, layout, registryHive);
        if (packageResult.IsFailure)
            return Result<string>.Failure(packageResult.Error);

        // Step 6: Sign the package
        var signResult = SignPackage(msixPath, model.Signing!);
        if (signResult.IsFailure)
            return Result<string>.Failure(signResult.Error);

        // Step 7: SBOM sidecar (opt-in via SbomOptions or FALKFORGE_GENERATE_SBOM).
        // Unlike MsiCompiler — which warns through its logger and continues — MsixCompiler has
        // no logger to warn through, so a requested-but-unwritten SBOM fails the compile rather
        // than disappearing silently.
        var sbomResult = MsixSbomHelper.WriteSbomSidecar(model, layout, packageResult.Value.PayloadHashes, msixPath);
        if (sbomResult.IsFailure)
            return Result<string>.Failure(sbomResult.Error);

        // Step 8: Generate .appinstaller (optional)
        if (model.UpdateSettings is not null)
        {
            var appInstallerResult = AppInstallerGenerator.Generate(model, msixFileName);
            if (appInstallerResult.IsSuccess)
            {
                var appInstallerPath = Path.ChangeExtension(msixPath, ".appinstaller");
                appInstallerResult.Value.Save(appInstallerPath);
            }
        }

        return Result<string>.Success(msixPath);
    }

    private static Result<Unit> SignPackage(string msixPath, SigningOptions signing)
    {
        try
        {
            var args = new List<string> { "sign", "/fd", signing.DigestAlgorithm };

            if (signing.CertificatePath is not null)
                args.AddRange(["/f", signing.CertificatePath]);
            else if (signing.CertificateThumbprint is not null)
                args.AddRange(["/sha1", signing.CertificateThumbprint, "/s", signing.StoreName]);

            if (signing.TimestampUrl is not null)
                args.AddRange(["/tr", signing.TimestampUrl, "/td", signing.DigestAlgorithm]);

            if (signing.Description is not null)
                args.AddRange(["/d", signing.Description]);

            args.Add(msixPath);

            var arguments = string.Join(" ", args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
            return SigntoolRunner.Run("signtool.exe", arguments, SigntoolRunner.DefaultTimeout);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Result<Unit>.Failure(ErrorKind.CompilationError, $"Signing failed: {ex.Message}");
        }
    }
}
