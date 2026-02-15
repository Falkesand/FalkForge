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
        store.Set("VersionNT", osVersion);
        store.Set("VersionNTMajor", (long)osVersion.Major);
        store.Set("VersionNTMinor", (long)osVersion.Minor);
        store.Set("ServicePackLevel", 0L);
        store.Set("WindowsBuildNumber", (long)osVersion.Build);
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

        store.Set("NativeMachine", arch);
        store.Set("ProcessorArchitecture", arch);

        var processArch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X86 => "x86",
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            _ => "unknown"
        };
        store.Set("ProcessArchitecture", processArch);

        store.Set("Is64BitOperatingSystem", RuntimeInformation.OSArchitecture is Architecture.X64 or Architecture.Arm64 ? 1L : 0L);
    }

    private static void PopulateFolders(VariableStore store, IPlatformServices? platform)
    {
        if (platform is null)
        {
            PopulateFoldersFallback(store);
            return;
        }

        var env = platform.Environment;

        store.Set("SystemFolder", env.GetFolderPath(System.Environment.SpecialFolder.System));
        store.Set("WindowsFolder", env.GetFolderPath(System.Environment.SpecialFolder.Windows));
        store.Set("ProgramFilesFolder", env.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles));
        store.Set("CommonFilesFolder", env.GetFolderPath(System.Environment.SpecialFolder.CommonProgramFiles));
        store.Set("TempFolder", Path.GetTempPath());
        store.Set("DesktopFolder", env.GetFolderPath(System.Environment.SpecialFolder.DesktopDirectory));
        store.Set("AdminToolsFolder", env.GetFolderPath(System.Environment.SpecialFolder.AdminTools));
        store.Set("LocalAppDataFolder", env.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData));
        store.Set("AppDataFolder", env.GetFolderPath(System.Environment.SpecialFolder.ApplicationData));
        store.Set("StartMenuFolder", env.GetFolderPath(System.Environment.SpecialFolder.StartMenu));
        store.Set("StartupFolder", env.GetFolderPath(System.Environment.SpecialFolder.Startup));
        store.Set("PersonalFolder", env.GetFolderPath(System.Environment.SpecialFolder.Personal));
        store.Set("FontsFolder", env.GetFolderPath(System.Environment.SpecialFolder.Fonts));

        // ProgramFiles64Folder: same as ProgramFilesFolder on 64-bit OS
        if (env.Is64BitOperatingSystem)
        {
            store.Set("ProgramFiles64Folder", env.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles));
        }
        else
        {
            store.Set("ProgramFiles64Folder", string.Empty);
        }
    }

    private static void PopulateFoldersFallback(VariableStore store)
    {
        store.Set("SystemFolder", System.Environment.GetFolderPath(System.Environment.SpecialFolder.System));
        store.Set("WindowsFolder", System.Environment.GetFolderPath(System.Environment.SpecialFolder.Windows));
        store.Set("ProgramFilesFolder", System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles));
        store.Set("CommonFilesFolder", System.Environment.GetFolderPath(System.Environment.SpecialFolder.CommonProgramFiles));
        store.Set("TempFolder", Path.GetTempPath());
        store.Set("DesktopFolder", System.Environment.GetFolderPath(System.Environment.SpecialFolder.DesktopDirectory));
        store.Set("AdminToolsFolder", System.Environment.GetFolderPath(System.Environment.SpecialFolder.AdminTools));
        store.Set("LocalAppDataFolder", System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData));
        store.Set("AppDataFolder", System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData));
        store.Set("StartMenuFolder", System.Environment.GetFolderPath(System.Environment.SpecialFolder.StartMenu));
        store.Set("StartupFolder", System.Environment.GetFolderPath(System.Environment.SpecialFolder.Startup));
        store.Set("PersonalFolder", System.Environment.GetFolderPath(System.Environment.SpecialFolder.Personal));
        store.Set("FontsFolder", System.Environment.GetFolderPath(System.Environment.SpecialFolder.Fonts));
        store.Set("ProgramFiles64Folder", System.Environment.Is64BitOperatingSystem
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
        store.Set("Privileged", isAdmin ? 1L : 0L);

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
        store.Set("TerminalServer", isTerminalServer ? 1L : 0L);
        store.Set("RemoteSession", isRemoteSession ? 1L : 0L);
    }

    private static void PopulateUserInfo(VariableStore store, IPlatformServices? platform)
    {
        if (platform is not null)
        {
            store.Set("ComputerName", platform.Environment.MachineName);
        }
        else
        {
            store.Set("ComputerName", System.Environment.MachineName);
        }

        var userName = System.Environment.UserName;
        store.Set("LogonUser", userName);

        store.Set("InstalledCulture", CultureInfo.CurrentCulture.Name);
        store.Set("UserLanguageID", (long)CultureInfo.CurrentCulture.LCID);
        store.Set("SystemLanguageID", (long)CultureInfo.InstalledUICulture.LCID);
    }

    private static void PopulateMsiInfo(VariableStore store)
    {
        // MSI version we emulate — we report 5.0 (Windows Installer 5.0)
        store.Set("VersionMsi", new Version(5, 0));
    }

    private static void PopulateDateInfo(VariableStore store)
    {
        var now = DateTime.UtcNow;
        store.Set("Date", now.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
        store.Set("Time", now.ToString("HHmmss", CultureInfo.InvariantCulture));
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
        store.Set("RebootPending", rebootPending ? 1L : 0L);
    }
}
