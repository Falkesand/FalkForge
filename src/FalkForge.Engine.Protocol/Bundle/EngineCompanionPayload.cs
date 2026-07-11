namespace FalkForge.Engine.Protocol.Bundle;

/// <summary>
/// Well-known identity of the elevation companion payload a runnable bundle carries.
///
/// <para>The elevation companion (<c>FalkForge.Engine.Elevation.exe</c>) executes elevated
/// (as SYSTEM for per-machine installs), so it rides the bundle as a first-class trust-covered
/// payload: an ordinary overlay TOC entry under this reserved <see cref="PackageId"/>, its SHA-256
/// declared in <see cref="Manifest.InstallerManifest.EngineCompanionSha256"/>, and — when the
/// bundle is integrity-signed — covered by the ECDSA signature envelope exactly like every
/// installable payload. No FALKBUNDLE format change is involved; the id is reserved at compile
/// time so no authored package can impersonate the companion.</para>
/// </summary>
public static class EngineCompanionPayload
{
    /// <summary>
    /// Reserved TOC package id AND on-disk file name of the embedded elevation companion.
    /// The bundle compiler rejects any authored package or pre-UI prerequisite using this id.
    /// </summary>
    public const string PackageId = "FalkForge.Engine.Elevation.exe";
}
