using System.Security.Cryptography;
using FalkForge.Engine.Protocol.Bundle;
using FalkForge.Engine.Protocol.Manifest;

namespace FalkForge.Compiler.Bundle.Compilation;

/// <summary>
/// Shared compile-time step that resolves the UI executable (<see cref="UiLocator"/>), guards the
/// reserved payload id, appends the UI to the embeddable payload list, and declares its SHA-256 on
/// the manifest (<see cref="InstallerManifest.EngineUiSha256"/>). Used by both
/// <see cref="BundleCompiler"/> and <see cref="DeltaBundleCompiler"/> so full and delta builds
/// carry the UI identically — crucially BEFORE integrity signing, so the process the bootstrapper
/// launches and hands its pipe secret to is covered by the same signed payload-trust chain as
/// every installable payload.
/// <para>The direct counterpart of <see cref="ElevationCompanionAppender"/>. There is no opt-out
/// equivalent to <c>OmitElevationCompanion</c>: a bundle without a companion still installs
/// per-user, but a bundle without a UI cannot run at all.</para>
/// </summary>
internal static class UiAppender
{
    /// <summary>
    /// Appends the resolved UI to <paramref name="payloads"/> and returns the manifest with its
    /// <c>EngineUiSha256</c> declared. Returns the manifest unchanged when the bundle legitimately
    /// carries no UI (a design-time placeholder build), and a loud failure when the UI is
    /// unresolvable or an authored payload uses the reserved id.
    /// </summary>
    internal static Result<InstallerManifest> Append(
        List<PayloadEntry> payloads,
        InstallerManifest manifest,
        string? explicitUiPath,
        bool allowPlaceholderStub)
    {
        // Reserved-id guard first: an authored payload impersonating the UI would be extracted
        // under the UI's name and launched with the session pipe secret. BundleValidator's BDL036
        // rejects the same collision earlier and over the whole model, including payloads assigned
        // to an external container that never reach this list; this guard is the embedded-half
        // backstop, mirroring ElevationCompanionAppender.
        foreach (var payload in payloads)
        {
            if (string.Equals(payload.PackageId, UiPayload.PackageId, StringComparison.OrdinalIgnoreCase))
                return Result<InstallerManifest>.Failure(ErrorKind.BundleError,
                    $"Package id '{payload.PackageId}' is reserved for the embedded UI executable " +
                    "and cannot be used by an authored package or pre-UI prerequisite.");
        }

        var resolved = UiLocator.Resolve(explicitUiPath, allowPlaceholderStub);
        if (resolved.IsFailure)
            return Result<InstallerManifest>.Failure(resolved.Error);

        if (resolved.Value is not { } uiPath)
            return manifest;

        long originalSize;
        string hash;
        try
        {
            using var fileStream = File.OpenRead(uiPath);
            originalSize = fileStream.Length;
            hash = Convert.ToHexString(SHA256.HashData(fileStream));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<InstallerManifest>.Failure(ErrorKind.BundleError,
                $"Failed to read the UI executable at {uiPath}: {ex.Message}");
        }

        payloads.Add(new PayloadEntry
        {
            PackageId = UiPayload.PackageId,
            SourcePath = uiPath,
            OriginalSize = originalSize,
            Sha256Hash = hash
        });

        // A `with` expression keeps every other manifest field verbatim (dedup lesson: hand-copied
        // manifest rebuilds silently drop fields).
        return manifest with { EngineUiSha256 = hash };
    }
}
