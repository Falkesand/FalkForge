namespace FalkForge.Compiler.Msi.Tables;

// MSI SQL syntax: no comma before PRIMARY KEY clause. This is intentional and matches
// the Windows Installer SQL Reference (MsiDatabaseOpenView). Standard SQL uses a comma
// before PRIMARY KEY, but the MSI SQL dialect does not.
//
// All SQL statements MUST be single-line. The msi.dll SQL engine (ERROR_BAD_QUERY_SYNTAX / 1615)
// does not tolerate multi-line strings or excessive whitespace.
internal static class MsiTableDefinitions
{
    internal const string CreateDirectoryTable =
        "CREATE TABLE `Directory` (`Directory` CHAR(72) NOT NULL, `Directory_Parent` CHAR(72), `DefaultDir` CHAR(255) NOT NULL LOCALIZABLE PRIMARY KEY `Directory`)";

    internal const string CreateComponentTable =
        "CREATE TABLE `Component` (`Component` CHAR(72) NOT NULL, `ComponentId` CHAR(38), `Directory_` CHAR(72) NOT NULL, `Attributes` SHORT NOT NULL, `Condition` CHAR(255), `KeyPath` CHAR(72) PRIMARY KEY `Component`)";

    internal const string CreateFileTable =
        "CREATE TABLE `File` (`File` CHAR(72) NOT NULL, `Component_` CHAR(72) NOT NULL, `FileName` CHAR(255) NOT NULL LOCALIZABLE, `FileSize` LONG NOT NULL, `Version` CHAR(72), `Language` CHAR(20), `Attributes` SHORT, `Sequence` SHORT NOT NULL PRIMARY KEY `File`)";

    internal const string CreateFeatureTable =
        "CREATE TABLE `Feature` (`Feature` CHAR(38) NOT NULL, `Feature_Parent` CHAR(38), `Title` CHAR(64) LOCALIZABLE, `Description` CHAR(255) LOCALIZABLE, `Display` SHORT, `Level` SHORT NOT NULL, `Directory_` CHAR(72), `Attributes` SHORT NOT NULL PRIMARY KEY `Feature`)";

    internal const string CreateFeatureComponentsTable =
        "CREATE TABLE `FeatureComponents` (`Feature_` CHAR(38) NOT NULL, `Component_` CHAR(72) NOT NULL PRIMARY KEY `Feature_`, `Component_`)";

    internal const string CreateMediaTable =
        "CREATE TABLE `Media` (`DiskId` SHORT NOT NULL, `LastSequence` SHORT NOT NULL, `DiskPrompt` CHAR(64) LOCALIZABLE, `Cabinet` CHAR(255), `VolumeLabel` CHAR(32), `Source` CHAR(72) PRIMARY KEY `DiskId`)";

    internal const string CreatePropertyTable =
        "CREATE TABLE `Property` (`Property` CHAR(72) NOT NULL, `Value` LONGCHAR NOT NULL LOCALIZABLE PRIMARY KEY `Property`)";

    internal const string CreateRegistryTable =
        "CREATE TABLE `Registry` (`Registry` CHAR(72) NOT NULL, `Root` SHORT NOT NULL, `Key` CHAR(255) NOT NULL LOCALIZABLE, `Name` CHAR(255) LOCALIZABLE, `Value` LONGCHAR LOCALIZABLE, `Component_` CHAR(72) NOT NULL PRIMARY KEY `Registry`)";

    internal const string CreateShortcutTable =
        "CREATE TABLE `Shortcut` (`Shortcut` CHAR(72) NOT NULL, `Directory_` CHAR(72) NOT NULL, `Name` CHAR(128) NOT NULL LOCALIZABLE, `Component_` CHAR(72) NOT NULL, `Target` CHAR(255) NOT NULL, `Arguments` CHAR(255), `Description` CHAR(255) LOCALIZABLE, `Hotkey` SHORT, `Icon_` CHAR(72), `IconIndex` SHORT, `ShowCmd` SHORT, `WkDir` CHAR(72) PRIMARY KEY `Shortcut`)";

    internal const string CreateServiceInstallTable =
        "CREATE TABLE `ServiceInstall` (`ServiceInstall` CHAR(72) NOT NULL, `Name` CHAR(255) NOT NULL, `DisplayName` CHAR(255) LOCALIZABLE, `ServiceType` LONG NOT NULL, `StartType` LONG NOT NULL, `ErrorControl` LONG NOT NULL, `LoadOrderGroup` CHAR(255), `Dependencies` CHAR(255), `StartName` CHAR(255), `Password` CHAR(255), `Arguments` CHAR(255), `Component_` CHAR(72) NOT NULL, `Description` CHAR(255) LOCALIZABLE PRIMARY KEY `ServiceInstall`)";

