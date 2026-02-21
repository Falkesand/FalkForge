using FalkForge;
using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Engine.Protocol.Manifest;

namespace FalkForge.Decompiler;

/// <summary>
/// Abstracts bundle file reading for testability.
/// Follows the <see cref="IMsiTableAccess"/> pattern.
/// </summary>
public interface IBundleAccess : IDisposable
{
    /// <summary>
    /// Reads the installer manifest from the bundle.
    /// </summary>
    /// <returns>The manifest if successful; otherwise a failure result.</returns>
    Result<InstallerManifest> ReadManifest();

    /// <summary>
    /// Reads the table of contents (TOC) from the bundle.
    /// </summary>
    /// <returns>An array of TOC entries if successful; otherwise a failure result.</returns>
    Result<TocEntry[]> ReadToc();
}
