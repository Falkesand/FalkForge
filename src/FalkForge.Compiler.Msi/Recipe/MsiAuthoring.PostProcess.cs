using System.Security.Cryptography;
using FalkForge.Compiler.Msi.Signing;
using FalkForge.Compiler.Msi.Validation;
using FalkForge.Configuration;
using FalkForge.Diagnostics;
using FalkForge.Models;
using FalkForge.WinGet;

namespace FalkForge.Compiler.Msi.Recipe;

// Steps 7-11: reproducible-timestamp patch, code signing, integrity signing, ICE
// validation, SBOM sidecar, WinGet manifest. Split out of the main Compile
// orchestration to keep MsiAuthoring.cs focused on pipeline sequencing.
public static partial class MsiAuthoring
{
    /// <summary>
    /// Steps 7-11: reproducible-timestamp patching, code signing, integrity signing, ICE validation, SBOM
    /// sidecar, and WinGet manifest generation. All gated behind <see cref="CompileOptions.PostProcess"/> —
    /// localized-variant rebuilds (used only to diff localizable tables for MST generation) skip this
    /// entirely. ICE infrastructure failures and SBOM failures are non-fatal (surfaced as warnings); every
    /// other failure aborts the compile.
    /// </summary>
    private static Result<Unit> RunPostProcessSteps(
        PackageModel package,
        string msiPath,
        string outputPath,
        CompileOptions options,
        ResolvedPackage resolved,
        IFalkLogger? logger)
    {
        // Step 7: Reproducible timestamp patching — Windows MsiSummaryInfoPersist
        // always stamps PID_LASTSAVE_DTM with current time, so for reproducible
        // builds the patcher walks the OLE compound document and overwrites the
        // FILETIME values in place.
        if (options.PostProcess && package.ReproducibleOptions is { } reproducibleOpts)
        {
            if (logger is not null && logger.MinimumLevel <= LogLevel.Debug)
                logger.Debug("MsiAuthoring", "Step 7: patching reproducible timestamps.");

            Result<Unit> patchResult = SummaryInfoPatcher.PatchTimestamps(msiPath, reproducibleOpts.Timestamp);
            if (patchResult.IsFailure)
            {
                logger?.Log(LogLevel.Error, "MsiAuthoring", $"Step 7: timestamp patching failed: {patchResult.Error.Message}",
                    new Dictionary<string, string> { ["code"] = patchResult.Error.Kind.ToString() });
                return Result<Unit>.Failure(patchResult.Error);
            }
        }

        // Step 8: Code signing.
        if (options.PostProcess && package.Signing is not null)
        {
            if (logger is not null && logger.MinimumLevel <= LogLevel.Debug)
                logger.Debug("MsiAuthoring", "Step 8: code signing.");

            CodeSigner signer = new();
            Result<Unit> signResult = signer.Sign(msiPath, package.Signing);
            if (signResult.IsFailure)
            {
                logger?.Log(LogLevel.Error, "MsiAuthoring", $"Step 8: code signing failed: {signResult.Error.Message}",
                    new Dictionary<string, string> { ["code"] = signResult.Error.Kind.ToString() });
                return Result<Unit>.Failure(signResult.Error);
            }
        }

        // Step 8.5: Integrity signing. The ECDSA envelope is pure .NET and always signs when
        // Integrity() is configured (FALKFORGE_NO_SIGN is the only opt-out) — it no longer depends on
        // the external sigil CLI. Sigil, when present, opportunistically adds a DSSE SBOM attestation on
        // top; see IntegritySigner.SignAndEmbed.
        if (options.PostProcess && !IsIntegritySigningDisabled() && package.Integrity is not null)
        {
            if (logger is not null && logger.MinimumLevel <= LogLevel.Debug)
                logger.Debug("MsiAuthoring", "Step 8.5: integrity signing.");

            // ECDSA signatures are nondeterministic (fresh random nonce per call), so embedding one
            // in-band in the MSI would defeat Reproducible() the moment Integrity() is also configured.
            // IntegritySigner.SignAndEmbed skips the in-band _FalkForgeIntegrity table in that case and
            // writes the signature sidecar-only — surfaced here at Info level (not gated behind
            // --verbose) since it is a real, user-visible change of where the signature lives.
            if (package.ReproducibleOptions is not null)
            {
                logger?.Info("MsiAuthoring",
                    "Step 8.5: Reproducible() + Integrity() are both configured. The MSI's in-band " +
                    "_FalkForgeIntegrity table is skipped so the artifact stays byte-identical across " +
                    "builds; the ECDSA signature is written sidecar-only ('<msi>.sig.json'). Verify via " +
                    "the sidecar, not the embedded table.");
            }

            Result<Unit> integrityResult = IntegritySigner.SignAndEmbed(msiPath, package, resolved.Files);
            if (integrityResult.IsFailure)
            {
                logger?.Log(LogLevel.Error, "MsiAuthoring", $"Step 8.5: integrity signing failed: {integrityResult.Error.Message}",
                    new Dictionary<string, string> { ["code"] = integrityResult.Error.Kind.ToString() });
                return Result<Unit>.Failure(integrityResult.Error);
            }
        }

        // Step 9: ICE validation. Reproducible builds skip ICE because ICE
        // dialog boxes can perturb the file in ways that drift the digest.
        // Default IceConfiguration for forge build uses lenient cub-absent behavior:
        // developer machines without the Windows SDK should not fail the build unless
        // the user has explicitly configured strict ICE via PackageBuilder.Ice().
        // CLI forge validate --ice and CI pipelines that need strict checking must use
        // the explicit config path or set SkipWhenCubUnavailable = false.
        IceConfiguration iceConfig = package.IceConfiguration
            ?? new IceConfiguration { SkipWhenCubUnavailable = true };
        if (options.PostProcess && iceConfig.Enabled && package.ReproducibleOptions is null)
        {
            if (logger is not null && logger.MinimumLevel <= LogLevel.Debug)
                logger.Debug("MsiAuthoring", "Step 9: ICE validation.");

            IceValidator iceValidator = new();
            Result<IceValidationResult> iceResult = iceValidator.Validate(msiPath, iceConfig);
            if (iceResult.IsSuccess)
            {
                if (iceConfig.ReportPath is not null)
                {
                    IceReportExporter.Export(iceResult.Value, iceConfig.ReportPath);
                }

                if (iceResult.Value.Errors.Count > 0 || iceResult.Value.Failures.Count > 0)
                {
                    string iceErrors = string.Join("; ", iceResult.Value.Messages
                        .Where(m => m.Severity is IceMessageSeverity.Error or IceMessageSeverity.Failure)
                        .Select(m => $"{m.IceName}: {m.Description}"));
                    string iceCodes = string.Join(",", iceResult.Value.Messages
                        .Where(m => m.Severity is IceMessageSeverity.Error or IceMessageSeverity.Failure)
                        .Select(m => m.IceName));
                    logger?.Log(LogLevel.Error, "MsiAuthoring", $"Step 9: ICE validation failed: {iceErrors}",
                        new Dictionary<string, string> { ["code"] = iceCodes });
                    return Result<Unit>.Failure(ErrorKind.Validation, $"ICE validation failed: {iceErrors}");
                }
            }
            else
            {
                // ICE infrastructure failure (e.g. darice.cub missing/unreadable, native MSI API
                // failure) is non-fatal — mirror MsiCompiler. Previously silently dropped; now
                // surfaced as a Warning so a `forge build --verbose` user can see ICE never ran.
                logger?.Log(LogLevel.Warning, "MsiAuthoring",
                    $"Step 9: ICE validation could not run: {iceResult.Error.Message}",
                    new Dictionary<string, string> { ["code"] = iceResult.Error.Kind.ToString() });
            }
        }

        // Step 10: SBOM sidecar (opt-in). SBOM failure is non-fatal.
        if (options.PostProcess)
        {
            if (logger is not null && logger.MinimumLevel <= LogLevel.Debug)
                logger.Debug("MsiAuthoring", "Step 10: SBOM sidecar.");

            Result<Unit> sbomResult = SbomHelper.WriteSbomSidecar(package, resolved.Files, msiPath);
            if (sbomResult.IsFailure)
            {
                // Previously silently dropped (`_ = sbomResult;`) — now surfaced as a Warning so a
                // `forge build --verbose` user can see the sidecar was not written. Compile still
                // succeeds; SBOM generation remains opt-in and non-fatal.
                logger?.Log(LogLevel.Warning, "MsiAuthoring",
                    $"Step 10: SBOM sidecar generation failed: {sbomResult.Error.Message}",
                    new Dictionary<string, string> { ["code"] = sbomResult.Error.Kind.ToString() });
            }
        }

        // Step 11: WinGet manifest (opt-in via PackageBuilder.WinGet()).
        if (options.PostProcess && package.WinGet is not null)
        {
            if (logger is not null && logger.MinimumLevel <= LogLevel.Debug)
                logger.Debug("MsiAuthoring", "Step 11: WinGet manifest.");

            using FileStream msiStream = File.OpenRead(msiPath);
            string sha256 = Convert.ToHexString(SHA256.HashData(msiStream));
            Result<string> wingetResult = WinGetManifestWriter.Write(
                package,
                package.WinGet,
                outputPath,
                sha256,
                Path.GetFileName(msiPath));
            if (wingetResult.IsFailure)
            {
                logger?.Log(LogLevel.Error, "MsiAuthoring", $"Step 11: WinGet manifest generation failed: {wingetResult.Error.Message}",
                    new Dictionary<string, string> { ["code"] = wingetResult.Error.Kind.ToString() });
                return Result<Unit>.Failure(wingetResult.Error);
            }
        }

        return Result<Unit>.Success(Unit.Value);
    }

    private static bool IsIntegritySigningDisabled()
        => EnvVarCatalog.IsSigningDisabled();
}
