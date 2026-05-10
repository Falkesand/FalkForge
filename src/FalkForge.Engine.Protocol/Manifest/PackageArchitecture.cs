namespace FalkForge.Engine.Protocol.Manifest;

/// <summary>
/// Processor architecture required by an installer package.
/// Used at plan time to reject packages that cannot run on the host OS,
/// surfacing an <see cref="FalkForge.ErrorKind.ArchitectureMismatch"/> error
/// before MSI error 1603 (fatal error during installation) appears at apply time.
/// </summary>
public enum PackageArchitecture
{
    /// <summary>
    /// No architecture constraint — runs on any host.
    /// Equivalent to "AnyCPU" or architecture-neutral installers.
    /// </summary>
    Neutral = 0,

    /// <summary>32-bit x86.</summary>
    X86 = 1,

    /// <summary>64-bit x86-64 (AMD64).</summary>
    X64 = 2,

    /// <summary>64-bit ARM (AArch64).</summary>
    Arm64 = 3
}
