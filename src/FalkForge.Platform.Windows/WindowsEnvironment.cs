using System.Runtime.Versioning;
using System.Security.Principal;

namespace FalkForge.Platform.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsEnvironment : IEnvironment
{
    public string MachineName => System.Environment.MachineName;
    public bool Is64BitOperatingSystem => System.Environment.Is64BitOperatingSystem;

    /// <summary>
    /// The standard .NET elevation check: the current process's Windows token is elevated
    /// (UAC administrator) when it is a member of the Administrators role. This is the same
    /// mechanism the repo's execution-step e2e tests use to gate real per-machine installs
    /// (<c>WindowsIdentity.GetCurrent()</c> + <c>WindowsPrincipal.IsInRole</c>).
    /// </summary>
    public bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public string? GetEnvironmentVariable(string name) =>
        System.Environment.GetEnvironmentVariable(name);

    public string GetFolderPath(System.Environment.SpecialFolder folder) =>
        System.Environment.GetFolderPath(folder);
}
