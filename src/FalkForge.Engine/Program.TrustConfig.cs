namespace FalkForge.Engine;

/// <summary>
/// Publisher trust-configuration seam (C18). This file is the code-path counterpart to the
/// <c>-p:FalkForgeTrustedKey</c> build parameter: a publisher who rebuilds FalkForge.Engine edits the
/// body of <see cref="ConfigureTrust"/> to register additional trusted publisher keys. It runs at the
/// very top of <see cref="Program.Main"/>, before any bundle is extracted or verified, and the keys it
/// registers are UNIONed with the MSBuild-baked set
/// (<see cref="FalkForge.Engine.Integrity.BakedTrustedKeys"/>) — baked keys are always honored. Keeping the
/// seam in its own file means a publisher's trust edits never conflict with upstream changes to
/// <c>Program.cs</c>.
///
/// <para><b>Security.</b> Only compiled code registers trust here. Never read a key to trust from a
/// bundle, manifest, downloaded update, or any file/network input the installer processes — that would
/// reopen the C14 trust-anchor hole (a self-describing key that grants its own trust). The engine's
/// verifiers derive trust exclusively from the frozen effective set produced here.</para>
/// </summary>
internal static partial class Program
{
    static partial void ConfigureTrust()
    {
        // Intentionally empty by default: the stock engine trusts only the MSBuild-baked keys
        // (-p:FalkForgeTrustedKey). A publisher rebuilding the engine adds registrations here, for example:
        //
        //   EngineTrustAnchor.TrustPublicKey(Convert.FromBase64String("<base64-encoded SubjectPublicKeyInfo>"));
        //   EngineTrustAnchor.TrustPublicKeyPem("-----BEGIN PUBLIC KEY-----\n...\n-----END PUBLIC KEY-----");
        //   EngineTrustAnchor.TrustFingerprint("A1B2C3...");  // 64-hex SHA-256 of the key's SPKI
        //
        // Each call must run before the first bundle verification (guaranteed here) or it throws.
    }
}
