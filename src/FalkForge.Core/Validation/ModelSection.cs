namespace FalkForge.Validation;

/// <summary>
/// Logical section of the model that a rule targets.
/// Used for filtering and documentation.
/// </summary>
public enum ModelSection
{
    Package,
    Feature,
    Component,
    File,
    Service,
    Registry,
    Shortcut,
    CustomAction,
    CustomTable,
    CustomDialog,
    Sequence,
    MajorUpgrade,
    Property,
    LaunchCondition,
    Signing,
    MediaTemplate,
    MergeModule,
    Patch,
    Transform,
    Assembly,
    Font,
    IniFile,
    Permission,
    FileAssociation,
    ServiceControl,
    ServiceDependency,
    RemoveRegistry,
    RemoveFile,
    CreateFolder,
    MoveFile,
    DuplicateFile,
    Extension_Util,
    Extension_Dependency,
    Extension_Firewall,
    Extension_DotNet,
    Extension_Iis,
    Extension_Sql
}
