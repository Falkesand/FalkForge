namespace FalkForge.Engine.Variables;

using System.Globalization;
using System.Runtime.InteropServices;
using FalkForge.Platform;

public static class BuiltInVariables
{
    public static void Populate(VariableStore store, IPlatformServices? platform)
    {
        PopulateOsVersion(store);
        PopulateArchitecture(store);
        PopulateFolders(store, platform);
        PopulateSessionInfo(store, platform);
        PopulateUserInfo(store, platform);
        PopulateMsiInfo(store);
        PopulateDateInfo(store);
        PopulateRebootPending(store, platform);
    }

    private static void PopulateOsVersion(VariableStore store)
    {
        var osVersion = System.Environment.OSVersion.Version;
        store.Set(BuiltInVariableNames.VersionNT, osVersion);
        store.Set(BuiltInVariableNames.VersionNTMajor, (long)osVersion.Major);
        store.Set(BuiltInVariableNames.VersionNTMinor, (long)osVersion.Minor);
        store.Set(BuiltInVariableNames.ServicePackLevel, 0L);
        store.Set(BuiltInVariableNames.WindowsBuildNumber, (long)osVersion.Build);
    }

    private static void PopulateArchitecture(VariableStore store)
    {
        var arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X86 => "x86",
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            _ => "unknown"
        };

        store.Set(BuiltInVariableNames.NativeMachine, arch);
        store.Set(BuiltInVariableNames.ProcessorArchitecture, arch);

