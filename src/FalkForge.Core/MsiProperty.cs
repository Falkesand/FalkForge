namespace FalkForge;

public sealed class MsiProperty : IEquatable<MsiProperty>
{
    private MsiProperty(string name)
    {
        Name = name;
    }

    public string Name { get; }

    // ── Product ──

    public static MsiProperty ProductName { get; } = new("ProductName");
    public static MsiProperty ProductCode { get; } = new("ProductCode");
    public static MsiProperty ProductVersion { get; } = new("ProductVersion");
    public static MsiProperty ProductLanguage { get; } = new("ProductLanguage");
    public static MsiProperty Manufacturer { get; } = new("Manufacturer");
    public static MsiProperty UpgradeCode { get; } = new("UpgradeCode");

    // ── Directories ──

    public static MsiProperty InstallFolder { get; } = new("INSTALLFOLDER");
    public static MsiProperty InstallDir { get; } = new("INSTALLDIR");
    public static MsiProperty TargetDir { get; } = new("TARGETDIR");

    // ── OS Version ──

    public static MsiProperty VersionNT { get; } = new("VersionNT");
    public static MsiProperty VersionNT64 { get; } = new("VersionNT64");
    public static MsiProperty ServicePackLevel { get; } = new("ServicePackLevel");
    public static MsiProperty WindowsBuildNumber { get; } = new("WindowsBuildNumber");

    // ── Architecture ──

    public static MsiProperty Intel { get; } = new("Intel");
    public static MsiProperty Intel64 { get; } = new("Intel64");
    public static MsiProperty MsiAMD64 { get; } = new("MsiAMD64");
    public static MsiProperty Msix64 { get; } = new("Msix64");

    // ── System Folders ──

    public static MsiProperty ProgramFilesFolder { get; } = new("ProgramFilesFolder");
    public static MsiProperty ProgramFiles64Folder { get; } = new("ProgramFiles64Folder");
    public static MsiProperty CommonFilesFolder { get; } = new("CommonFilesFolder");
    public static MsiProperty SystemFolder { get; } = new("SystemFolder");
    public static MsiProperty System64Folder { get; } = new("System64Folder");
    public static MsiProperty WindowsFolder { get; } = new("WindowsFolder");
    public static MsiProperty TempFolder { get; } = new("TempFolder");
    public static MsiProperty AppDataFolder { get; } = new("AppDataFolder");
    public static MsiProperty LocalAppDataFolder { get; } = new("LocalAppDataFolder");
    public static MsiProperty CommonAppDataFolder { get; } = new("CommonAppDataFolder");
    public static MsiProperty DesktopFolder { get; } = new("DesktopFolder");
    public static MsiProperty StartMenuFolder { get; } = new("StartMenuFolder");
    public static MsiProperty ProgramMenuFolder { get; } = new("ProgramMenuFolder");
    public static MsiProperty StartupFolder { get; } = new("StartupFolder");
    public static MsiProperty FontsFolder { get; } = new("FontsFolder");
    public static MsiProperty PersonalFolder { get; } = new("PersonalFolder");

    // ── Session ──

    public static MsiProperty Privileged { get; } = new("Privileged");
    public static MsiProperty AdminUser { get; } = new("AdminUser");
    public static MsiProperty TerminalServer { get; } = new("TerminalServer");
    public static MsiProperty RemoteAdminTS { get; } = new("RemoteAdminTS");

    // ── User ──

    public static MsiProperty ComputerName { get; } = new("ComputerName");
    public static MsiProperty LogonUser { get; } = new("LogonUser");
    public static MsiProperty UserSID { get; } = new("UserSID");

    // ── State ──

    public static MsiProperty Installed { get; } = new("Installed");
    public static MsiProperty UILevel { get; } = new("UILevel");
    public static MsiProperty REMOVE { get; } = new("REMOVE");
    public static MsiProperty REINSTALL { get; } = new("REINSTALL");
    public static MsiProperty CustomActionData { get; } = new("CustomActionData");

    // ── Equality ──

    public bool Equals(MsiProperty? other)
    {
        return other is not null && Name == other.Name;
    }

    public static MsiProperty Custom(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new MsiProperty(name);
    }

    // ── ToString ──

    public override string ToString()
    {
        return $"[{Name}]";
    }

    // ── / operator: MsiProperty / "subpath" → "[NAME]subpath" string ──

    public static string operator /(MsiProperty property, string subPath)
    {
        return $"[{property.Name}]{subPath}";
    }

    // ── Comparison operators returning Condition ──
    // MSI uses single = for equality and <> for not-equal.
    // String values are quoted; integer values are not.

    public static Condition operator ==(MsiProperty property, string value)
    {
        return new Condition($"{property.Name} = \"{value}\"");
    }

    public static Condition operator !=(MsiProperty property, string value)
    {
        return new Condition($"{property.Name} <> \"{value}\"");
    }

    public static Condition operator ==(MsiProperty property, int value)
    {
        return new Condition($"{property.Name} = {value}");
    }

    public static Condition operator !=(MsiProperty property, int value)
    {
        return new Condition($"{property.Name} <> {value}");
    }

    public static Condition operator >(MsiProperty property, int value)
    {
        return new Condition($"{property.Name} > {value}");
    }

    public static Condition operator <(MsiProperty property, int value)
    {
        return new Condition($"{property.Name} < {value}");
    }

    public static Condition operator >=(MsiProperty property, int value)
    {
        return new Condition($"{property.Name} >= {value}");
    }

    public static Condition operator <=(MsiProperty property, int value)
    {
        return new Condition($"{property.Name} <= {value}");
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as MsiProperty);
    }

    public override int GetHashCode()
    {
        return Name.GetHashCode();
    }
}