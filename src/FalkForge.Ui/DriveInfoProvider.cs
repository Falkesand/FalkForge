using System.IO;
using Microsoft.Win32;

namespace FalkForge.Ui;

/// <summary>
/// Default production implementation of <see cref="IDriveInfoProvider"/>.
/// Probes writability via a temp-file create/delete, reads free space via
/// <see cref="DriveInfo"/>, and checks the Windows LongPathsEnabled registry key.
/// </summary>
internal sealed class DriveInfoProvider : IDriveInfoProvider
{
    public static readonly DriveInfoProvider Default = new();

    public bool IsWritable(string path)
    {
        try
        {
            // Walk up to first existing ancestor so we can probe even when the target
            // directory does not yet exist.
            var probe = path;
            while (!string.IsNullOrEmpty(probe) && !Directory.Exists(probe))
                probe = Path.GetDirectoryName(probe) ?? string.Empty;

            if (string.IsNullOrEmpty(probe))
                return false;

            var testFile = Path.Combine(probe, $".falkforge_write_probe_{Guid.NewGuid():N}");
            File.WriteAllBytes(testFile, []);
            File.Delete(testFile);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public long GetAvailableFreeSpace(string path)
    {
        try
        {
            // DriveInfo needs a root; resolve from the path.
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root))
                return 0;
            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch
        {
            return 0;
        }
    }

    public bool IsLongPathsEnabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\FileSystem", writable: false);
            return key?.GetValue("LongPathsEnabled") is int v && v == 1;
        }
        catch
        {
            return false;
        }
    }
}
