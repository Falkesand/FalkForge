namespace FalkInstaller.Engine.Journal;

public enum JournalEntryType
{
    PackageInstalled,
    PackageUninstalled,
    FileCreated,
    FileModified,
    RegistryKeyCreated,
    RegistryValueSet,
    ServiceInstalled
}
