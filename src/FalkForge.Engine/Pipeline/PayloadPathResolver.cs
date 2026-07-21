namespace FalkForge.Engine.Pipeline;

using System.IO;

/// <summary>
/// Resolves a package's identity to the absolute path of its payload as extracted into the
/// self-extract bootstrapper's cache directory on the target machine, enforcing that the resolved
/// path stays contained within the extraction root.
/// </summary>
/// <remarks>
/// The bootstrapper writes each payload to <c>{payloadRoot}/{PackageId}</c> (see the extraction loop
/// in <c>Program.cs</c>). The package id travels from an attacker-influencable manifest / TOC, so the
/// composed destination is never trusted unguarded: this mirrors the three-layer containment guard in
/// <see cref="FalkForge.Engine.Bootstrap.PreUIPrerequisiteInstaller"/> — the resolved path is verified
/// to resolve strictly under <c>payloadRoot</c> (with a trailing separator) before it is ever handed
/// to an installer. A crafted id (traversal such as <c>..\evil</c>, a rooted path, a UNC / device
/// namespace) resolves outside the root and is rejected with a
/// <see cref="ErrorKind.SecurityError"/> — fail loud, never install from an unverified location.
/// </remarks>
internal static class PayloadPathResolver
{
    /// <summary>
    /// Resolves <paramref name="packageId"/> under <paramref name="payloadRoot"/> and verifies
    /// containment. Returns the absolute extracted-payload path on success, or a
    /// <see cref="ErrorKind.SecurityError"/> failure when the id escapes the root.
    /// </summary>
    public static Result<string> Resolve(string payloadRoot, string packageId)
    {
        if (string.IsNullOrWhiteSpace(packageId))
            return Result<string>.Failure(
                ErrorKind.SecurityError,
                "Package id is empty; cannot resolve its extracted payload path.");

        // Full-path both sides so obfuscated traversal (e.g. sub\..\..\evil) is normalised before
        // the containment comparison; Path.Combine discards the root for a rooted/UNC id, which the
        // containment check below then rejects.
        var root = Path.GetFullPath(payloadRoot);
        var resolved = Path.GetFullPath(Path.Combine(root, packageId));

        var containmentBase = root + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(containmentBase, StringComparison.OrdinalIgnoreCase))
            return Result<string>.Failure(
                ErrorKind.SecurityError,
                $"Package id '{packageId}' resolves outside the payload extraction root '{root}'. " +
                "Rejecting to prevent installing from an out-of-cache location.");

        return resolved;
    }
}
