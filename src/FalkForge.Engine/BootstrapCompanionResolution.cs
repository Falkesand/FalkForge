namespace FalkForge.Engine;

/// <summary>
/// Outcome of a successful <see cref="BootstrapCompanionResolver.Resolve"/> call.
/// <see cref="VerifiedPath"/> is null when the bundle carries no elevation companion (older
/// bundles, per-user-only authoring) — a legitimate state distinct from a verification failure,
/// which <see cref="Result{T}"/> cannot express with a null success value. A non-null path points
/// at the extracted companion whose bytes were hash-verified during extraction and whose TOC hash
/// binds to the manifest's declared companion hash.
/// </summary>
/// <param name="VerifiedPath">Full path to the verified extracted companion, or null for none.</param>
internal readonly record struct BootstrapCompanionResolution(string? VerifiedPath)
{
    /// <summary>The bundle carries no companion; the engine falls back to per-user behavior.</summary>
    internal static BootstrapCompanionResolution None => new((string?)null);
}
