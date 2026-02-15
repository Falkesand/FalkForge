namespace FalkInstaller.Engine.Journal;

public enum JournalEntryType
{
    PackageInstalled,
    PackageUninstalled,
    FileCreated,
    FileModified,
    RegistryKeyCreated,
    RegistryValueSet,
    ServiceInstalled,
    SegmentBoundary,
    MsiInstalled,
    ExeInstalled,
    PayloadCached,
    RegistryModified
}
