namespace FalkForge.Platform;

public interface IEnvironment
{
    string MachineName { get; }
    bool Is64BitOperatingSystem { get; }

    /// <summary>
    /// <see langword="true"/> when the current process token is elevated (UAC administrator);
    /// <see langword="false"/> otherwise. The production implementation answers this via
    /// <c>WindowsIdentity</c>/<c>WindowsPrincipal.IsInRole(WindowsBuiltInRole.Administrator)</c> —
    /// the standard .NET elevation check — not a registry ACL probe.
    /// </summary>
    bool IsElevated { get; }

    string? GetEnvironmentVariable(string name);
    string GetFolderPath(System.Environment.SpecialFolder folder);
}
