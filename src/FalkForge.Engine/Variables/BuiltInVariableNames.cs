namespace FalkForge.Engine.Variables;

internal static class BuiltInVariableNames
{
    // OS version
    public const string VersionNT = "VersionNT";
    public const string VersionNTMajor = "VersionNTMajor";
    public const string VersionNTMinor = "VersionNTMinor";
    public const string ServicePackLevel = "ServicePackLevel";
    public const string WindowsBuildNumber = "WindowsBuildNumber";

    // Architecture
    public const string NativeMachine = "NativeMachine";
    public const string ProcessorArchitecture = "ProcessorArchitecture";
    public const string ProcessArchitecture = "ProcessArchitecture";
    public const string Is64BitOperatingSystem = "Is64BitOperatingSystem";

    // Folders
    public const string SystemFolder = "SystemFolder";
    public const string WindowsFolder = "WindowsFolder";
    public const string ProgramFilesFolder = "ProgramFilesFolder";
    public const string CommonFilesFolder = "CommonFilesFolder";
    public const string TempFolder = "TempFolder";
    public const string DesktopFolder = "DesktopFolder";
    public const string AdminToolsFolder = "AdminToolsFolder";
    public const string LocalAppDataFolder = "LocalAppDataFolder";
    public const string AppDataFolder = "AppDataFolder";
    public const string StartMenuFolder = "StartMenuFolder";
    public const string StartupFolder = "StartupFolder";
    public const string PersonalFolder = "PersonalFolder";
    public const string FontsFolder = "FontsFolder";
    public const string ProgramFiles64Folder = "ProgramFiles64Folder";

    // Session
    public const string Privileged = "Privileged";
    public const string TerminalServer = "TerminalServer";
    public const string RemoteSession = "RemoteSession";

    // User
    public const string ComputerName = "ComputerName";
    public const string LogonUser = "LogonUser";
    public const string InstalledCulture = "InstalledCulture";
    public const string UserLanguageID = "UserLanguageID";
    public const string SystemLanguageID = "SystemLanguageID";

    // MSI
    public const string VersionMsi = "VersionMsi";

    // Date/Time
    public const string Date = "Date";
    public const string Time = "Time";

    // Reboot
    public const string RebootPending = "RebootPending";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        VersionNT, VersionNTMajor, VersionNTMinor, ServicePackLevel, WindowsBuildNumber,
        NativeMachine, ProcessorArchitecture, ProcessArchitecture, Is64BitOperatingSystem,
        SystemFolder, WindowsFolder, ProgramFilesFolder, CommonFilesFolder, TempFolder,
        DesktopFolder, AdminToolsFolder, LocalAppDataFolder, AppDataFolder,
        StartMenuFolder, StartupFolder, PersonalFolder, FontsFolder, ProgramFiles64Folder,
        Privileged, TerminalServer, RemoteSession, ComputerName, LogonUser,
        InstalledCulture, UserLanguageID, SystemLanguageID, VersionMsi,
        Date, Time, RebootPending
    };
}
