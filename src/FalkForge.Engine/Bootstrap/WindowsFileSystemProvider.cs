namespace FalkForge.Engine.Bootstrap;

using System.Diagnostics;
using FalkForge.Engine.Detection;

/// <summary>
/// Production implementation of <see cref="IFileSystemProvider"/> that delegates to the
/// real Windows file system. Used by <see cref="PreUIPrerequisiteDetector"/> in the
/// NativeAOT bootstrapper.
/// AOT-safe: no reflection, no dynamic loading.
/// </summary>
internal sealed class WindowsFileSystemProvider : IFileSystemProvider
{
    /// <summary>Singleton instance for use in the bootstrapper hot path.</summary>
    internal static readonly WindowsFileSystemProvider Instance = new();

    private WindowsFileSystemProvider() { }

    /// <inheritdoc/>
    public bool FileExists(string path) => File.Exists(path);

    /// <inheritdoc/>
    public bool DirectoryExists(string path) => Directory.Exists(path);

    /// <inheritdoc/>
    public IReadOnlyList<string> GetDirectories(string path)
    {
        if (!Directory.Exists(path))
            return [];

        try
        {
            return Directory.GetDirectories(path);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // Directory.Exists only needs traverse + read-attributes on the parent -- a directory
            // whose ACL denies list access (a hardened enterprise image is a real example) still
            // passes that check above and then throws UnauthorizedAccessException here. A directory
            // removed between the Exists check and this call throws DirectoryNotFoundException,
            // a subtype of IOException. Both degrade to the same empty result the "missing
            // directory" branch above already returns, matching this interface's never-throw
            // contract and the WindowsRegistry.TryKeyExists precedent for ACL-denied reads.
            return [];
        }
    }

    /// <inheritdoc/>
    public Version? GetFileVersion(string path)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            return info.FileVersion is { Length: > 0 } ver && Version.TryParse(ver, out var v) ? v : null;
        }
        catch
        {
            return null;
        }
    }
}
