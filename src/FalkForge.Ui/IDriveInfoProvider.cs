namespace FalkForge.Ui;

/// <summary>
/// Abstracts OS drive and filesystem queries so InstallDirPageViewModel can be tested
/// without touching the real filesystem or registry.
/// </summary>
public interface IDriveInfoProvider
{
    /// <summary>Probes whether <paramref name="path"/> (or its nearest existing ancestor) is writable.</summary>
    bool IsWritable(string path);

    /// <summary>Returns available free bytes on the drive hosting <paramref name="path"/>.</summary>
    long GetAvailableFreeSpace(string path);

    /// <summary>
    /// Returns true when the OS long-paths registry key is enabled
    /// (<c>HKLM\SYSTEM\CurrentControlSet\Control\FileSystem\LongPathsEnabled = 1</c>).
    /// </summary>
    bool IsLongPathsEnabled();
}