        var processArch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X86 => "x86",
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            _ => "unknown"
        };
        store.Set(BuiltInVariableNames.ProcessArchitecture, processArch);

        store.Set(BuiltInVariableNames.Is64BitOperatingSystem, RuntimeInformation.OSArchitecture is Architecture.X64 or Architecture.Arm64 ? 1L : 0L);
    }

    private static void PopulateFolders(VariableStore store, IPlatformServices? platform)
    {
        if (platform is null)
        {
            PopulateFoldersFallback(store);
            return;
        }

        var env = platform.Environment;

        store.Set(BuiltInVariableNames.SystemFolder, env.GetFolderPath(System.Environment.SpecialFolder.System));
        store.Set(BuiltInVariableNames.WindowsFolder, env.GetFolderPath(System.Environment.SpecialFolder.Windows));
        store.Set(BuiltInVariableNames.ProgramFilesFolder, env.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles));
        store.Set(BuiltInVariableNames.CommonFilesFolder, env.GetFolderPath(System.Environment.SpecialFolder.CommonProgramFiles));
        store.Set(BuiltInVariableNames.TempFolder, Path.GetTempPath());
        store.Set(BuiltInVariableNames.DesktopFolder, env.GetFolderPath(System.Environment.SpecialFolder.DesktopDirectory));
        store.Set(BuiltInVariableNames.AdminToolsFolder, env.GetFolderPath(System.Environment.SpecialFolder.AdminTools));
        store.Set(BuiltInVariableNames.LocalAppDataFolder, env.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData));
        store.Set(BuiltInVariableNames.AppDataFolder, env.GetFolderPath(System.Environment.SpecialFolder.ApplicationData));
        store.Set(BuiltInVariableNames.StartMenuFolder, env.GetFolderPath(System.Environment.SpecialFolder.StartMenu));
        store.Set(BuiltInVariableNames.StartupFolder, env.GetFolderPath(System.Environment.SpecialFolder.Startup));
        store.Set(BuiltInVariableNames.PersonalFolder, env.GetFolderPath(System.Environment.SpecialFolder.Personal));
        store.Set(BuiltInVariableNames.FontsFolder, env.GetFolderPath(System.Environment.SpecialFolder.Fonts));

        // ProgramFiles64Folder: same as ProgramFilesFolder on 64-bit OS
        if (env.Is64BitOperatingSystem)
        {
            store.Set(BuiltInVariableNames.ProgramFiles64Folder, env.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles));
        }
        else
        {
            store.Set(BuiltInVariableNames.ProgramFiles64Folder, string.Empty);
        }
    }

    private static void PopulateFoldersFallback(VariableStore store)
    {
        store.Set(BuiltInVariableNames.SystemFolder, System.Environment.GetFolderPath(System.Environment.SpecialFolder.System));
        store.Set(BuiltInVariableNames.WindowsFolder, System.Environment.GetFolderPath(System.Environment.SpecialFolder.Windows));
        store.Set(BuiltInVariableNames.ProgramFilesFolder, System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles));
        store.Set(BuiltInVariableNames.CommonFilesFolder, System.Environment.GetFolderPath(System.Environment.SpecialFolder.CommonProgramFiles));
        store.Set(BuiltInVariableNames.TempFolder, Path.GetTempPath());
        store.Set(BuiltInVariableNames.DesktopFolder, System.Environment.GetFolderPath(System.Environment.SpecialFolder.DesktopDirectory));
        store.Set(BuiltInVariableNames.AdminToolsFolder, System.Environment.GetFolderPath(System.Environment.SpecialFolder.AdminTools));
        store.Set(BuiltInVariableNames.LocalAppDataFolder, System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData));
        store.Set(BuiltInVariableNames.AppDataFolder, System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData));
        store.Set(BuiltInVariableNames.StartMenuFolder, System.Environment.GetFolderPath(System.Environment.SpecialFolder.StartMenu));
        store.Set(BuiltInVariableNames.StartupFolder, System.Environment.GetFolderPath(System.Environment.SpecialFolder.Startup));
        store.Set(BuiltInVariableNames.PersonalFolder, System.Environment.GetFolderPath(System.Environment.SpecialFolder.Personal));
        store.Set(BuiltInVariableNames.FontsFolder, System.Environment.GetFolderPath(System.Environment.SpecialFolder.Fonts));
        store.Set(BuiltInVariableNames.ProgramFiles64Folder, System.Environment.Is64BitOperatingSystem
            ? System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles)
            : string.Empty);
    }

    private static void PopulateSessionInfo(VariableStore store, IPlatformServices? platform)
    {
        // Privileged detection: check if running as admin
        // Use platform registry to check for admin access (NativeAOT safe)
        var isAdmin = false;
        if (platform is not null)
        {
            // Try reading a key that requires admin access; if it exists we have registry access
            // This is a heuristic - the real check happens via Windows API in the engine
            isAdmin = platform.Registry.KeyExists("HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion");
        }
        store.Set(BuiltInVariableNames.Privileged, isAdmin ? 1L : 0L);

        // Terminal Server / Remote Desktop detection via registry
        var isTerminalServer = false;
        var isRemoteSession = false;
        if (platform is not null)
        {
            var tsMode = platform.Registry.GetDWordValue("HKLM",
                @"SYSTEM\CurrentControlSet\Control\Terminal Server",
                "TSAppCompat");
            isTerminalServer = tsMode is 1;

            var sessionName = platform.Environment.GetEnvironmentVariable("SESSIONNAME");
            isRemoteSession = sessionName is not null &&
                              !sessionName.StartsWith("Console", StringComparison.OrdinalIgnoreCase);
        }
        store.Set(BuiltInVariableNames.TerminalServer, isTerminalServer ? 1L : 0L);
        store.Set(BuiltInVariableNames.RemoteSession, isRemoteSession ? 1L : 0L);
    }

    private static void PopulateUserInfo(VariableStore store, IPlatformServices? platform)
    {
        if (platform is not null)
        {
            store.Set(BuiltInVariableNames.ComputerName, platform.Environment.MachineName);
        }
        else
        {
            store.Set(BuiltInVariableNames.ComputerName, System.Environment.MachineName);
        }

        var userName = System.Environment.UserName;
        store.Set(BuiltInVariableNames.LogonUser, userName);

        store.Set(BuiltInVariableNames.InstalledCulture, CultureInfo.CurrentCulture.Name);
        store.Set(BuiltInVariableNames.UserLanguageID, (long)CultureInfo.CurrentCulture.LCID);
        store.Set(BuiltInVariableNames.SystemLanguageID, (long)CultureInfo.InstalledUICulture.LCID);
    }

    private static void PopulateMsiInfo(VariableStore store)
    {
        // MSI version we emulate — we report 5.0 (Windows Installer 5.0)
        store.Set(BuiltInVariableNames.VersionMsi, new Version(5, 0));
    }

    private static void PopulateDateInfo(VariableStore store)
    {
        var now = DateTime.UtcNow;
        store.Set(BuiltInVariableNames.Date, now.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
        store.Set(BuiltInVariableNames.Time, now.ToString("HHmmss", CultureInfo.InvariantCulture));
    }

    private static void PopulateRebootPending(VariableStore store, IPlatformServices? platform)
    {
        var rebootPending = false;
        if (platform is not null)
        {
            // Check standard reboot-pending registry keys
            rebootPending =
                platform.Registry.KeyExists("HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending") ||
                platform.Registry.KeyExists("HKLM", @"SYSTEM\CurrentControlSet\Control\Session Manager\PendingFileRenameOperations");
        }
        store.Set(BuiltInVariableNames.RebootPending, rebootPending ? 1L : 0L);
    }
}
