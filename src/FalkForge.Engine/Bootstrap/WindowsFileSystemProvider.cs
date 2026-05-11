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
