using System.Security.Cryptography;
using FalkForge.Engine.Protocol.Bundle;
using FalkForge.Engine.Protocol.Manifest;

namespace FalkForge.Compiler.Bundle.Compilation;

/// <summary>
/// Shared compile-time step that resolves the elevation companion
/// (<see cref="ElevationCompanionLocator"/>), guards the reserved payload id, appends the
/// companion to the embeddable payload list, and declares its SHA-256 on the manifest
/// (<see cref="InstallerManifest.EngineCompanionSha256"/>). Used by both
/// <see cref="BundleCompiler"/> and <see cref="DeltaBundleCompiler"/> so full and delta builds
/// carry the companion identically — crucially BEFORE integrity signing, so the SYSTEM-executing
/// companion is covered by the same signed payload-trust chain as every installable payload.
/// </summary>
internal static class ElevationCompanionAppender
{
    /// <summary>
    /// Appends the resolved companion to <paramref name="payloads"/> and returns the manifest with
    /// its <c>EngineCompanionSha256</c> declared. Returns the manifest unchanged when the bundle
    /// legitimately carries no companion (opt-out / placeholder), and a loud failure when the
    /// companion is unresolvable or an authored payload uses the reserved id.
    /// </summary>
    internal static Result<InstallerManifest> Append(
        List<PayloadEntry> payloads,
        InstallerManifest manifest,
        BundleModel model,
        string? explicitCompanionPath,
        string? explicitStubPath,
        bool allowPlaceholderStub,
        Func<Result<string>> engineResolver)
    {
        // Reserved-id guard first: even an opted-out bundle must not ship an authored payload
        // impersonating the companion — the engine would extract it under the companion's name.
        foreach (var payload in payloads)
        {
            if (string.Equals(payload.PackageId, EngineCompanionPayload.PackageId, StringComparison.OrdinalIgnoreCase))
                return Result<InstallerManifest>.Failure(ErrorKind.BundleError,
                    $"Package id '{payload.PackageId}' is reserved for the embedded elevation " +
                    "companion and cannot be used by an authored package or pre-UI prerequisite.");
        }

        var resolved = ElevationCompanionLocator.Resolve(
            explicitCompanionPath, explicitStubPath, allowPlaceholderStub,
            model.OmitElevationCompanion, engineResolver);
        if (resolved.IsFailure)
            return Result<InstallerManifest>.Failure(resolved.Error);

        if (resolved.Value.ResolvedPath is not { } companionPath)
            return manifest;

        long originalSize;
        string hash;
        try
        {
            using var fileStream = File.OpenRead(companionPath);
            originalSize = fileStream.Length;
            hash = Convert.ToHexString(SHA256.HashData(fileStream));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<InstallerManifest>.Failure(ErrorKind.BundleError,
                $"Failed to read the elevation companion at {companionPath}: {ex.Message}");
        }

        payloads.Add(new PayloadEntry
        {
            PackageId = EngineCompanionPayload.PackageId,
            SourcePath = companionPath,
            OriginalSize = originalSize,
            Sha256Hash = hash
        });

        // A `with` expression keeps every other manifest field verbatim (dedup lesson: hand-copied
        // manifest rebuilds silently drop fields).
        return manifest with { EngineCompanionSha256 = hash };
    }
}
