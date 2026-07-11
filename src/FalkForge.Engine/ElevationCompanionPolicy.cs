namespace FalkForge.Engine;

/// <summary>
/// Controls how <see cref="EngineSession.BindToPipe"/> resolves the elevation companion
/// (<c>FalkForge.Engine.Elevation.exe</c>) — the binary launched ELEVATED (SYSTEM for
/// per-machine installs) for privileged commands.
///
/// <para><b>Why an explicit policy.</b> The session historically fell back to an ambient probe
/// beside the engine (<c>AppContext.BaseDirectory</c>) whenever no verified companion path was
/// supplied. That probe is correct for a plain engine run, where the companion legitimately ships
/// next to the engine executable. It is WRONG during a bundle bootstrap: there the bundle manifest
/// is the authority on whether a companion exists, and a manifest that declares none (authored
/// <c>WithoutElevationCompanion()</c>) must run per-user — an attacker who plants
/// <c>FalkForge.Engine.Elevation.exe</c> beside the bundle exe must never get an unverified
/// SYSTEM launch. This enum makes that security property direct instead of emergent.</para>
/// </summary>
public enum ElevationCompanionPolicy
{
    /// <summary>
    /// Plain (non-bundle-bootstrap) engine run: the ambient probe beside the engine is the
    /// normal, intended companion source. Default.
    /// </summary>
    AmbientAllowed = 0,

    /// <summary>
    /// Bundle bootstrap, manifest declares a companion: use ONLY the integrity-verified
    /// extracted companion at <see cref="EngineSessionOptions.ElevationCompanionPath"/>.
    /// Never falls back to the ambient probe — if the verified file is gone, the session
    /// runs per-user (fail-safe: nothing unverified is ever launched elevated).
    /// </summary>
    VerifiedPath,

    /// <summary>
    /// Bundle bootstrap, manifest declares NO companion: run per-user with no elevation
    /// gateway. The ambient probe is skipped — the manifest is authoritative, so a planted
    /// companion beside the bundle exe is never launched.
    /// </summary>
    NoneDeclared
}
