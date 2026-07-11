namespace FalkForge.Compiler.Msi.Recipe;

/// <summary>
/// Cached <see cref="TableId"/> instances for every fixed MSI table name
/// referenced by two or more recipe producers (either as the producer's own
/// table or as a foreign-key target). Centralizing these avoids re-validating
/// the same literal table name via <see cref="TableId.Create"/> in every
/// producer's <c>BuildSchema</c> and <c>Produce</c> methods. All entries are
/// known-valid MSI identifiers, so unwrapping <see cref="Result{T}.Value"/> at
/// static-init time is safe.
/// </summary>
internal static class WellKnownTableIds
{
    internal static readonly TableId AppId = TableId.Create("AppId").Value;
    internal static readonly TableId Binary = TableId.Create("Binary").Value;
    internal static readonly TableId Class = TableId.Create("Class").Value;
    internal static readonly TableId Component = TableId.Create("Component").Value;
    internal static readonly TableId Condition = TableId.Create("Condition").Value;
    internal static readonly TableId Control = TableId.Create("Control").Value;
    internal static readonly TableId ControlCondition = TableId.Create("ControlCondition").Value;
    internal static readonly TableId ControlEvent = TableId.Create("ControlEvent").Value;
    internal static readonly TableId CreateFolder = TableId.Create("CreateFolder").Value;
    internal static readonly TableId CustomAction = TableId.Create("CustomAction").Value;
    internal static readonly TableId Dialog = TableId.Create("Dialog").Value;
    internal static readonly TableId Directory = TableId.Create("Directory").Value;
    internal static readonly TableId DuplicateFile = TableId.Create("DuplicateFile").Value;
    internal static readonly TableId Environment = TableId.Create("Environment").Value;
    internal static readonly TableId EventMapping = TableId.Create("EventMapping").Value;
    internal static readonly TableId Extension = TableId.Create("Extension").Value;
    internal static readonly TableId Feature = TableId.Create("Feature").Value;
    internal static readonly TableId FeatureComponents = TableId.Create("FeatureComponents").Value;
    internal static readonly TableId File = TableId.Create("File").Value;
    internal static readonly TableId Font = TableId.Create("Font").Value;
    internal static readonly TableId Icon = TableId.Create("Icon").Value;
    internal static readonly TableId IniFile = TableId.Create("IniFile").Value;
    internal static readonly TableId InstallExecuteSequence = TableId.Create("InstallExecuteSequence").Value;
    internal static readonly TableId InstallUISequence = TableId.Create("InstallUISequence").Value;
    internal static readonly TableId LaunchCondition = TableId.Create("LaunchCondition").Value;
    internal static readonly TableId LockPermissions = TableId.Create("LockPermissions").Value;
    internal static readonly TableId Media = TableId.Create("Media").Value;
    internal static readonly TableId MIME = TableId.Create("MIME").Value;
    internal static readonly TableId MoveFile = TableId.Create("MoveFile").Value;
    internal static readonly TableId MsiAssembly = TableId.Create("MsiAssembly").Value;
    internal static readonly TableId MsiAssemblyName = TableId.Create("MsiAssemblyName").Value;
    internal static readonly TableId MsiLockPermissionsEx = TableId.Create("MsiLockPermissionsEx").Value;
    internal static readonly TableId MsiServiceConfigFailureActions = TableId.Create("MsiServiceConfigFailureActions").Value;
    internal static readonly TableId ProgId = TableId.Create("ProgId").Value;
    internal static readonly TableId Property = TableId.Create("Property").Value;
    internal static readonly TableId Registry = TableId.Create("Registry").Value;
    internal static readonly TableId RemoveFile = TableId.Create("RemoveFile").Value;
    internal static readonly TableId RemoveIniFile = TableId.Create("RemoveIniFile").Value;
    internal static readonly TableId RemoveRegistry = TableId.Create("RemoveRegistry").Value;
    internal static readonly TableId ServiceControl = TableId.Create("ServiceControl").Value;
    internal static readonly TableId ServiceInstall = TableId.Create("ServiceInstall").Value;
    internal static readonly TableId Shortcut = TableId.Create("Shortcut").Value;
    internal static readonly TableId TextStyle = TableId.Create("TextStyle").Value;
    internal static readonly TableId TypeLib = TableId.Create("TypeLib").Value;
    internal static readonly TableId UIText = TableId.Create("UIText").Value;
    internal static readonly TableId Upgrade = TableId.Create("Upgrade").Value;
    internal static readonly TableId Verb = TableId.Create("Verb").Value;
}
