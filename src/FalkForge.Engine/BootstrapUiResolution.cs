namespace FalkForge.Engine;

/// <summary>
/// Outcome of a successful <see cref="BootstrapUiResolver.Resolve"/> call.
/// <see cref="VerifiedPath"/> is null when the bundle carries no UI payload (older bundles,
/// design-time placeholder builds) — a legitimate state distinct from a verification failure,
/// which <see cref="Result{T}"/> cannot express with a null success value. A non-null path points
/// at the extracted UI whose bytes were hash-verified during extraction and whose TOC hash binds
/// to the manifest's declared UI hash.
/// </summary>
/// <param name="VerifiedPath">Full path to the verified extracted UI executable, or null for none.</param>
/// <param name="ExpectedSha256">
/// The SHA-256 digest, as 64 hexadecimal characters, that the UI's bytes were proven to have.
/// Null exactly when <paramref name="VerifiedPath"/> is null.
/// <para>
/// The resolver runs while the bundle is being unpacked. The UI is not launched until later: the
/// engine first acquires any external containers and then runs the whole pre-UI prerequisite
/// bootstrap, including its own elevation prompts. The extraction directory sits under
/// <c>%TEMP%</c> and belongs to the user, so any process running as that user can replace the file
/// during that gap. Carrying the digest forward lets the launch site open the file itself and
/// prove the bytes again at the moment it starts the process, instead of trusting a path string
/// that was checked earlier.
/// </para>
/// </param>
internal readonly record struct BootstrapUiResolution(string? VerifiedPath, string? ExpectedSha256)
{
    /// <summary>The bundle carries no UI payload; the bootstrapper has nothing to launch.</summary>
    internal static BootstrapUiResolution None => new(null, null);
}