    internal const string CreateServiceControlTable =
        "CREATE TABLE `ServiceControl` (`ServiceControl` CHAR(72) NOT NULL, `Name` CHAR(255) NOT NULL, `Event` SHORT NOT NULL, `Arguments` CHAR(255), `Wait` SHORT, `Component_` CHAR(72) NOT NULL PRIMARY KEY `ServiceControl`)";

    internal const string CreateMsiServiceConfigFailureActionsTable =
        "CREATE TABLE `MsiServiceConfigFailureActions` (`MsiServiceConfigFailureActions` CHAR(72) NOT NULL, `Name` CHAR(255) NOT NULL, `Event` LONG NOT NULL, `ResetPeriod` LONG, `RebootMessage` CHAR(255), `Command` CHAR(255), `Actions` CHAR(255), `DelayActions` CHAR(255), `Component_` CHAR(72) NOT NULL PRIMARY KEY `MsiServiceConfigFailureActions`)";

    internal const string CreateUpgradeTable =
        "CREATE TABLE `Upgrade` (`UpgradeCode` CHAR(38) NOT NULL, `VersionMin` CHAR(20), `VersionMax` CHAR(20), `Language` CHAR(255), `Attributes` LONG NOT NULL, `Remove` CHAR(255), `ActionProperty` CHAR(72) NOT NULL PRIMARY KEY `UpgradeCode`, `VersionMin`, `VersionMax`, `Language`, `Attributes`)";

    internal const string CreateLaunchConditionTable =
        "CREATE TABLE `LaunchCondition` (`Condition` CHAR(255) NOT NULL, `Description` CHAR(255) NOT NULL LOCALIZABLE PRIMARY KEY `Condition`)";

    internal const string CreateInstallExecuteSequenceTable =
        "CREATE TABLE `InstallExecuteSequence` (`Action` CHAR(72) NOT NULL, `Condition` CHAR(255), `Sequence` SHORT PRIMARY KEY `Action`)";

    internal const string CreateInstallUISequenceTable =
        "CREATE TABLE `InstallUISequence` (`Action` CHAR(72) NOT NULL, `Condition` CHAR(255), `Sequence` SHORT PRIMARY KEY `Action`)";

    internal const string CreateEnvironmentTable =
        "CREATE TABLE `Environment` (`Environment` CHAR(72) NOT NULL, `Name` CHAR(255) NOT NULL LOCALIZABLE, `Value` LONGCHAR LOCALIZABLE, `Component_` CHAR(72) NOT NULL PRIMARY KEY `Environment`)";

    internal const string CreateFontTable =
        "CREATE TABLE `Font` (`File_` CHAR(72) NOT NULL, `FontTitle` CHAR(128) PRIMARY KEY `File_`)";

    internal const string CreateIniFileTable =
        "CREATE TABLE `IniFile` (`IniFile` CHAR(72) NOT NULL, `FileName` CHAR(255) NOT NULL LOCALIZABLE, `DirProperty` CHAR(72), `Section` CHAR(96) NOT NULL LOCALIZABLE, `Key` CHAR(128) NOT NULL LOCALIZABLE, `Value` CHAR(255) LOCALIZABLE, `Action` SHORT NOT NULL, `Component_` CHAR(72) NOT NULL PRIMARY KEY `IniFile`)";

    internal const string CreateRemoveIniFileTable =
        "CREATE TABLE `RemoveIniFile` (`RemoveIniFile` CHAR(72) NOT NULL, `FileName` CHAR(255) NOT NULL LOCALIZABLE, `DirProperty` CHAR(72), `Section` CHAR(96) NOT NULL LOCALIZABLE, `Key` CHAR(128) NOT NULL LOCALIZABLE, `Value` CHAR(255) LOCALIZABLE, `Action` SHORT NOT NULL, `Component_` CHAR(72) NOT NULL PRIMARY KEY `RemoveIniFile`)";

    internal const string CreateLockPermissionsTable =
        "CREATE TABLE `LockPermissions` (`LockObject` CHAR(72) NOT NULL, `Table` CHAR(32) NOT NULL, `Domain` CHAR(255), `User` CHAR(255) NOT NULL, `Permission` LONG PRIMARY KEY `LockObject`, `Table`, `Domain`, `User`)";

    internal const string CreateMsiLockPermissionsExTable =
        "CREATE TABLE `MsiLockPermissionsEx` (`MsiLockPermissionsEx` CHAR(72) NOT NULL, `LockObject` CHAR(72) NOT NULL, `Table` CHAR(32) NOT NULL, `SDDLText` CHAR(255) NOT NULL, `Condition` CHAR(255) PRIMARY KEY `MsiLockPermissionsEx`)";

