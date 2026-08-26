namespace FalkForge.Engine.Protocol.Bundle;

/// <summary>
/// Well-known identity of the UI executable payload a runnable bundle carries.
///
/// <para>The UI (<c>FalkForge.Ui.exe</c>) is the process the bootstrapper launches once the
/// pre-UI prerequisite stage completes, so — like the elevation companion — it rides the bundle as
/// a first-class trust-covered payload: an ordinary overlay TOC entry under this reserved
/// <see cref="PackageId"/>, its SHA-256 declared in the manifest
/// (<c>InstallerManifest.EngineUiSha256</c>), and covered by the ECDSA signature envelope on a
/// signed bundle. An authored package, pre-UI prerequisite or MSI transform using this id is
/// rejected at build time (BDL036), because a payload extracted under this name would be launched
/// with the session pipe secret.</para>
/// </summary>
public static class UiPayload
{
    /// <summary>
    /// Reserved TOC package id AND on-disk file name of the embedded UI executable.
    /// </summary>
    public const string PackageId = "FalkForge.Ui.exe";
}
