using System.Runtime.Versioning;

namespace FalkInstaller.Platform.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsEnvironment : IEnvironment
{
    public string MachineName => System.Environment.MachineName;
    public bool Is64BitOperatingSystem => System.Environment.Is64BitOperatingSystem;

    public string? GetEnvironmentVariable(string name) =>
        System.Environment.GetEnvironmentVariable(name);

    public string GetFolderPath(System.Environment.SpecialFolder folder) =>
        System.Environment.GetFolderPath(folder);
}