    internal const string CreateExtensionTable =
        "CREATE TABLE `Extension` (`Extension` CHAR(255) NOT NULL, `Component_` CHAR(72) NOT NULL, `ProgId_` CHAR(255), `MIME_` CHAR(64), `Feature_` CHAR(38) NOT NULL PRIMARY KEY `Extension`, `Component_`)";

    internal const string CreateVerbTable =
        "CREATE TABLE `Verb` (`Extension_` CHAR(255) NOT NULL, `Verb` CHAR(32) NOT NULL, `Sequence` SHORT, `Command` CHAR(255) LOCALIZABLE, `Argument` CHAR(255) LOCALIZABLE PRIMARY KEY `Extension_`, `Verb`)";

    internal const string CreateMimeTable =
        "CREATE TABLE `MIME` (`ContentType` CHAR(64) NOT NULL, `Extension_` CHAR(255) NOT NULL, `CLSID` CHAR(38) PRIMARY KEY `ContentType`)";

    internal const string CreateProgIdTable =
        "CREATE TABLE `ProgId` (`ProgId` CHAR(255) NOT NULL, `ProgId_Parent` CHAR(255), `Class_` CHAR(38), `Description` CHAR(255) LOCALIZABLE, `Icon_` CHAR(72), `IconIndex` SHORT PRIMARY KEY `ProgId`)";

    internal const string CreateCustomActionTable =
        "CREATE TABLE `CustomAction` (`Action` CHAR(72) NOT NULL, `Type` SHORT NOT NULL, `Source` CHAR(72), `Target` CHAR(255), `ExtendedType` LONG PRIMARY KEY `Action`)";

    internal const string CreateBinaryTable =
        "CREATE TABLE `Binary` (`Name` CHAR(72) NOT NULL, `Data` OBJECT NOT NULL PRIMARY KEY `Name`)";

    internal const string CreateIconTable =
        "CREATE TABLE `Icon` (`Name` CHAR(72) NOT NULL, `Data` OBJECT NOT NULL PRIMARY KEY `Name`)";

    internal const string CreateRemoveRegistryTable =
        "CREATE TABLE `RemoveRegistry` (`RemoveRegistry` CHAR(72) NOT NULL, `Root` SHORT NOT NULL, `Key` CHAR(255) NOT NULL LOCALIZABLE, `Name` CHAR(255) LOCALIZABLE, `Component_` CHAR(72) NOT NULL PRIMARY KEY `RemoveRegistry`)";

    internal const string CreateRemoveFileTable =
        "CREATE TABLE `RemoveFile` (`FileKey` CHAR(72) NOT NULL, `Component_` CHAR(72) NOT NULL, `FileName` CHAR(255) LOCALIZABLE, `DirProperty` CHAR(72) NOT NULL, `InstallMode` SHORT NOT NULL PRIMARY KEY `FileKey`)";

    internal const string CreateCreateFolderTable =
        "CREATE TABLE `CreateFolder` (`Directory_` CHAR(72) NOT NULL, `Component_` CHAR(72) NOT NULL PRIMARY KEY `Directory_`, `Component_`)";

    internal const string CreateMoveFileTable =
        "CREATE TABLE `MoveFile` (`FileKey` CHAR(72) NOT NULL, `Component_` CHAR(72) NOT NULL, `SourceName` CHAR(255) LOCALIZABLE, `SourceFolder` CHAR(72), `DestName` CHAR(255) LOCALIZABLE, `DestFolder` CHAR(72) NOT NULL, `Options` SHORT NOT NULL PRIMARY KEY `FileKey`)";

    internal const string CreateDuplicateFileTable =
        "CREATE TABLE `DuplicateFile` (`FileKey` CHAR(72) NOT NULL, `Component_` CHAR(72) NOT NULL, `File_` CHAR(72) NOT NULL, `DestFolder` CHAR(72), `DestName` CHAR(255) LOCALIZABLE PRIMARY KEY `FileKey`)";

    internal const string CreateConditionTable =
        "CREATE TABLE `Condition` (`Feature_` CHAR(38) NOT NULL, `Level` SHORT NOT NULL, `Condition` CHAR(255) NOT NULL PRIMARY KEY `Feature_`, `Level`)";

