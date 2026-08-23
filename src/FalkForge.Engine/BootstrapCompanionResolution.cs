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
/// <param name="ExpectedSha256">
/// The SHA-256 digest, as 64 hexadecimal characters, that the companion's bytes were proven to
/// have. Null exactly when <paramref name="VerifiedPath"/> is null.
/// <para>
/// The resolver runs while the bundle is being unpacked. The companion is not launched until much
/// later: the engine first runs the pre-UI prerequisite bootstrap, starts the UI process, and
/// waits for the user to read a licence, pick a directory and click Install. The extraction
/// directory sits under <c>%TEMP%</c> and belongs to the user, so any process running as that user
/// can replace the file during that wait. Carrying the digest forward lets the launch site open
/// the file itself and prove the bytes again at the moment it starts the process, instead of
/// trusting a path string that was checked minutes earlier.
/// </para>
/// </param>
internal readonly record struct BootstrapCompanionResolution(string? VerifiedPath, string? ExpectedSha256)
{
    /// <summary>The bundle carries no companion; the engine falls back to per-user behavior.</summary>
    internal static BootstrapCompanionResolution None => new(null, null);
}
