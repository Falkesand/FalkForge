using System.Diagnostics;
using System.IO;

namespace FalkForge.TestSupport;

/// <summary>
/// Creates and repoints NTFS directory junctions from tests, so a test can build the real
/// path-swap attack the elevation crossings are hardened against.
/// <para>
/// Creating a junction needs no privilege and no Developer Mode, unlike a symbolic link, which is
/// why the attack is reachable by an ordinary same-user process in the first place. There is no
/// managed API for junctions (<see cref="Directory.CreateSymbolicLink(string, string)"/> creates a
/// symlink, which does need a privilege), so this shells out to <c>mklink /J</c>.
/// </para>
/// <para>
/// Deleting a junction with <see cref="Directory.Delete(string)"/> removes the reparse point only.
/// The files under the junction's target are untouched, which is exactly why an open handle to a
/// file reached through a junction does not stop the junction from being repointed.
/// </para>
/// </summary>
public static class TestJunction
{
    /// <summary>
    /// Creates a junction at <paramref name="linkPath"/> pointing at <paramref name="targetPath"/>.
    /// Returns <see langword="false"/> when the platform or the environment will not create one,
    /// so the caller can skip rather than assert something weaker.
    /// </summary>
    /// <param name="linkPath">Directory path to create as a junction. Must not already exist.</param>
    /// <param name="targetPath">Existing directory the junction should resolve to.</param>
    public static bool TryCreate(string linkPath, string targetPath)
    {
        if (!OperatingSystem.IsWindows())
            return false;

        var startInfo = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("mklink");
        startInfo.ArgumentList.Add("/J");
        startInfo.ArgumentList.Add(linkPath);
        startInfo.ArgumentList.Add(targetPath);

        using var process = Process.Start(startInfo);
        if (process is null)
            return false;

        process.WaitForExit();
        return process.ExitCode == 0 && Directory.Exists(linkPath);
    }

    /// <summary>
    /// Points an existing junction at a different directory: deletes the reparse point and
    /// recreates it. This is the move an attacker makes while the victim holds an open handle.
    /// </summary>
    /// <param name="linkPath">The existing junction to repoint.</param>
    /// <param name="newTargetPath">The directory it should resolve to afterwards.</param>
    /// <exception cref="InvalidOperationException">The junction could not be recreated.</exception>
    public static void Repoint(string linkPath, string newTargetPath)
    {
        Directory.Delete(linkPath);
        if (!TryCreate(linkPath, newTargetPath))
            throw new InvalidOperationException($"Could not repoint the junction '{linkPath}' at '{newTargetPath}'.");
    }
}