    internal const string CreateClassTable =
        "CREATE TABLE `Class` (`CLSID` CHAR(38) NOT NULL, `Context` CHAR(32) NOT NULL, `Component_` CHAR(72) NOT NULL, `ProgId_Default` CHAR(255), `Description` CHAR(255), `AppId_` CHAR(38), `FileTypeMask` CHAR(255), `Icon_` CHAR(72), `IconIndex` SHORT, `DefInprocHandler` CHAR(32), `Argument` CHAR(255), `Feature_` CHAR(38) NOT NULL PRIMARY KEY `CLSID`, `Context`, `Component_`)";

    internal const string CreateTypeLibTable =
        "CREATE TABLE `TypeLib` (`LibID` CHAR(38) NOT NULL, `Language` SHORT NOT NULL, `Component_` CHAR(72) NOT NULL, `Version` LONG, `Description` CHAR(255), `Directory_` CHAR(72), `Feature_` CHAR(38) NOT NULL, `Cost` LONG PRIMARY KEY `LibID`, `Language`, `Component_`)";

    internal const string CreateMsiAssemblyTable =
        "CREATE TABLE `MsiAssembly` (`Component_` CHAR(72) NOT NULL, `Feature_` CHAR(38) NOT NULL, `File_Manifest` CHAR(72), `File_Application` CHAR(72), `Attributes` SHORT PRIMARY KEY `Component_`)";

    internal const string CreateMsiAssemblyNameTable =
        "CREATE TABLE `MsiAssemblyName` (`Component_` CHAR(72) NOT NULL, `Name` CHAR(255) NOT NULL, `Value` CHAR(255) NOT NULL PRIMARY KEY `Component_`, `Name`)";

    // UI tables
    internal const string CreateDialogTable =
        "CREATE TABLE `Dialog` (`Dialog` CHAR(72) NOT NULL, `HCentering` SHORT NOT NULL, `VCentering` SHORT NOT NULL, `Width` SHORT NOT NULL, `Height` SHORT NOT NULL, `Attributes` LONG, `Title` CHAR(128) LOCALIZABLE, `Control_First` CHAR(50) NOT NULL, `Control_Default` CHAR(50), `Control_Cancel` CHAR(50) PRIMARY KEY `Dialog`)";

    internal const string CreateControlTable =
        "CREATE TABLE `Control` (`Dialog_` CHAR(72) NOT NULL, `Control` CHAR(50) NOT NULL, `Type` CHAR(20) NOT NULL, `X` SHORT NOT NULL, `Y` SHORT NOT NULL, `Width` SHORT NOT NULL, `Height` SHORT NOT NULL, `Attributes` LONG, `Property` CHAR(50), `Text` LONGCHAR LOCALIZABLE, `Control_Next` CHAR(50), `Help` CHAR(255) LOCALIZABLE PRIMARY KEY `Dialog_`, `Control`)";

    internal const string CreateControlEventTable =
        "CREATE TABLE `ControlEvent` (`Dialog_` CHAR(72) NOT NULL, `Control_` CHAR(50) NOT NULL, `Event` CHAR(50) NOT NULL, `Argument` CHAR(255) NOT NULL, `Condition` CHAR(255), `Ordering` SHORT PRIMARY KEY `Dialog_`, `Control_`, `Event`, `Argument`, `Condition`)";

    internal const string CreateControlConditionTable =
        "CREATE TABLE `ControlCondition` (`Dialog_` CHAR(72) NOT NULL, `Control_` CHAR(50) NOT NULL, `Action` CHAR(50) NOT NULL, `Condition` CHAR(255) NOT NULL PRIMARY KEY `Dialog_`, `Control_`, `Action`, `Condition`)";

    internal const string CreateEventMappingTable =
        "CREATE TABLE `EventMapping` (`Dialog_` CHAR(72) NOT NULL, `Control_` CHAR(50) NOT NULL, `Event` CHAR(50) NOT NULL, `Attribute` CHAR(50) NOT NULL PRIMARY KEY `Dialog_`, `Control_`, `Event`)";

    internal const string CreateTextStyleTable =
        "CREATE TABLE `TextStyle` (`TextStyle` CHAR(72) NOT NULL, `FaceName` CHAR(32) NOT NULL, `Size` SHORT NOT NULL, `Color` LONG, `StyleBits` SHORT PRIMARY KEY `TextStyle`)";

    internal const string CreateUITextTable =
        "CREATE TABLE `UIText` (`Key` CHAR(72) NOT NULL, `Text` CHAR(255) LOCALIZABLE PRIMARY KEY `Key`)";

    internal const string CreateFalkForgeIntegrityTable =
        "CREATE TABLE `_FalkForgeIntegrity` (`Id` CHAR(72) NOT NULL, `Format` CHAR(64) NOT NULL, `Data` LONGCHAR NOT NULL PRIMARY KEY `Id`)";
}