namespace FalkForge.Engine.Protocol.Bundle;

/// <summary>
/// Well-known identity of the UI executable payload a runnable bundle carries.
///
/// <para>The UI (<c>FalkForge.Ui.exe</c>) is the process the bootstrapper launches once the
/// pre-UI prerequisite stage completes, so — like the elevation companion — it is meant to ride
/// the bundle as a first-class trust-covered payload: an ordinary overlay TOC entry under this
/// reserved <see cref="PackageId"/>, its SHA-256 declared in the manifest, and covered by the
/// ECDSA signature envelope on a signed bundle. That wiring (append before signing, manifest hash
/// field, gate resolver branch, rejection of an authored payload using this id) is a later
/// landing; this constant exists ahead of it so the compiler's <c>UiPath</c> seam can resolve and
/// validate the binary before it is ever embedded.</para>
/// </summary>
public static class UiPayload
{
    /// <summary>
    /// Reserved TOC package id AND on-disk file name of the embedded UI executable.
    /// </summary>
    public const string PackageId = "FalkForge.Ui.exe";
}
