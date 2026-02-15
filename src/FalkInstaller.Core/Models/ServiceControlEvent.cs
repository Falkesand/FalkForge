namespace FalkInstaller.Models;

[Flags]
public enum ServiceControlEvent
{
    None = 0,
    StartOnInstall = 1,
    StopOnInstall = 2,
    DeleteOnInstall = 8,
    StartOnUninstall = 16,
    StopOnUninstall = 32,
    DeleteOnUninstall = 128
}
