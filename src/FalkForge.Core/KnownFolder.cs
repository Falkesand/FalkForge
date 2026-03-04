namespace FalkForge;

public sealed class KnownFolder
{
    private KnownFolder(string token, string displayName)
    {
        Token = token;
        DisplayName = displayName;
    }

    public string Token { get; }
    public string DisplayName { get; }

    // Standard MSI directory tokens
    public static KnownFolder ProgramFiles { get; } = new("ProgramFilesFolder", "Program Files");
    public static KnownFolder ProgramFiles64 { get; } = new("ProgramFiles64Folder", "Program Files (64-bit)");
    public static KnownFolder CommonAppData { get; } = new("CommonAppDataFolder", "ProgramData");
    public static KnownFolder LocalAppData { get; } = new("LocalAppDataFolder", "Local AppData");
    public static KnownFolder AppData { get; } = new("AppDataFolder", "AppData");
    public static KnownFolder SystemFolder { get; } = new("SystemFolder", "System32");
    public static KnownFolder System64Folder { get; } = new("System64Folder", "System64");
    public static KnownFolder WindowsFolder { get; } = new("WindowsFolder", "Windows");
    public static KnownFolder TempFolder { get; } = new("TempFolder", "Temp");
    public static KnownFolder DesktopFolder { get; } = new("DesktopFolder", "Desktop");
    public static KnownFolder StartMenuFolder { get; } = new("StartMenuFolder", "Start Menu");
    public static KnownFolder ProgramMenuFolder { get; } = new("ProgramMenuFolder", "Programs");
    public static KnownFolder StartupFolder { get; } = new("StartupFolder", "Startup");
    public static KnownFolder CommonFilesFolder { get; } = new("CommonFilesFolder", "Common Files");
    public static KnownFolder CommonFiles64Folder { get; } = new("CommonFiles64Folder", "Common Files (64-bit)");
    public static KnownFolder FontsFolder { get; } = new("FontsFolder", "Fonts");

    public InstallPath this[string subPath] => new(this, subPath);

    public static InstallPath operator /(KnownFolder folder, string subPath)
    {
        return new InstallPath(folder, subPath);
    }

    public override string ToString()
    {
        return $"[{Token}]";
    }
}